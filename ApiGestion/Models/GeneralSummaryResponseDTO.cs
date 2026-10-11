using EntityLibrary;

namespace ApiGestion.Models;

// Everything the "Resumen General" dashboard needs in a single call.
public class GeneralSummaryResponseDTO
{
    public int TotalJugadores { get; set; }
    public int CantidadCategorias { get; set; }
    public int PagosDelMes { get; set; }
    public decimal RecaudadoAnioActual { get; set; }
    public decimal IngresadoEsteMes { get; set; }
    public decimal IngresadoMesAnterior { get; set; }
    public decimal DeudaAcumulada { get; set; }
    public int JugadoresSinDeuda { get; set; }
    public int JugadoresConUnaImpaga { get; set; }
    public int JugadoresConDosOMasImpagas { get; set; }
    public List<MonthlyCollectionPointDTO> RecaudacionMensual { get; set; } = new();
    public List<MonthlyCoveragePointDTO> CoberturaPagoMensual { get; set; } = new();
    public List<PendingDebtorResponseDTO> MayorDeudaPendiente { get; set; } = new();

    public static GeneralSummaryResponseDTO FromEntity(ResumenGeneralInfo summary)
    {
        return new GeneralSummaryResponseDTO
        {
            TotalJugadores = summary.TotalJugadores,
            CantidadCategorias = summary.CantidadCategorias,
            PagosDelMes = summary.PagosDelMes,
            RecaudadoAnioActual = summary.RecaudadoAnioActual,
            IngresadoEsteMes = summary.IngresadoEsteMes,
            IngresadoMesAnterior = summary.IngresadoMesAnterior,
            DeudaAcumulada = summary.DeudaAcumulada,
            JugadoresSinDeuda = summary.JugadoresSinDeuda,
            JugadoresConUnaImpaga = summary.JugadoresConUnaImpaga,
            JugadoresConDosOMasImpagas = summary.JugadoresConDosOMasImpagas,
            RecaudacionMensual = summary.RecaudacionMensual.Select(p => new MonthlyCollectionPointDTO
            {
                Mes = p.Mes,
                Anio = p.Anio,
                Monto = p.Monto
            }).ToList(),
            CoberturaPagoMensual = summary.CoberturaPagoMensual.Select(p => new MonthlyCoveragePointDTO
            {
                Mes = p.Mes,
                Anio = p.Anio,
                Porcentaje = p.Porcentaje
            }).ToList(),
            MayorDeudaPendiente = summary.MayorDeudaPendiente.Select(PendingDebtorResponseDTO.FromEntity).ToList()
        };
    }
}

public class MonthlyCollectionPointDTO
{
    public string Mes { get; set; } = "";
    public int Anio { get; set; }
    public decimal Monto { get; set; }
}

public class MonthlyCoveragePointDTO
{
    public string Mes { get; set; } = "";
    public int Anio { get; set; }
    public double Porcentaje { get; set; }
}
