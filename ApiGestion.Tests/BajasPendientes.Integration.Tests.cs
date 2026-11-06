namespace ApiGestion.Tests;

using DaoLibrary;
using EntityLibrary;
using Microsoft.Data.SqlClient;

// Red de seguridad de las bajas del Portal Deportivo (SCRUM-757), contra la base real y dentro de una transaccion que se
// revierte: completa las bajas que quedaron a medias y no toca a quien sigue activo.
[Collection(BaseDeDatosCollection.Nombre)]
public class BajasPendientesIntegrationTests
{
    private static readonly DateTime Hoy = RelojNegocio.Hoy;
    private static DateTime Mes(int desdeHoy) => new DateTime(Hoy.Year, Hoy.Month, 1).AddMonths(desdeHoy);

    private static SqlConnection Abrir()
    {
        var conexion = new SqlConnection(Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable));
        conexion.Open();
        return conexion;
    }

    private static int Crear(SqlConnection c, SqlTransaction t, string dni, bool dadoDeBaja, params int[] mesesPendientes)
    {
        long categoria;
        using (var cmd = new SqlCommand("SELECT TOP (1) PK_id_categoria FROM CATEGORIAS ORDER BY PK_id_categoria", c, t))
        {
            categoria = Convert.ToInt64(cmd.ExecuteScalar());
        }
        var jugador = new Player
        {
            FirstName = "Prueba", LastName = "Pendiente", Dni = dni, BirthDate = new DateTime(2010, 1, 1),
            Gender = "Masculino", CategoryId = categoria, JoinDate = Hoy.AddYears(-1)
        };
        int id = PlayerDAO.InsertPlayer(c, t, PlayerDAO.InsertPerson(c, t, jugador), jugador);
        foreach (int mes in mesesPendientes) PlayerDAO.InsertPendingCharge(c, t, id, 85_000m, Mes(mes), "Cuota");
        if (dadoDeBaja)
        {
            using var baja = new SqlCommand("UPDATE JUGADORES SET fecha_baja = @hoy WHERE PK_id_jugador = @id", c, t);
            baja.Parameters.AddWithValue("@hoy", Hoy.Date);
            baja.Parameters.AddWithValue("@id", id);
            baja.ExecuteNonQuery();
        }
        return id;
    }

    private static int Pendientes(SqlConnection c, SqlTransaction t, int id)
    {
        using var cmd = new SqlCommand("SELECT COUNT(*) FROM PAGOS WHERE FK_id_jugador = @id AND estado = 0", c, t);
        cmd.Parameters.AddWithValue("@id", id);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    [IntegracionFact]
    public void CompletaLasBajasQueQuedaronAMedias_YNoTocaAQuienSigueActivo()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        try
        {
            int dadoDeBaja = Crear(c, t, "99999931", dadoDeBaja: true, 0, 1, 2);
            int activo = Crear(c, t, "99999932", dadoDeBaja: false, 0, 1, 2);
            int sinNada = Crear(c, t, "99999933", dadoDeBaja: true, -1, 0);

            int completadas = JugadoresDao.CompletarBajasPendientesCore(c, t);

            Assert.True(completadas >= 1);
            Assert.Equal(1, Pendientes(c, t, dadoDeBaja));   // queda solo la del mes en curso
            Assert.Equal(3, Pendientes(c, t, activo));       // quien sigue activo no se toca
            Assert.Equal(2, Pendientes(c, t, sinNada));      // nada futuro: nada que quitar
        }
        finally { t.Rollback(); }
    }

    [IntegracionFact]
    public void RepetirlaNoHaceNada()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        try
        {
            int id = Crear(c, t, "99999934", dadoDeBaja: true, 0, 1);

            JugadoresDao.CompletarBajasPendientesCore(c, t);
            int pendientesTrasLaPrimera = Pendientes(c, t, id);
            JugadoresDao.CompletarBajasPendientesCore(c, t);

            Assert.Equal(1, pendientesTrasLaPrimera);
            Assert.Equal(pendientesTrasLaPrimera, Pendientes(c, t, id));
        }
        finally { t.Rollback(); }
    }
}
