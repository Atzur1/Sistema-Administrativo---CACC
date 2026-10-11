using EntityLibrary;

namespace ApiGestion.Models
{
    // HU-018: lo que consume el panel de Recaudación Mensual. Los porcentajes
    // no viajan: los calcula la pantalla a partir de estos números, que ya
    // vienen cuadrados de la base.
    public class RecaudacionResponseDTO
    {
        public decimal Total { get; set; }
        public decimal Efectivo { get; set; }
        public decimal Transferencia { get; set; }
        public int Pagos { get; set; }
        public int PagosEfectivo { get; set; }
        public int PagosTransferencia { get; set; }
        public List<PeriodPaymentDTO> Detalle { get; set; } = new List<PeriodPaymentDTO>();

        public static RecaudacionResponseDTO FromEntity(RecaudacionPeriodo periodo)
        {
            return new RecaudacionResponseDTO
            {
                Total = periodo.Total,
                Efectivo = periodo.Efectivo,
                Transferencia = periodo.Transferencia,
                Pagos = periodo.Pagos,
                PagosEfectivo = periodo.PagosEfectivo,
                PagosTransferencia = periodo.PagosTransferencia,
                Detalle = periodo.Detalle.Select(PeriodPaymentDTO.FromEntity).ToList()
            };
        }
    }

    public class PeriodPaymentDTO
    {
        public long IdPago { get; set; }
        public string Jugador { get; set; } = string.Empty;
        public string Dni { get; set; } = string.Empty;
        public string Categoria { get; set; } = string.Empty;
        public decimal Monto { get; set; }
        public string MedioPago { get; set; } = string.Empty;

        public static PeriodPaymentDTO FromEntity(PagoDelPeriodo pago)
        {
            return new PeriodPaymentDTO
            {
                IdPago = pago.IdPago,
                Jugador = pago.Jugador,
                Dni = pago.Dni,
                Categoria = pago.Categoria,
                Monto = pago.Monto,
                MedioPago = pago.MedioPago
            };
        }
    }
}
