namespace ApiGestion.Tests;

using DaoLibrary;
using EntityLibrary;
using Microsoft.Data.SqlClient;

// La baja que informa el Portal Deportivo (SCRUM-757), contra la base real y dentro de una transaccion que se revierte:
// quita solo las cuotas futuras sin pagos, mantiene la deuda anterior y es idempotente.
[Collection(BaseDeDatosCollection.Nombre)]
public class BajaInformadaIntegrationTests
{
    private static readonly DateTime Hoy = RelojNegocio.Hoy;
    private static DateTime Mes(int desdeHoy) => new DateTime(Hoy.Year, Hoy.Month, 1).AddMonths(desdeHoy);

    private static SqlConnection Abrir()
    {
        var conexion = new SqlConnection(Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable));
        conexion.Open();
        return conexion;
    }

    private static void Ejecutar(SqlConnection c, SqlTransaction t, string sql, params (string, object)[] parametros)
    {
        using var cmd = new SqlCommand(sql, c, t);
        foreach (var (nombre, valor) in parametros) cmd.Parameters.AddWithValue(nombre, valor);
        cmd.ExecuteNonQuery();
    }

    private static int CrearJugadorConCuotasPendientes(SqlConnection c, SqlTransaction t, string dni, params int[] mesesDesdeHoy)
    {
        long categoria;
        using (var cmd = new SqlCommand("SELECT TOP (1) PK_id_categoria FROM CATEGORIAS ORDER BY PK_id_categoria", c, t))
        {
            categoria = Convert.ToInt64(cmd.ExecuteScalar());
        }
        var jugador = new Player
        {
            FirstName = "Prueba", LastName = "Baja", Dni = dni, BirthDate = new DateTime(2010, 1, 1),
            Gender = "Masculino", CategoryId = categoria, JoinDate = Hoy.AddYears(-1)
        };
        int id = PlayerDAO.InsertPlayer(c, t, PlayerDAO.InsertPerson(c, t, jugador), jugador);
        foreach (int mes in mesesDesdeHoy) PlayerDAO.InsertPendingCharge(c, t, id, 85_000m, Mes(mes), "Cuota");
        return id;
    }

    private static List<DateTime> MesesPendientes(SqlConnection c, SqlTransaction t, int id)
    {
        var meses = new List<DateTime>();
        using var cmd = new SqlCommand("SELECT fecha_vencimiento FROM PAGOS WHERE FK_id_jugador = @id AND estado = 0 ORDER BY fecha_vencimiento", c, t);
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) meses.Add(reader.GetDateTime(0));
        return meses;
    }

    [IntegracionFact]
    public void SiTodaviaNoFiguraDadoDeBaja_LaRegistraYQuitaSoloLasCuotasFuturas_ManteniendoLaDeudaAnterior()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        try
        {
            int id = CrearJugadorConCuotasPendientes(c, t, "99999941", -2, 0, 1, 2);

            var resultado = JugadoresDao.AplicarBajaInformadaCore(c, t, id, Hoy);

            Assert.Equal(BajaJugadorResultado.Ok, resultado.Resultado);
            Assert.Equal(2, resultado.CuotasEliminadas);
            Assert.Equal(new[] { Mes(-2), Mes(0) }, MesesPendientes(c, t, id));   // la deuda anterior y la del mes se mantienen
            using var baja = new SqlCommand("SELECT fecha_baja FROM JUGADORES WHERE PK_id_jugador = @id", c, t);
            baja.Parameters.AddWithValue("@id", id);
            Assert.Equal(Hoy.Date, Convert.ToDateTime(baja.ExecuteScalar()).Date);
        }
        finally { t.Rollback(); }
    }

    [IntegracionFact]
    public void SiElDeportivoYaGraboLaBaja_SoloAplicaSusEfectos_YRepetirNoHaceNada()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        try
        {
            int id = CrearJugadorConCuotasPendientes(c, t, "99999942", -1, 0, 1, 2);
            // Lo que hace el Deportivo: graba la baja directo, sin tocar nada mas.
            Ejecutar(c, t, "UPDATE JUGADORES SET fecha_baja = @hoy WHERE PK_id_jugador = @id", ("@hoy", Hoy.Date), ("@id", id));
            Assert.Equal(4, MesesPendientes(c, t, id).Count);

            var primera = JugadoresDao.AplicarBajaInformadaCore(c, t, id, Hoy);
            var segunda = JugadoresDao.AplicarBajaInformadaCore(c, t, id, Hoy);

            Assert.Equal(2, primera.CuotasEliminadas);
            Assert.Equal(0, segunda.CuotasEliminadas);
            Assert.Equal(new[] { Mes(-1), Mes(0) }, MesesPendientes(c, t, id));
        }
        finally { t.Rollback(); }
    }

    [IntegracionFact]
    public void UnaCuotaFuturaConUnPagoNoSeQuita()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        try
        {
            int id = CrearJugadorConCuotasPendientes(c, t, "99999943", 1, 2);
            // Pago por adelantado (parcial) del mes siguiente: esa cuota ya tiene plata adentro.
            Ejecutar(c, t,
                "INSERT INTO PAGOS (PK_id_pago, FK_id_jugador, monto_base, monto_final, fecha_pago, metodo_pago, fecha_vencimiento, estado, concepto, fecha_hora_registro) " +
                "VALUES (NEXT VALUE FOR dbo.SEQ_PAGOS, @id, 85000, 30000, @hoy, 'Efectivo', @venc, 1, 'Cuota', SYSDATETIME())",
                ("@id", id), ("@hoy", Hoy.Date), ("@venc", Mes(1)));

            var resultado = JugadoresDao.AplicarBajaInformadaCore(c, t, id, Hoy);

            Assert.Equal(1, resultado.CuotasEliminadas);
            Assert.Equal(new[] { Mes(1) }, MesesPendientes(c, t, id));
        }
        finally { t.Rollback(); }
    }

    [IntegracionFact]
    public void UnJugadorQueNoExiste_NoSeEncuentra()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        try
        {
            Assert.Equal(BajaJugadorResultado.NoEncontrado, JugadoresDao.AplicarBajaInformadaCore(c, t, 2_000_000_000, Hoy).Resultado);
        }
        finally { t.Rollback(); }
    }
}
