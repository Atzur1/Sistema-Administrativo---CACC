namespace ApiGestion.Tests;

using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using ApiGestion.Controllers;
using ApiGestion.Models;
using ApiGestion.Services;
using EntityLibrary;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

// HU-090: todo lo que el Portal Administrativo le muestra al usuario sale en español, con el contrato { exito, mensaje }
// y sin nombres de propiedades, tipos de .NET ni textos del framework. Los status HTTP no cambian (400 sigue siendo 400).
// La API de integración con el Deportivo tiene su propio contrato (HU-058) y queda afuera de estas reglas.
public class MensajesEnEspanolTests
{
    // Huellas de los textos que .NET genera solo, en inglés. Ninguna puede llegarle al usuario.
    private static readonly string[] TextosDelFramework =
    {
        "The ", " field ", "is required", "is invalid", "is not valid", "could not be converted", "non-empty request body",
        "must be a", "System.", "Path:", "One or more validation errors",
    };

    private static void AssertSinIngles(string? mensaje)
    {
        Assert.False(string.IsNullOrWhiteSpace(mensaje));
        foreach (string huella in TextosDelFramework)
        {
            Assert.DoesNotContain(huella, mensaje!, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static object? Propiedad(object? valor, string nombre) => valor?.GetType().GetProperty(nombre)?.GetValue(valor);

    // ===== Formato de fechas y montos de los mensajes, sin depender de la cultura del servidor =====

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    [InlineData("nl-NL")]
    [InlineData("es-AR")]
    public void FormatoVisible_FechaYMonto_SonSiempreLosDelPortal(string cultura)
    {
        CultureInfo anterior = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(cultura);
        try
        {
            Assert.Equal("01/10/2026", FormatoVisible.Fecha(new DateTime(2026, 10, 1)));
            Assert.Equal("31/12/2026", FormatoVisible.Fecha(new DateTime(2026, 12, 31, 23, 59, 59)));
            Assert.Equal("$85.000", FormatoVisible.Pesos(85000m));
            Assert.Equal("$1.234.568", FormatoVisible.Pesos(1234567.5m));
        }
        finally
        {
            CultureInfo.CurrentCulture = anterior;
        }
    }

    // ===== Validación de DTOs por el pipeline real de MVC (incluye el [Required] implícito de los string) =====

    private static ServiceProvider Mvc()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers(ErroresDelPortal.ConfigurarMensajesDeBinding);
        return services.BuildServiceProvider();
    }

    // Valida el DTO como lo hace MVC antes de entrar al controller y devuelve la respuesta 400 del portal.
    private static (BadRequestObjectResult Respuesta, string Mensaje) Validar(object dto)
    {
        using ServiceProvider sp = Mvc();
        var contexto = new ActionContext(new DefaultHttpContext { RequestServices = sp }, new RouteData(), new ControllerActionDescriptor());
        sp.GetRequiredService<IObjectModelValidator>().Validate(contexto, validationState: null, prefix: "", model: dto);
        Assert.False(contexto.ModelState.IsValid);

        var respuesta = Assert.IsType<BadRequestObjectResult>(ErroresDelPortal.RespuestaValidacion(contexto));
        Assert.Equal(false, Propiedad(respuesta.Value, "exito"));
        string mensaje = Assert.IsType<string>(Propiedad(respuesta.Value, "mensaje"));
        return (respuesta, mensaje);
    }

    public static TheoryData<string, object, string> DtosIncompletos => new()
    {
        // Becados y Descuentos
        { "beneficio sin datos", new DiscountRequestDTO { Reason = null!, ValueType = null!, StartDate = null, EndDate = null }, "Indicá el motivo de la bonificación." },
        { "beneficio fin antes que inicio", new DiscountRequestDTO { Reason = "Becado", ValueType = "%", Percentage = 50, StartDate = "2026-12-01", EndDate = "2026-11-01" }, "La fecha de fin debe ser posterior a la de inicio." },
        // Alta de jugador
        { "jugador sin datos", new PlayerRequestDTO { FirstName = null!, LastName = null!, Dni = null!, Gender = null! }, "Indicá el nombre." },
        { "jugador con DNI inválido", new PlayerRequestDTO { FirstName = "Ana", LastName = "Paz", Dni = "12.345.678", BirthDate = new DateTime(2010, 1, 1), Gender = "Femenino", CategoryId = 1 }, "El DNI debe tener 7 u 8 dígitos, sin puntos." },
        // Aranceles
        { "arancel sin tipo", new TariffChangeRequestDTO { Tipo = null!, Valor = 1000, VigenteDesde = new DateTime(2026, 12, 1) }, "Indicá el tipo de cambio del arancel." },
        { "arancel programado sin huella", new ScheduleTariffRequestDTO { Tipo = "absoluto", Huella = null!, Valor = 1000, VigenteDesde = new DateTime(2026, 12, 1) }, "Revisá el impacto del cambio antes de confirmarlo." },
        // Arancel de inscripción
        { "arancel de inscripción sin datos", new EnrollmentFeeRequestDTO(), "Indicá a qué género corresponde la inscripción." },
        // Pago de inscripción
        { "pago de inscripción sin método", new EnrollmentPaymentRequestDTO { Amount = 1000, PaymentMethod = null! }, "Indicá el método de pago." },
        { "pago de inscripción con monto cero", new EnrollmentPaymentRequestDTO { Amount = 0, PaymentMethod = "Efectivo" }, "El monto debe ser mayor a cero." },
        // Pagos de cuotas
        { "pago de cuota sin período ni método", new RegisterPaymentRequestDTO { IdJugador = 1, Periodo = null!, MetodoPago = null!, Anio = 2026, Monto = 1 }, "Indicá el período." },
        { "cobro sin cuotas", new CollectFeesRequestDTO { IdJugador = 1, IdsPago = new(), MetodoPago = "Efectivo" }, "Elegí al menos una cuota para cobrar." },
    };

    [Theory]
    [MemberData(nameof(DtosIncompletos))]
    public void ValidacionDeDto_Responde400ConMensajeEnEspanol(string caso, object dto, string esperado)
    {
        var (respuesta, mensaje) = Validar(dto);

        Assert.Equal(StatusCodes.Status400BadRequest, respuesta.StatusCode);
        Assert.Contains(esperado, mensaje);
        AssertSinIngles(mensaje);
        Assert.False(string.IsNullOrEmpty(caso));
    }

    [Fact]
    public void ValidacionDeDto_ConVariosErrores_LosInformaTodosEnUnSoloMensaje()
    {
        var (_, mensaje) = Validar(new PlayerRequestDTO { FirstName = null!, LastName = null!, Dni = null!, Gender = null! });

        Assert.Contains("Indicá el nombre.", mensaje);
        Assert.Contains("Indicá el apellido.", mensaje);
        Assert.Contains("Indicá el DNI.", mensaje);
        Assert.Contains("Indicá la categoría.", mensaje);
        AssertSinIngles(mensaje);
    }

    // ===== Errores que arma el framework antes de validar (JSON ilegible, cuerpo vacío) =====

    private static ActionContext ContextoConError(string clave, string mensajeDelFramework)
    {
        var contexto = new ActionContext(new DefaultHttpContext(), new RouteData(), new ControllerActionDescriptor
        {
            Parameters = new List<ParameterDescriptor> { new() { Name = "request", BindingInfo = new() { BindingSource = Microsoft.AspNetCore.Mvc.ModelBinding.BindingSource.Body } } },
        });
        contexto.ModelState.AddModelError(clave, mensajeDelFramework);
        return contexto;
    }

    [Theory]
    [InlineData("$.percentage", "porcentaje")]
    [InlineData("$.startDate", "fecha de inicio")]
    [InlineData("$.amount", "monto")]
    [InlineData("$.categoryId", "categoría")]
    [InlineData("$.vigenteDesde", "fecha de vigencia")]
    public void JsonConUnDatoMalFormado_NombraElDatoEnEspanol(string clave, string etiqueta)
    {
        var contexto = ContextoConError(clave, "The JSON value could not be converted to System.Nullable`1[System.Decimal]. Path: $.percentage | LineNumber: 0");

        var respuesta = Assert.IsType<BadRequestObjectResult>(ErroresDelPortal.RespuestaValidacion(contexto));
        string mensaje = (string)Propiedad(respuesta.Value, "mensaje")!;

        Assert.Equal($"El dato «{etiqueta}» no tiene un formato válido.", mensaje);
        AssertSinIngles(mensaje);
    }

    [Fact]
    public void JsonConUnDatoDesconocido_UsaUnMensajeGenericoSinNombresInternos()
    {
        var contexto = ContextoConError("$.FK_id_categoria", "The JSON value could not be converted to System.Int32.");

        string mensaje = (string)Propiedad(((BadRequestObjectResult)ErroresDelPortal.RespuestaValidacion(contexto)).Value, "mensaje")!;

        Assert.Equal("Uno de los datos enviados no tiene un formato válido.", mensaje);
        Assert.DoesNotContain("FK_id_categoria", mensaje);
    }

    [Theory]
    [InlineData("", "A non-empty request body is required.")]
    [InlineData("request", "The request field is required.")]
    [InlineData("$", "'0x' is an invalid start of a value. Path: $ | LineNumber: 0")]
    public void SinCuerpoOConJsonIlegible_PideLosDatosEnEspanol(string clave, string mensajeDelFramework)
    {
        var contexto = ContextoConError(clave, mensajeDelFramework);

        var respuesta = Assert.IsType<BadRequestObjectResult>(ErroresDelPortal.RespuestaValidacion(contexto));

        Assert.Equal("No se recibieron los datos del formulario o no tienen un formato válido.", Propiedad(respuesta.Value, "mensaje"));
    }

    // Un parámetro de ruta o de query con un valor imposible ("abc" como id) usa los textos de binding en español.
    [Fact]
    public void ValorDeRutaOQueryInvalido_UsaElTextoDeBindingEnEspanol()
    {
        using ServiceProvider sp = Mvc();
        var opciones = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MvcOptions>>().Value;
        var proveedor = opciones.ModelBindingMessageProvider;

        string[] textos =
        {
            proveedor.AttemptedValueIsInvalidAccessor("abc", "playerId"),
            proveedor.NonPropertyAttemptedValueIsInvalidAccessor("abc"),
            proveedor.UnknownValueIsInvalidAccessor("playerId"),
            proveedor.NonPropertyUnknownValueIsInvalidAccessor(),
            proveedor.ValueIsInvalidAccessor("abc"),
            proveedor.ValueMustBeANumberAccessor("playerId"),
            proveedor.NonPropertyValueMustBeANumberAccessor(),
            proveedor.MissingBindRequiredValueAccessor("playerId"),
            proveedor.MissingKeyOrValueAccessor(),
            proveedor.MissingRequestBodyRequiredValueAccessor(),
            proveedor.ValueMustNotBeNullAccessor("abc"),
        };

        foreach (string texto in textos)
        {
            AssertSinIngles(texto);
            Assert.DoesNotContain("playerId", texto);
        }
    }

    // ===== Guardián: ningún DTO de entrada del portal deja un texto por defecto del framework =====
    // Cada atributo de validación lleva su ErrorMessage en español, y cada string no anulable (que .NET marca como
    // obligatorio solo, con "The X field is required.") lleva un [Required] explícito con su mensaje.

    private static IEnumerable<Type> DtosDeEntradaDelPortal()
    {
        Type[] deIntegracion = { typeof(IntegracionJugadoresController), typeof(EstadoDeudaController) };
        return typeof(ResultadoHttp).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract && !deIntegracion.Contains(t))
            .SelectMany(c => c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .SelectMany(m => m.GetParameters())
            .Select(p => p.ParameterType)
            .Where(t => t.IsClass && t != typeof(string) && t.Namespace == "ApiGestion.Models")
            .Distinct();
    }

    [Fact]
    public void LosDtosDeEntradaDelPortal_NoUsanMensajesPorDefectoDelFramework()
    {
        var nulabilidad = new NullabilityInfoContext();
        var problemas = new List<string>();
        List<Type> dtos = DtosDeEntradaDelPortal().ToList();
        Assert.Contains(typeof(DiscountRequestDTO), dtos);
        Assert.Contains(typeof(PlayerRequestDTO), dtos);
        Assert.Contains(typeof(EnrollmentPaymentRequestDTO), dtos);

        foreach (Type dto in dtos)
        {
            foreach (PropertyInfo propiedad in dto.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite))
            {
                foreach (ValidationAttribute atributo in propiedad.GetCustomAttributes<ValidationAttribute>())
                {
                    if (string.IsNullOrWhiteSpace(atributo.ErrorMessage))
                    {
                        problemas.Add($"{dto.Name}.{propiedad.Name}: [{atributo.GetType().Name}] sin ErrorMessage");
                    }
                }

                bool stringObligatorioImplicito = propiedad.PropertyType == typeof(string)
                    && nulabilidad.Create(propiedad).WriteState == NullabilityState.NotNull;
                if (stringObligatorioImplicito && propiedad.GetCustomAttribute<RequiredAttribute>() is null)
                {
                    problemas.Add($"{dto.Name}.{propiedad.Name}: string obligatorio sin [Required(ErrorMessage = ...)]");
                }
            }
        }

        Assert.True(problemas.Count == 0, string.Join(Environment.NewLine, problemas));
    }

    // ===== Errores sin cuerpo (NotFound() vacío, 415): { exito, mensaje } en el portal, contrato propio en integración =====

    private sealed class SinCuerpo(int status) : Microsoft.AspNetCore.Mvc.Infrastructure.IClientErrorActionResult
    {
        public int? StatusCode => status;
        public Task ExecuteResultAsync(ActionContext context) => Task.CompletedTask;
    }

    [Theory]
    [InlineData(404, "No se encontró lo que buscabas.")]
    [InlineData(415, "No se recibieron los datos del formulario o no tienen un formato válido.")]
    [InlineData(405, "Ocurrió un error al procesar la solicitud.")]
    public void ErrorSinCuerpoEnElPortal_RespondeMensajeEnEspanolConElMismoStatus(int status, string esperado)
    {
        var fabrica = new IntegrationClientErrorFactory(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        var http = new DefaultHttpContext();
        http.Request.Path = "/api/players/3/discount";

        var resultado = Assert.IsType<ObjectResult>(fabrica.GetClientError(new ActionContext(http, new RouteData(), new ActionDescriptor()), new SinCuerpo(status)));

        Assert.Equal(status, resultado.StatusCode);
        Assert.Equal(false, Propiedad(resultado.Value, "exito"));
        Assert.Equal(esperado, Propiedad(resultado.Value, "mensaje"));
    }

}
