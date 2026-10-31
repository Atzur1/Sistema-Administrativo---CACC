namespace ApiGestion.Tests;

using DaoLibrary;
using EntityLibrary;
using Microsoft.Data.SqlClient;

// El detalle de deuda de un jugador contra la base real, dentro de una transacción que se revierte: la inscripción y la cuota
// de un mismo mes se distinguen (la inscripción es anual: "Inscripción 2026", no el nombre del mes).
[Collection(BaseDeDatosCollection.Nombre)]
public class DeudaDetalleIntegrationTests
{
    private static readonly DateTime Hoy = RelojNegocio.Hoy;

    private static SqlConnection Abrir()
    {
        var conexion = new SqlConnection(Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable));
        conexion.Open();
        return conexion;
    }

    private static int CrearJugador(SqlConnection c, SqlTransaction t, string dni)
    {
        long categoria;
        using (var cmd = new SqlCommand("SELECT TOP (1) PK_id_categoria FROM CATEGORIAS ORDER BY PK_id_categoria", c, t))
        {
            categoria = Convert.ToInt64(cmd.ExecuteScalar());
        }
        var jugador = new Player
        {
            FirstName = "Prueba", LastName = "Deuda", Dni = dni, BirthDate = new DateTime(2010, 1, 1),
            Gender = "Masculino", CategoryId = categoria, JoinDate = Hoy
        };
        return PlayerDAO.InsertPlayer(c, t, PlayerDAO.InsertPerson(c, t, jugador), jugador);
    }

    [IntegracionFact]
    public void LaInscripcionYLaCuotaDelMismoMes_SeNombranDistinto()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        try
        {
            int id = CrearJugador(c, t, "99999971");
            var vencimiento = new DateTime(Hoy.Year, Hoy.Month, 1);
            PlayerDAO.InsertPendingCharge(c, t, id, 100_000m, vencimiento, "Cuota");
            PlayerDAO.InsertPendingCharge(c, t, id, 70_000m, vencimiento, "Inscripcion");

            var deuda = PagosDao.ObtenerDeudaDetalleCore(c, t, id);

            var cuota = Assert.Single(deuda, d => d.Concepto == "Cuota");
            var inscripcion = Assert.Single(deuda, d => d.Concepto == "Inscripcion");
            Assert.Equal(CalendarioCuotas.NombreDelPeriodo(vencimiento), cuota.Periodo);
            Assert.Equal($"Inscripción {vencimiento.Year}", inscripcion.Periodo);
            Assert.Equal(100_000m, cuota.MontoOriginal);
            Assert.Equal(70_000m, inscripcion.MontoOriginal);
        }
        finally { t.Rollback(); }
    }

    [IntegracionFact]
    public void ElAbonoDeLaInscripcion_SeAsociaALaInscripcionYNoALaCuotaDeEseMes()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        try
        {
            int id = CrearJugador(c, t, "99999972");
            var vencimiento = new DateTime(Hoy.Year, Hoy.Month, 1);
            PlayerDAO.InsertPendingCharge(c, t, id, 100_000m, vencimiento, "Cuota");
            PlayerDAO.InsertPendingCharge(c, t, id, 70_000m, vencimiento, "Inscripcion");
            using (var cmd = new SqlCommand(
                "INSERT INTO PAGOS (PK_id_pago, FK_id_jugador, monto_base, monto_final, fecha_pago, metodo_pago, fecha_vencimiento, estado, concepto, fecha_hora_registro) " +
                "VALUES (NEXT VALUE FOR dbo.SEQ_PAGOS, @id, 70000, 20000, @hoy, 'Efectivo', @venc, 1, 'Inscripcion', SYSDATETIME())", c, t))
            {
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@hoy", Hoy);
                cmd.Parameters.AddWithValue("@venc", vencimiento);
                cmd.ExecuteNonQuery();
            }

            var deuda = PagosDao.ObtenerDeudaDetalleCore(c, t, id);

            Assert.Single(Assert.Single(deuda, d => d.Concepto == "Inscripcion").Abonos);
            Assert.Empty(Assert.Single(deuda, d => d.Concepto == "Cuota").Abonos);
        }
        finally { t.Rollback(); }
    }
}
