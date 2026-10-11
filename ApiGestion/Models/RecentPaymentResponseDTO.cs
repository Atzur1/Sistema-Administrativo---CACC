using EntityLibrary;

namespace ApiGestion.Models;

// A registered payment ("Últimos pagos" and the "Pagos registrados" table).
public class RecentPaymentResponseDTO
{
    public int IdPago { get; set; }
    public int IdJugador { get; set; }
    public string NombreCompleto { get; set; } = "";
    public string Categoria { get; set; } = "";
    public string Periodo { get; set; } = "";
    public string MetodoPago { get; set; } = "";
    public decimal Monto { get; set; }

    // "Pagado" | "Parcial"
    public string Estado { get; set; } = "";
    public DateTime FechaPago { get; set; }

    // "Cuota" | "Inscripcion"
    public string Concepto { get; set; } = "";
    public DateTime? FechaHoraRegistro { get; set; }
    public string? ResponsableNombre { get; set; }
    public string? ResponsableApellido { get; set; }

    public static RecentPaymentResponseDTO FromEntity(PagoReciente payment)
    {
        return new RecentPaymentResponseDTO
        {
            IdPago = payment.IdPago,
            IdJugador = payment.IdJugador,
            NombreCompleto = payment.NombreCompleto,
            Categoria = payment.Categoria,
            Periodo = payment.Periodo,
            MetodoPago = payment.MetodoPago,
            Monto = payment.Monto,
            Estado = payment.Estado,
            FechaPago = payment.FechaPago,
            Concepto = payment.Concepto,
            FechaHoraRegistro = payment.FechaHoraRegistro,
            ResponsableNombre = payment.ResponsableNombre,
            ResponsableApellido = payment.ResponsableApellido
        };
    }
}
