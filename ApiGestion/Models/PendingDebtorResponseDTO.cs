using EntityLibrary;

namespace ApiGestion.Models;

// Row of "Pendientes de cobro": debt grouped by player.
public class PendingDebtorResponseDTO
{
    public int IdJugador { get; set; }
    public string NombreCompleto { get; set; } = "";
    public string Dni { get; set; } = "";
    public int IdCategoria { get; set; }
    public string Categoria { get; set; } = "";
    public decimal MontoTotal { get; set; }
    public int CantidadCuotas { get; set; }
    public int CantidadVencidas { get; set; }

    // null while the player is active
    public DateTime? FechaBaja { get; set; }

    // El estado deportivo que calcula el backend (habilitado, solo_entrenamientos, inhabilitado o baja): la misma regla y los
    // mismos textos que la API de integración con el Portal Deportivo. La pantalla lo muestra, no lo recalcula.
    public string EstadoDeportivo { get; set; } = EstadoDeportivoTexto.Habilitado;

    public static PendingDebtorResponseDTO FromEntity(PendienteJugador debtor)
    {
        return new PendingDebtorResponseDTO
        {
            IdJugador = debtor.IdJugador,
            NombreCompleto = debtor.NombreCompleto,
            Dni = debtor.Dni,
            IdCategoria = debtor.IdCategoria,
            Categoria = debtor.Categoria,
            MontoTotal = debtor.MontoTotal,
            CantidadCuotas = debtor.CantidadCuotas,
            CantidadVencidas = debtor.CantidadVencidas,
            FechaBaja = debtor.FechaBaja,
            EstadoDeportivo = EstadoDeportivoTexto.De(
                new EstadoDeudaJugador(debtor.IdJugador, debtor.Dni, debtor.CantidadVencidas, debtor.MontoTotal, debtor.FechaBaja).Estado)
        };
    }
}
