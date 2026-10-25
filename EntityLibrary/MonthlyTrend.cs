namespace EntityLibrary;

public enum TrendDirection
{
    None, // sin mes anterior con qué comparar (o la tarjeta no lleva variación)
    Up,
    Down,
    Flat
}

// HU-099: variación de un indicador del Resumen General contra el mes anterior, en porcentaje entero.
// Sin valor en el mes anterior (0) no hay base para un porcentaje: la tendencia queda neutral en vez de inventar un "+100 %".
public sealed record MonthlyTrend(decimal? ChangePercentage, TrendDirection Direction)
{
    public static MonthlyTrend None { get; } = new(null, TrendDirection.None);

    public static MonthlyTrend Between(decimal current, decimal previous)
    {
        if (previous <= 0)
        {
            return None;
        }
        decimal change = Math.Round((current - previous) * 100m / previous, 0, MidpointRounding.AwayFromZero);
        return new MonthlyTrend(change, change > 0 ? TrendDirection.Up : change < 0 ? TrendDirection.Down : TrendDirection.Flat);
    }
}
