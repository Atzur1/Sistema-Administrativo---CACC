using EntityLibrary;

namespace ApiGestion.Models
{
    // HU-018: lo que consume el panel de Recaudación Mensual. Los porcentajes
    // no viajan: los calcula la pantalla a partir de estos números, que ya
    // vienen cuadrados de la base.
    // HU-107: ya no trae el detalle de cobros; la tabla lo pide paginado a
    // api/recaudacion/pagos. Estos números son siempre del período completo.
    public class RecaudacionResponseDTO
    {
        public decimal Total { get; set; }
        public decimal Efectivo { get; set; }
        public decimal Transferencia { get; set; }
        public int Pagos { get; set; }
        public int PagosEfectivo { get; set; }
        public int PagosTransferencia { get; set; }

        public static RecaudacionResponseDTO FromEntity(RecaudacionPeriodo periodo)
        {
            return new RecaudacionResponseDTO
            {
                Total = periodo.Total,
                Efectivo = periodo.Efectivo,
                Transferencia = periodo.Transferencia,
                Pagos = periodo.Pagos,
                PagosEfectivo = periodo.PagosEfectivo,
                PagosTransferencia = periodo.PagosTransferencia
            };
        }
    }

    // HU-107: una página de "Pagos del período". Mismos nombres que "Pagos registrados" (HU-088).
    public class PeriodPaymentsPageResponseDTO
    {
        public List<PeriodPaymentDTO> Items { get; set; } = new List<PeriodPaymentDTO>();

        // Cobros de todo el período, no de la página
        public int Total { get; set; }
        public int Pagina { get; set; }
        public int TamanioPagina { get; set; }
        public int TotalPaginas { get; set; }

        public static PeriodPaymentsPageResponseDTO FromEntity(PagosDelPeriodoPagina pagina)
        {
            return new PeriodPaymentsPageResponseDTO
            {
                Items = pagina.Items.Select(PeriodPaymentDTO.FromEntity).ToList(),
                Total = pagina.Total,
                Pagina = pagina.Pagina,
                TamanioPagina = pagina.TamanioPagina,
                TotalPaginas = pagina.TotalPaginas
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

        // "2026-10-31": el día del cobro, el que lo ubica en el período
        public DateOnly FechaPago { get; set; }

        // "2026-10-31T14:35:12-03:00": hora real de registro, ya en hora del club y con su corrimiento.
        // La pantalla muestra estos dígitos tal cual, sin pasarlos por la zona del navegador.
        // Null en los pagos anteriores a la columna fecha_hora_registro.
        public DateTimeOffset? FechaHoraRegistro { get; set; }

        public static PeriodPaymentDTO FromEntity(PagoDelPeriodo pago)
        {
            return new PeriodPaymentDTO
            {
                IdPago = pago.IdPago,
                Jugador = pago.Jugador,
                Dni = pago.Dni,
                Categoria = pago.Categoria,
                Monto = pago.Monto,
                MedioPago = pago.MedioPago,
                FechaPago = DateOnly.FromDateTime(pago.FechaPago),
                FechaHoraRegistro = pago.FechaHoraRegistro is DateTime registro
                    ? RelojNegocio.EnHoraDelClub(registro)
                    : null
            };
        }
    }
}
