namespace ApiGestion.Tests;

using System.ComponentModel.DataAnnotations;
using ApiGestion.Controllers;
using ApiGestion.Models;
using ApiGestion.Services;
using DaoLibrary;
using EntityLibrary;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceLibrary;

// HU-087: activación de cuenta y recuperación de contraseña (POST api/auth/account-access/complete). Los dos flujos usan
// el mismo enlace de un solo uso y la misma PoliticaContrasenia. Una contraseña que no cumple responde 400 con
// { mensaje, problemas } y NO consume el enlace: la persona corrige y vuelve a enviar con el mismo. El login no aplica la
// política: una contraseña histórica que hoy no la cumpliría sigue sirviendo para entrar.
public class AccesoCuentaTests
{
    private const string Mensaje = "mensaje";

    // Enlaces de prueba en memoria, con las mismas condiciones que AccountAccessDao: el titular se lee sin consumir el
    // enlace, y completar lo consume una sola vez (solo si sigue vigente).
    private sealed class EnlacesEnMemoria : ITokensAccesoCuenta
    {
        public sealed class Enlace
        {
            public required string Tipo { get; init; } // ACTIVACION o RECUPERACION
            public required DatosTitular Titular { get; init; }
            public bool Vencido { get; set; }
            public bool Consumido { get; set; }
        }

        public Dictionary<string, Enlace> Enlaces { get; } = new();
        public string? HashGuardado { get; private set; }
        public int Escrituras { get; private set; }

        public (string? Nombre, string? Apellido, string? Dni, string? Email)? ObtenerTitularDelToken(string tokenHash) =>
            Enlaces.TryGetValue(tokenHash, out var e) && !e.Vencido && !e.Consumido
                ? (e.Titular.Nombre, e.Titular.Apellido, e.Titular.Dni, e.Titular.Email)
                : null;

        public bool CompleteAccountAccess(string tokenHash, string passwordHash)
        {
            if (!Enlaces.TryGetValue(tokenHash, out var e) || e.Vencido || e.Consumido) return false;
            e.Consumido = true;
            HashGuardado = passwordHash;
            Escrituras++;
            return true;
        }
    }

    private static readonly DatosTitular Jose = new("José", "Pérez", "40.123.456", "juan.perez@dominio.com");
    private const string Valida = "Tribuna-Norte-77";

    // Un token "de verdad" (43 caracteres, como TokenAccesoCuenta.Generar) y su hash, que es lo que guarda la base.
    private static (string Token, string Hash, EnlacesEnMemoria Enlaces) EnlaceVigente(string tipo)
    {
        var (token, hash) = TokenAccesoCuenta.Generar();
        var enlaces = new EnlacesEnMemoria();
        enlaces.Enlaces[hash] = new EnlacesEnMemoria.Enlace { Tipo = tipo, Titular = Jose };
        return (token, hash, enlaces);
    }

    private static object? Propiedad(object? valor, string nombre) => valor?.GetType().GetProperty(nombre)?.GetValue(valor);

    // ===== Servicio: el flujo de los dos enlaces =====

    [Theory]
    [InlineData("ACTIVACION")]
    [InlineData("RECUPERACION")]
    public void UnaContraseniaInvalida_DevuelveLosProblemasYNoConsumeElEnlace(string tipo)
    {
        var (_, hash, enlaces) = EnlaceVigente(tipo);
        var servicio = new AccesoCuentaService(enlaces);

        var resultado = servicio.Completar(hash, "1234567890");

        Assert.False(resultado.Exito);
        Assert.Equal(TipoResultado.Invalido, resultado.Tipo);
        Assert.Contains("Tiene que contener al menos una letra.", resultado.Problemas);
        Assert.Contains("Es una contraseña demasiado común. Elegí otra.", resultado.Problemas);
        Assert.False(enlaces.Enlaces[hash].Consumido);
        Assert.Equal(0, enlaces.Escrituras);
        Assert.NotNull(enlaces.ObtenerTitularDelToken(hash)); // el enlace sigue sirviendo
    }

    [Theory]
    [InlineData("ACTIVACION")]
    [InlineData("RECUPERACION")]
    public void ConElMismoEnlace_SeCorrigeYSeCompleta_YDespuesElEnlaceYaNoSirve(string tipo)
    {
        var (_, hash, enlaces) = EnlaceVigente(tipo);
        var servicio = new AccesoCuentaService(enlaces);

        Assert.False(servicio.Completar(hash, "Perez-12345").Exito); // contiene el apellido
        var segundo = servicio.Completar(hash, Valida);

        Assert.True(segundo.Exito);
        Assert.Equal(1, enlaces.Escrituras);
        Assert.True(enlaces.Enlaces[hash].Consumido);
        // Se guarda el hash seguro, nunca el texto.
        Assert.NotEqual(Valida, enlaces.HashGuardado);
        Assert.True(PasswordHasher.VerifyPassword(Valida, enlaces.HashGuardado!));

        var tercero = servicio.Completar(hash, "Otra-Tribuna-88");
        Assert.False(tercero.Exito);
        Assert.Empty(tercero.Problemas);
        Assert.Equal("El enlace no es válido o venció.", tercero.Mensaje);
    }

    [Fact]
    public void UnEnlaceVencido_RespondeEnlaceInvalido_AunqueLaContraseniaTambienFalle()
    {
        var (_, hash, enlaces) = EnlaceVigente("RECUPERACION");
        enlaces.Enlaces[hash].Vencido = true;

        var resultado = new AccesoCuentaService(enlaces).Completar(hash, "123");

        Assert.Equal("El enlace no es válido o venció.", resultado.Mensaje);
        Assert.Empty(resultado.Problemas); // no se confunde con una contraseña inválida
        Assert.Equal(0, enlaces.Escrituras);
    }

    [Fact]
    public void SinContrasenia_EsUnaContraseniaInvalida_NoUnError()
    {
        var (_, hash, enlaces) = EnlaceVigente("ACTIVACION");

        var resultado = new AccesoCuentaService(enlaces).Completar(hash, null);

        Assert.Contains("Tiene que tener al menos 10 caracteres.", resultado.Problemas);
        Assert.False(enlaces.Enlaces[hash].Consumido);
    }

    // ===== Endpoint: 400 + { mensaje, problemas } y el mismo enlace sirve después =====

    private static AuthController Controller(ITokensAccesoCuenta enlaces, AuthDao? authDao = null)
    {
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "clave-de-prueba-de-al-menos-32-bytes-para-hmac-sha256",
            ["Jwt:Issuer"] = "ApiGestion",
            ["Jwt:Audience"] = "Portales",
        }).Build();

        return new AuthController(
            authDao ?? new AuthDao(""), config, NullLogger<AuthController>.Instance, new AccountAccessDao(""),
            new EmailLinkSender(config, NullLogger<EmailLinkSender>.Instance, new EntornoDePrueba()),
            new LoginThrottle(TimeProvider.System, new IntentosEnMemoria()),
            new AccesoCuentaService(enlaces))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    [Theory]
    [InlineData("Corta-7")]          // corta
    [InlineData("Clave-40123456")]   // con el DNI (aunque esté cargado con puntos)
    [InlineData("password123")]      // común
    [InlineData("Camioneros-1931")]  // el nombre del club
    public void AlEndpointDirecto_UnaContraseniaQueNoCumple_Responde400ConProblemas(string contrasenia)
    {
        var (token, hash, enlaces) = EnlaceVigente("ACTIVACION");

        var respuesta = Assert.IsType<BadRequestObjectResult>(
            Controller(enlaces).CompletarAcceso(new CompleteAccountAccessRequestDTO { Token = token, Password = contrasenia }));

        Assert.Equal(StatusCodes.Status400BadRequest, respuesta.StatusCode);
        var problemas = Assert.IsAssignableFrom<IEnumerable<string>>(Propiedad(respuesta.Value, "problemas"));
        Assert.NotEmpty(problemas);
        Assert.StartsWith("La contraseña no cumple los requisitos", (string)Propiedad(respuesta.Value, Mensaje)!);
        Assert.False(enlaces.Enlaces[hash].Consumido);
    }

    [Fact]
    public void ElEndpoint_TrasUn400_AceptaElMismoEnlaceConUnaContraseniaValida()
    {
        var (token, hash, enlaces) = EnlaceVigente("RECUPERACION");
        var controller = Controller(enlaces);

        Assert.IsType<BadRequestObjectResult>(controller.CompletarAcceso(new CompleteAccountAccessRequestDTO { Token = token, Password = "1234567890" }));
        var ok = Assert.IsType<OkObjectResult>(controller.CompletarAcceso(new CompleteAccountAccessRequestDTO { Token = token, Password = Valida }));

        Assert.Equal("La contraseña se actualizó. Ya puede iniciar sesión.", Propiedad(ok.Value, Mensaje));
        Assert.True(enlaces.Enlaces[hash].Consumido);
        Assert.True(PasswordHasher.VerifyPassword(Valida, enlaces.HashGuardado!));
    }

    [Fact]
    public void ElEndpoint_ConUnEnlaceInvalido_Responde400SinProblemas()
    {
        var respuesta = Assert.IsType<BadRequestObjectResult>(Controller(new EnlacesEnMemoria())
            .CompletarAcceso(new CompleteAccountAccessRequestDTO { Token = TokenAccesoCuenta.Generar().Token, Password = Valida }));

        Assert.Equal("El enlace no es válido o venció.", Propiedad(respuesta.Value, Mensaje));
        Assert.Null(Propiedad(respuesta.Value, "problemas"));
    }

    // La validación del DTO corre antes del controller: no puede cortar una contraseña corta antes de la política, porque
    // esa respuesta no traería "problemas" y la pantalla la tomaría como un enlace vencido.
    [Theory]
    [InlineData("abc12345")]
    [InlineData("")]
    public void ElDto_DejaPasarLaContraseniaParaQueLaJuzgueLaPolitica(string contrasenia)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers(ErroresDelPortal.ConfigurarMensajesDeBinding);
        using var sp = services.BuildServiceProvider();
        var contexto = new ActionContext(new DefaultHttpContext { RequestServices = sp }, new RouteData(), new ControllerActionDescriptor());

        sp.GetRequiredService<IObjectModelValidator>().Validate(contexto, null, "",
            new CompleteAccountAccessRequestDTO { Token = TokenAccesoCuenta.Generar().Token, Password = contrasenia });

        Assert.True(contexto.ModelState.IsValid, string.Join(" ", contexto.ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
    }

    // ===== Compatibilidad: el login no aplica la política nueva =====

    // AuthDao contra la base, pero con la verificación real del hash: la cuenta tiene guardada una contraseña histórica de
    // 8 caracteres, que hoy la política rechazaría.
    private sealed class CuentaHistorica(string contraseniaHistorica) : AuthDao("")
    {
        private readonly string _hash = PasswordHasher.HashPassword(contraseniaHistorica);

        public override Usuario? ValidarLogin(string identificador, string contrasenia) =>
            identificador == "30111222" && PasswordHasher.VerifyPassword(contrasenia, _hash)
                ? new Usuario { IdUsuario = 7, Dni = "30111222", Nombre = "Camila", Apellido = "Pérez", Email = "camila@cacc.test", AccesoPortal = true, RolPortal = 2, IdRol = 2 }
                : null;
    }

    [Fact]
    public void UnaContraseniaHistoricaQueNoCumpleLaPolitica_SigueSirviendoParaEntrar()
    {
        const string historica = "clave123"; // 8 caracteres
        Assert.NotEmpty(PoliticaContrasenia.Validar(historica, new DatosTitular("Camila", "Pérez", "30111222", "camila@cacc.test")));

        var controller = Controller(new EnlacesEnMemoria(), new CuentaHistorica(historica));

        var ok = Assert.IsType<OkObjectResult>(controller.Login(new LoginDTO { Username = "30111222", Password = historica }));
        Assert.False(string.IsNullOrEmpty(Assert.IsType<LoginResponseDTO>(ok.Value).Token));

        // Y una contraseña equivocada sigue siendo 401, no un 400 de política.
        Assert.IsType<UnauthorizedObjectResult>(controller.Login(new LoginDTO { Username = "30111222", Password = "otra1234" }));
    }

    [Fact]
    public void ElLoginNoTieneLaReglaDeLargoMinimoDeLaPolitica()
    {
        var minLength = typeof(LoginDTO).GetProperty(nameof(LoginDTO.Password))!.GetCustomAttributes(typeof(MinLengthAttribute), true)
            .Cast<MinLengthAttribute>().SingleOrDefault();

        Assert.True(minLength is null || minLength.Length <= 1);
    }

    // ===== Soporte =====

    private sealed class EntornoDePrueba : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "ApiGestion.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class IntentosEnMemoria : IIntentosLoginStore
    {
        private readonly Dictionary<string, (int Fallos, DateTimeOffset Desde)> _fallos = new();
        public (int Fallos, DateTimeOffset Desde)? Obtener(string clave) => _fallos.TryGetValue(clave, out var e) ? e : null;
        public void RegistrarFallo(string clave, DateTimeOffset ahora, TimeSpan ventana) =>
            _fallos[clave] = _fallos.TryGetValue(clave, out var e) ? (e.Fallos + 1, e.Desde) : (1, ahora);
        public void Limpiar(string clave) => _fallos.Remove(clave);
    }
}
