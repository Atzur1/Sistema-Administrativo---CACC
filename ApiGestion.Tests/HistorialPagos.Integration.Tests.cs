namespace ApiGestion.Tests;

using DaoLibrary;
using EntityLibrary;
using Microsoft.Data.SqlClient;

// El historial de pagos de un jugador contra la base real, dentro de una transaccion que se revierte: el total es el
// real aunque la pagina pedida no exista (SCRUM-131).
[Collection(BaseDeDatosCollection.Nombre)]
public class HistorialPagosIntegrationTests
{
    private static readonly DateTime Hoy = RelojNegocio.Hoy;

    private static SqlConnection Abrir()
    {
        var conexion = new SqlConnection(Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable));
        conexion.Open();
        return conexion;
    }

    private static int CrearJugadorConUnPagoCobrado(SqlConnection c, SqlTransaction t, string dni)
    {
        long categoria;
        using (var cmd = new SqlCommand("SELECT TOP (1) PK_id_categoria FROM CATEGORIAS ORDER BY PK_id_categoria", c, t))
        {
            categoria = Convert.ToInt64(cmd.ExecuteScalar());
        }
        var jugador = new Player
        {
            FirstName = "Prueba", LastName = "Historial", Dni = dni, BirthDate = new DateTime(2010, 1, 1),
            Gender = "Masculino", CategoryId = categoria, JoinDate = Hoy
        };
        int id = PlayerDAO.InsertPlayer(c, t, PlayerDAO.InsertPerson(c, t, jugador), jugador);
        using var pago = new SqlCommand(
            "INSERT INTO PAGOS (PK_id_pago, FK_id_jugador, monto_base, monto_final, fecha_pago, metodo_pago, fecha_vencimiento, estado, concepto, fecha_hora_registro) " +
            "VALUES (NEXT VALUE FOR dbo.SEQ_PAGOS, @id, 85000, 85000, @hoy, 'Efectivo', @venc, 1, 'Cuota', SYSDATETIME())", c, t);
        pago.Parameters.AddWithValue("@id", id);
        pago.Parameters.AddWithValue("@hoy", Hoy);
        pago.Parameters.AddWithValue("@venc", new DateTime(Hoy.Year, Hoy.Month, 1));
        pago.ExecuteNonQuery();
        return id;
    }

    [IntegracionFact]
    public void UnaPaginaFueraDeRango_InformaElTotalRealYNoCero()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        try
        {
            int id = CrearJugadorConUnPagoCobrado(c, t, "99999951");

            var fueraDeRango = PagosDao.ObtenerHistorialPagosCore(c, t, id, page: 5, pageSize: 10);

            Assert.Empty(fueraDeRango.Items);
            Assert.Equal(1, fueraDeRango.Total);
            Assert.Equal(5, fueraDeRango.Page);
        }
        finally { t.Rollback(); }
    }

    [IntegracionFact]
    public void LaPrimeraPagina_SigueInformandoElMismoTotal()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        try
        {
            int id = CrearJugadorConUnPagoCobrado(c, t, "99999952");

            var primera = PagosDao.ObtenerHistorialPagosCore(c, t, id, page: 1, pageSize: 10);

            Assert.Single(primera.Items);
            Assert.Equal(1, primera.Total);
        }
        finally { t.Rollback(); }
    }

    [IntegracionFact]
    public void UnJugadorSinPagos_TieneTotalCeroEnCualquierPagina()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        try
        {
            int id = CrearJugadorConUnPagoCobrado(c, t, "99999953");
            using (var borrar = new SqlCommand("DELETE FROM PAGOS WHERE FK_id_jugador = @id", c, t))
            {
                borrar.Parameters.AddWithValue("@id", id);
                borrar.ExecuteNonQuery();
            }

            Assert.Equal(0, PagosDao.ObtenerHistorialPagosCore(c, t, id, page: 1, pageSize: 10).Total);
            Assert.Equal(0, PagosDao.ObtenerHistorialPagosCore(c, t, id, page: 3, pageSize: 10).Total);
        }
        finally { t.Rollback(); }
    }
}
