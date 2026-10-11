using ServiceLibrary;

namespace ApiGestion.Models;

// One fee settled by POST api/pagos/cobro.
public class CollectedFeeDTO
{
    public int IdPago { get; set; }
    public string Periodo { get; set; } = "";
    public decimal Monto { get; set; }
    public string Estado { get; set; } = "";

    public static CollectedFeeDTO FromEntity(CuotaCobrada fee)
    {
        return new CollectedFeeDTO
        {
            IdPago = fee.IdPago,
            Periodo = fee.Periodo,
            Monto = fee.Monto,
            Estado = fee.Estado
        };
    }
}
