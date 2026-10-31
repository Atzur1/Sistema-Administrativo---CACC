namespace ApiGestion.Tests;

using ApiGestion.Models;
using EntityLibrary;

// Deudas y Morosidad muestra el estado deportivo que calcula el backend (HU-028): es la misma regla y los mismos textos que
// usa la API de integración con el Portal Deportivo, y no se recalcula en el navegador.
public class EstadoDeportivoEnPantallasTests
{
    [Theory]
    [InlineData(0, null, "habilitado")]
    [InlineData(1, null, "solo_entrenamientos")]
    [InlineData(2, null, "inhabilitado")]
    [InlineData(5, null, "inhabilitado")]
    [InlineData(2, "2026-09-01", "baja")]   // quien se fue del club no tiene situación deportiva, aunque deba
    public void LosPendientesDeCobroTraenElEstadoDelBackend(int vencidas, string? baja, string esperado)
    {
        var deudor = new PendienteJugador
        {
            IdJugador = 7, Dni = "40111222", CantidadVencidas = vencidas, CantidadCuotas = vencidas, MontoTotal = 85_000m,
            FechaBaja = baja is null ? null : DateTime.Parse(baja)
        };

        Assert.Equal(esperado, PendingDebtorResponseDTO.FromEntity(deudor).EstadoDeportivo);
    }

    [Theory]
    [InlineData(2, null)]
    [InlineData(1, null)]
    [InlineData(0, null)]
    [InlineData(3, "2026-09-01")]
    public void ElEstadoDeLasPantallasEsElMismoQueElDeLaApiDeIntegracion(int vencidas, string? baja)
    {
        DateTime? fechaBaja = baja is null ? null : DateTime.Parse(baja);
        var contrato = EstadoDeudaResponseDTO.FromEntity(new EstadoDeudaJugador(7, "40111222", vencidas, 85_000m, fechaBaja));
        var pantalla = PendingDebtorResponseDTO.FromEntity(new PendienteJugador
        {
            IdJugador = 7, Dni = "40111222", CantidadVencidas = vencidas, MontoTotal = 85_000m, FechaBaja = fechaBaja
        });

        Assert.Equal(contrato.Estado, pantalla.EstadoDeportivo);
    }

    [Fact]
    public void LosTextosDelEstadoSonLosDelContratoV1()
    {
        Assert.Equal("habilitado", EstadoDeportivoTexto.De(EstadoDeportivo.Habilitado));
        Assert.Equal("solo_entrenamientos", EstadoDeportivoTexto.De(EstadoDeportivo.SoloEntrenamientos));
        Assert.Equal("inhabilitado", EstadoDeportivoTexto.De(EstadoDeportivo.Inhabilitado));
        Assert.Equal("baja", EstadoDeportivoTexto.De(EstadoDeportivo.Baja));
    }
}
