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
    // HU-099: variación de cada tarjeta contra el mes anterior.
    public SummaryTrendsDTO Trends { get; set; } = new();

    public static GeneralSummaryResponseDTO FromEntity(ResumenGeneralInfo summary)
    {
        return new GeneralSummaryResponseDTO
        {
            Trends = new SummaryTrendsDTO
            {
                ActivePlayers = KpiTrendDTO.From(MonthlyTrend.Between(summary.TotalJugadores, summary.JugadoresActivosMesAnterior)),
                MonthRevenue = KpiTrendDTO.From(MonthlyTrend.Between(summary.IngresadoEsteMes, summary.IngresadoMesAnterior)),
                // Deuda acumulada y Habilitados no llevan variación: la base no guarda cómo estaban al cierre del mes
                // anterior (los saldos se descuentan con cada abono y los beneficios se aplican al leer), y una
                // reconstrucción sería una aproximación. Quedan neutrales.
                AccumulatedDebt = KpiTrendDTO.From(MonthlyTrend.None),
                EnabledPlayers = KpiTrendDTO.From(MonthlyTrend.None),
            },
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

public class SummaryTrendsDTO
{
    public KpiTrendDTO ActivePlayers { get; set; } = new();
    public KpiTrendDTO AccumulatedDebt { get; set; } = new();
    public KpiTrendDTO MonthRevenue { get; set; } = new();
    public KpiTrendDTO EnabledPlayers { get; set; } = new();
}

// changePercentage: entero, null sin mes anterior con qué comparar. direction: "up" | "down" | "flat" | "none".
public class KpiTrendDTO
{
    public decimal? ChangePercentage { get; set; }
    public string Direction { get; set; } = "none";

    public static KpiTrendDTO From(MonthlyTrend trend) => new()
    {
        ChangePercentage = trend.ChangePercentage,
        Direction = trend.Direction switch
        {
            TrendDirection.Up => "up",
            TrendDirection.Down => "down",
            TrendDirection.Flat => "flat",
            _ => "none"
        }
    };
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
