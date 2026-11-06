namespace ApiGestion.Tests;

using System.Reflection;
using ApiGestion.Controllers;
using DaoLibrary;
using EntityLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

// POST api/jugadores/{id}/anular-alta: solo SuperAdmin, y el mensaje explica qué pasó o por qué no se pudo.
public class JugadoresControllerAnularAltaTests
{
    private static object? Propiedad(object? valor, string nombre) => valor?.GetType().GetProperty(nombre)?.GetValue(valor);

    private static IActionResult Anular(ResultadoAnulacionAlta resultado) =>
        new JugadoresController(new JugadoresDaoFalso(resultado), null!).AnularAlta(7); // esta accion no usa IPagosDao

    [Fact]
    public void SoloLoPuedeHacerElSuperAdmin_PorPost()
    {
        MethodInfo accion = typeof(JugadoresController).GetMethod(nameof(JugadoresController.AnularAlta))!;

        Assert.Equal("1", accion.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.Equal("POST", Assert.Single(accion.GetCustomAttributes<HttpMethodAttribute>()).HttpMethods.Single());
        Assert.Equal("{id:int}/anular-alta", accion.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Fact]
    public void UnJugadorQueNoExiste_Da404()
    {
        var resultado = Anular(new ResultadoAnulacionAlta(AnularAltaResultado.NoEncontrado));

        Assert.IsType<NotFoundObjectResult>(resultado);
    }

    [Fact]
    public void ConMovimientos_Da409YDiceCualesSonYQueCorrespondeLaBaja()
    {
        var resultado = Anular(new ResultadoAnulacionAlta(AnularAltaResultado.TieneMovimientos, new[] { "un pago cobrado", "asistencias registradas" }));

        var conflicto = Assert.IsType<ConflictObjectResult>(resultado);
        string mensaje = (string)Propiedad(conflicto.Value, "mensaje")!;
        Assert.Contains("un pago cobrado, asistencias registradas", mensaje);
        Assert.Contains("dalo de baja", mensaje);
        Assert.Equal(false, Propiedad(conflicto.Value, "exito"));
    }

    [Fact]
    public void Anulada_DiceCuantosCargosSeQuitaronYQueElDniQuedoLibre()
    {
        var resultado = Anular(new ResultadoAnulacionAlta(AnularAltaResultado.Ok, null, CargosEliminados: 2, PersonaEliminada: true));

        var ok = Assert.IsType<OkObjectResult>(resultado);
        Assert.Equal(2, Propiedad(ok.Value, "cargosEliminados"));
        string mensaje = (string)Propiedad(ok.Value, "mensaje")!;
        Assert.Contains("sus 2 cargos pendientes", mensaje);
        Assert.Contains("DNI quedó libre", mensaje);
    }

    [Fact]
    public void SiLaPersonaTambienEsTutorOUsuario_SeAvisaQueSeConserva()
    {
        string mensaje = JugadoresController.MensajeAnulacion(new ResultadoAnulacionAlta(AnularAltaResultado.Ok, null, 1, PersonaEliminada: false));

        Assert.Contains("su cargo pendiente", mensaje);
        Assert.Contains("persona se conserva", mensaje);
        Assert.DoesNotContain("DNI quedó libre", mensaje);
    }

    private sealed class JugadoresDaoFalso(ResultadoAnulacionAlta resultado) : IJugadoresDao
    {
        public ResultadoAnulacionAlta AnularAlta(int idJugador) => resultado;
        public IReadOnlyList<JugadorResumen> ListarJugadores() => throw new NotSupportedException();
        public JugadorResumen? ObtenerJugadorPorId(int idJugador) => throw new NotSupportedException();
        public ResultadoBajaJugador DarDeBaja(int idJugador, DateTime fechaBaja) => throw new NotSupportedException();
        public bool Reactivar(int idJugador) => throw new NotSupportedException();
    }
}
