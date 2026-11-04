namespace ApiGestion.Tests;

using System.Text.Json;
using ApiGestion.Services;
using Microsoft.AspNetCore.Mvc.ModelBinding;

// HU-090: los 400 de validación responden { mensaje, errores } en español y sin tipos internos. Las entradas se
// arman con los mismos textos y claves que deja el framework al leer un cuerpo JSON.
public class RespuestaDeValidacionTests
{
    private static RespuestaValidacion Con(params (string Clave, string Mensaje)[] errores)
    {
        var modelState = new ModelStateDictionary();
        foreach (var (clave, mensaje) in errores) modelState.AddModelError(clave, mensaje);
        return RespuestaDeValidacion.Cuerpo(modelState);
    }

    [Fact]
    public void UnPorcentajeConTexto_NombraElDatoYNoMuestraElTipoInterno()
    {
        var respuesta = Con(("$.percentage",
            "The JSON value could not be converted to System.Nullable`1[System.Decimal]. Path: $.percentage | LineNumber: 0 | BytePositionInLine: 24."));

        Assert.Equal("El dato «percentage» no tiene un formato válido.", respuesta.Mensaje);
        Assert.DoesNotContain("System.", respuesta.Mensaje);
    }

    [Fact]
    public void UnaFechaIlegibleEnElAltaDeJugador_SeInformaEnEspanol()
    {
        var respuesta = Con(("$.fechaNacimiento",
            "The JSON value could not be converted to System.DateTime. Path: $.fechaNacimiento | LineNumber: 0 | BytePositionInLine: 40."));

        Assert.Equal("El dato «fechaNacimiento» no tiene un formato válido.", respuesta.Mensaje);
    }

    [Fact]
    public void UnJsonRoto_DaUnSoloMensaje_AunqueArrastreOtrosErrores()
    {
        var respuesta = Con(
            ("$", "'x' is an invalid start of a property name. Expected a '\"'. Path: $ | LineNumber: 0 | BytePositionInLine: 1."),
            ("dto", "The dto field is required."));

        Assert.Equal(RespuestaDeValidacion.MensajeJsonInvalido, respuesta.Mensaje);
        Assert.DoesNotContain("Expected", respuesta.Mensaje);
    }

    [Theory]
    [InlineData("dto", "The dto field is required.")]
    [InlineData("", "A non-empty request body is required.")]
    public void SinCuerpo_SeInformaQueFaltanLosDatos(string clave, string mensaje)
    {
        Assert.Equal(RespuestaDeValidacion.MensajeSinCuerpo, Con((clave, mensaje)).Mensaje);
    }

    [Fact]
    public void UnDatoObligatorioEnIngles_SeTraduce()
    {
        Assert.Equal("Falta el dato «Reason».", Con(("Reason", "The Reason field is required.")).Mensaje);
    }

    [Fact]
    public void UnValorDeRutaOConsultaInvalido_SeTraduce()
    {
        Assert.Equal("El valor «abc» no es válido para «page».", Con(("page", "The value 'abc' is not valid for page.")).Mensaje);
    }

    [Fact]
    public void LosMensajesPropiosDelDto_LleganTalCual()
    {
        var respuesta = Con(
            ("Percentage", "Indicá el porcentaje de la bonificación."),
            ("EndDate", "La fecha de fin no puede ser anterior a la de inicio."));

        // El orden de los campos lo decide el framework: se controla que estén los dos, tal cual.
        Assert.Contains("Indicá el porcentaje de la bonificación.", respuesta.Mensaje);
        Assert.Contains("La fecha de fin no puede ser anterior a la de inicio.", respuesta.Mensaje);
        Assert.Equal(new[] { "Indicá el porcentaje de la bonificación." }, respuesta.Errores["Percentage"]);
    }

    [Fact]
    public void UnMensajeEnInglesQueNoSeReconoce_NoSeMuestra()
    {
        var respuesta = Con(("Amount", "The field Amount must be between 1 and 999999."));

        Assert.Equal("El dato «Amount» no es válido.", respuesta.Mensaje);
    }

    // Becados y Descuentos (y el resto de las pantallas) leen error.error.mensaje: la clave viaja en minúscula.
    [Fact]
    public void ElCuerpoSeSerializaConLaClaveMensaje()
    {
        var json = JsonSerializer.Serialize(Con(("Percentage", "Indicá el porcentaje de la bonificación.")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Indicá el porcentaje de la bonificación.", doc.RootElement.GetProperty("mensaje").GetString());
        Assert.True(doc.RootElement.TryGetProperty("errores", out _));
    }
}
