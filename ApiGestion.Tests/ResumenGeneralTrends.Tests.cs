namespace ApiGestion.Tests;

using ApiGestion.Models;
using DaoLibrary;
using EntityLibrary;
using Microsoft.Data.SqlClient;

// HU-099: variación mensual de las tarjetas del Resumen General (mes actual contra el anterior).
public class MonthlyTrendTests
{
    [Theory]
    [InlineData(112, 100, 12, TrendDirection.Up)]
    [InlineData(88, 100, -12, TrendDirection.Down)]
    [InlineData(100, 100, 0, TrendDirection.Flat)]
    [InlineData(0, 100, -100, TrendDirection.Down)]        // un mes sin cobros después de uno con cobros
    [InlineData(250_000, 100_000, 150, TrendDirection.Up)]
    public void TheChangeIsAWholePercentageAgainstThePreviousMonth(decimal current, decimal previous, decimal change, TrendDirection direction)
    {
        var trend = MonthlyTrend.Between(current, previous);

        Assert.Equal(change, trend.ChangePercentage);
        Assert.Equal(direction, trend.Direction);
    }

    [Fact]
    public void ItRoundsToTheNearestWholePercentage()
    {
        Assert.Equal(13, MonthlyTrend.Between(1125, 1000).ChangePercentage);   // 12,5 % -> 13 %
        Assert.Equal(-13, MonthlyTrend.Between(875, 1000).ChangePercentage);   // -12,5 % -> -13 %
        Assert.Equal(33, MonthlyTrend.Between(4, 3).ChangePercentage);         // 33,33 % -> 33 %
    }

    [Theory]
    [InlineData(500_000, 0)]   // el mes anterior no tuvo cobros: no hay base para un porcentaje
    [InlineData(0, 0)]         // dos meses sin cobros
    public void WithoutAPreviousValue_TheTrendIsNeutral(decimal current, decimal previous)
    {
        var trend = MonthlyTrend.Between(current, previous);

        Assert.Null(trend.ChangePercentage);
        Assert.Equal(TrendDirection.None, trend.Direction);
    }
}

public class GeneralSummaryTrendsDtoTests
{
    private static ResumenGeneralInfo Summary(int players = 120, int playersBefore = 100, decimal revenue = 95_625m, decimal revenueBefore = 85_000m) => new()
    {
        TotalJugadores = players,
        JugadoresActivosMesAnterior = playersBefore,
        IngresadoEsteMes = revenue,
        IngresadoMesAnterior = revenueBefore,
        DeudaAcumulada = 1_000_000m,
        JugadoresSinDeuda = 90,
    };

    [Fact]
    public void TwoMonthsWithData_ActivePlayersAndRevenueCarryTheirChange()
    {
        var trends = GeneralSummaryResponseDTO.FromEntity(Summary()).Trends;

        Assert.Equal(20, trends.ActivePlayers.ChangePercentage);
        Assert.Equal("up", trends.ActivePlayers.Direction);
        Assert.Equal(13, trends.MonthRevenue.ChangePercentage);   // 95.625 / 85.000 = +12,5 %
        Assert.Equal("up", trends.MonthRevenue.Direction);
    }

    [Fact]
    public void ACurrentMonthWithoutPayments_IsADrop()
    {
        var trends = GeneralSummaryResponseDTO.FromEntity(Summary(revenue: 0m)).Trends;

        Assert.Equal(-100, trends.MonthRevenue.ChangePercentage);
        Assert.Equal("down", trends.MonthRevenue.Direction);
    }

    [Fact]
    public void APreviousMonthWithoutPayments_IsNeutral()
    {
        var trends = GeneralSummaryResponseDTO.FromEntity(Summary(revenueBefore: 0m)).Trends;

        Assert.Null(trends.MonthRevenue.ChangePercentage);
        Assert.Equal("none", trends.MonthRevenue.Direction);
    }

    // Decisión de la HU: la base no guarda la deuda ni los habilitados al cierre del mes anterior.
    [Fact]
    public void DebtAndEnabledPlayersAreAlwaysNeutral()
    {
        var trends = GeneralSummaryResponseDTO.FromEntity(Summary()).Trends;

        Assert.Equal("none", trends.AccumulatedDebt.Direction);
        Assert.Null(trends.AccumulatedDebt.ChangePercentage);
        Assert.Equal("none", trends.EnabledPlayers.Direction);
        Assert.Null(trends.EnabledPlayers.ChangePercentage);
    }

    [Fact]
    public void TheSerializedTrendsUseTheAgreedNames()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(GeneralSummaryResponseDTO.FromEntity(Summary()),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        Assert.Contains("\"trends\":{\"activePlayers\":{\"changePercentage\":20,\"direction\":\"up\"}", json);
        Assert.Contains("\"accumulatedDebt\":{\"changePercentage\":null,\"direction\":\"none\"}", json);
    }
}

// El conteo de activos al cierre del mes anterior, contra la base real dentro de una transacción que se revierte.
[Collection(BaseDeDatosCollection.Nombre)]
public class ActivePlayersPreviousMonthIntegrationTests
{
    private static string Conexion => Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable)!;

    private static int CrearJugador(SqlConnection c, SqlTransaction t, DateTime alta)
    {
        int categoria;
        using (var cmd = new SqlCommand("SELECT TOP (1) PK_id_categoria FROM CATEGORIAS ORDER BY PK_id_categoria", c, t))
            categoria = Convert.ToInt32(cmd.ExecuteScalar());
        var player = new Player
        {
            FirstName = "Prueba", LastName = "Tendencia", Dni = "96" + Random.Shared.Next(100000, 999999),
            BirthDate = new DateTime(2010, 1, 1), Gender = "Masculino", CategoryId = categoria, JoinDate = alta
        };
        return PlayerDAO.InsertPlayer(c, t, PlayerDAO.InsertPerson(c, t, player), player);
    }

    private static void DarDeBaja(SqlConnection c, SqlTransaction t, int jugador, DateTime baja)
    {
        using var cmd = new SqlCommand("UPDATE JUGADORES SET fecha_baja = @b WHERE PK_id_jugador = @j", c, t);
        cmd.Parameters.AddWithValue("@b", baja);
        cmd.Parameters.AddWithValue("@j", jugador);
        cmd.ExecuteNonQuery();
    }

    [IntegracionFact]
    public void CountsWhoWasActiveOnTheLastDayOfThePreviousMonth()
    {
        using var connection = new SqlConnection(Conexion);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            DateTime hoy = RelojNegocio.Hoy;
            DateTime inicioDelMes = new DateTime(hoy.Year, hoy.Month, 1);
            int antes = EstadisticasDao.ContarActivosAlCierreDelMesAnterior(connection, transaction);

            CrearJugador(connection, transaction, new DateTime(2020, 1, 1));                            // activo desde antes: cuenta
            CrearJugador(connection, transaction, inicioDelMes);                                         // entró este mes: no cuenta
            DarDeBaja(connection, transaction, CrearJugador(connection, transaction, new DateTime(2020, 1, 1)),
                inicioDelMes.AddDays(-1));                                                               // baja el último día del mes pasado: no cuenta
            DarDeBaja(connection, transaction, CrearJugador(connection, transaction, new DateTime(2020, 1, 1)),
                inicioDelMes);                                                                           // baja este mes: el mes pasado estaba activo, cuenta

            Assert.Equal(antes + 2, EstadisticasDao.ContarActivosAlCierreDelMesAnterior(connection, transaction));
        }
        finally { transaction.Rollback(); }
    }
}
