namespace ApiGestion.Models;

// Outbound shape of GET api/pagos/jugador/{id}/estado-de-cuenta (HU-024)
public class PlayerStatementDto
{
    public int PlayerId { get; set; }
    public string PlayerFullName { get; set; } = "";
    public string Dni { get; set; } = "";
    // What is left to pay on Pendiente and Vencido fees, benefits and partial payments applied
    public decimal TotalDebtAmount { get; set; }
    public List<FeeItemDto> Fees { get; set; } = new();
}

// One monthly fee of the statement, oldest first
public class FeeItemDto
{
    // Pendiente/Vencido: the PAGOS row to charge. Pagado: the last payment of the period.
    public int Id { get; set; }
    public string PeriodName { get; set; } = "";
    // Frozen amount the fee was issued with (PAGOS.monto_base)
    public decimal Amount { get; set; }
    // What would be charged today (0 when paid or covered by a benefit)
    public decimal AmountDue { get; set; }
    public decimal AmountPaid { get; set; }
    // Last day of the month the fee covers: after it, an unpaid fee is Vencido
    public string DueDate { get; set; } = "";
    // "Pagado", "Pendiente" or "Vencido"
    public string Status { get; set; } = "";
    public string? PaidAt { get; set; }
    public string? PaymentMethod { get; set; }
    public bool CoveredByBenefit { get; set; }
    public string? BenefitReason { get; set; }
}
