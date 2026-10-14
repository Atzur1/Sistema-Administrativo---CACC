using EntityLibrary;

namespace ApiGestion.Models;

// Paged payment history of a player (GET api/jugadores/{id}/historial-pagos), newest first.
public class PaymentHistoryResponseDTO
{
    public List<PaymentHistoryItemDTO> Items { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }

    public static PaymentHistoryResponseDTO FromEntity(HistorialPagosResultado history)
    {
        return new PaymentHistoryResponseDTO
        {
            Items = history.Items.Select(PaymentHistoryItemDTO.FromEntity).ToList(),
            Total = history.Total,
            Page = history.Page,
            PageSize = history.PageSize
        };
    }
}

// One paid period, with every partial payment it received.
public class PaymentHistoryItemDTO
{
    public string Periodo { get; set; } = "";
    public string Concepto { get; set; } = "";
    public decimal MontoTotal { get; set; }
    public decimal MontoOriginal { get; set; }
    public bool TieneBeneficio { get; set; }
    public string? MotivoBeneficio { get; set; }
    public string? TipoValorBeneficio { get; set; }
    public decimal? PorcentajeBeneficio { get; set; }
    public decimal? MontoFijoBeneficio { get; set; }
    public List<PaymentHistoryPartialPaymentDTO> Abonos { get; set; } = new();

    public static PaymentHistoryItemDTO FromEntity(PagoHistorialItem item)
    {
        return new PaymentHistoryItemDTO
        {
            Periodo = item.Periodo,
            Concepto = item.Concepto,
            MontoTotal = item.MontoTotal,
            MontoOriginal = item.MontoOriginal,
            TieneBeneficio = item.TieneBeneficio,
            MotivoBeneficio = item.MotivoBeneficio,
            TipoValorBeneficio = item.TipoValorBeneficio,
            PorcentajeBeneficio = item.PorcentajeBeneficio,
            MontoFijoBeneficio = item.MontoFijoBeneficio,
            Abonos = item.Abonos.Select(PaymentHistoryPartialPaymentDTO.FromEntity).ToList()
        };
    }
}

public class PaymentHistoryPartialPaymentDTO
{
    // PAGOS row of the payment: the id sent to POST api/pagos/{id}/anular
    public int IdPago { get; set; }
    public decimal Monto { get; set; }
    public string MetodoPago { get; set; } = "";
    public DateTime FechaPago { get; set; }
    public DateTime? FechaHoraRegistro { get; set; }
    public string? ResponsableNombre { get; set; }
    public string? ResponsableApellido { get; set; }

    public static PaymentHistoryPartialPaymentDTO FromEntity(PagoHistorialAbono payment)
    {
        return new PaymentHistoryPartialPaymentDTO
        {
            IdPago = payment.IdPago,
            Monto = payment.Monto,
            MetodoPago = payment.MetodoPago,
            FechaPago = payment.FechaPago,
            FechaHoraRegistro = payment.FechaHoraRegistro,
            ResponsableNombre = payment.ResponsableNombre,
            ResponsableApellido = payment.ResponsableApellido
        };
    }
}
