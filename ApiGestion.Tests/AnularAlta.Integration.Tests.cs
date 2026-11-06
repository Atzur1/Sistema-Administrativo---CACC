namespace ApiGestion.Tests;

using DaoLibrary;
using EntityLibrary;
using Microsoft.Data.SqlClient;

// Anular un alta cargada por error, contra la base real y dentro de una transacción que se revierte: se borra todo si
// el jugador solo tiene cargos pendientes, y no se toca nada si ya tiene movimientos reales.
[Collection(BaseDeDatosCollection.Nombre)]
public class AnularAltaIntegrationTests
{
    private static readonly DateTime Hoy = RelojNegocio.Hoy;

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

    private static int Contar(SqlConnection c, SqlTransaction t, string sql, params (string, object)[] parametros)
    {
        using var cmd = new SqlCommand(sql, c, t);
        foreach (var (nombre, valor) in parametros) cmd.Parameters.AddWithValue(nombre, valor);
        return Convert.ToInt32(cmd.ExecuteScalar());
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
            FirstName = "Alta", LastName = "Equivocada", Dni = dni, BirthDate = new DateTime(2012, 1, 1),
            Gender = "Masculino", CategoryId = categoria, JoinDate = Hoy
        };
        return PlayerDAO.InsertPlayer(c, t, PlayerDAO.InsertPerson(c, t, jugador), jugador);
    }

    private static void CargarCuotaEInscripcion(SqlConnection c, SqlTransaction t, int id)
    {
        var mes = new DateTime(Hoy.Year, Hoy.Month, 1);
        PlayerDAO.InsertPendingCharge(c, t, id, 100_000m, mes, "Cuota");
        PlayerDAO.InsertPendingCharge(c, t, id, 70_000m, mes, "Inscripcion");
    }

    [IntegracionFact]
    public void SoloConCargosPendientes_SeBorraTodoYElDniQuedaLibre()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        try
        {
            int id = CrearJugador(c, t, "99999961");
            CargarCuotaEInscripcion(c, t, id);

            var resultado = JugadoresDao.AnularAltaCore(c, t, id);

            Assert.Equal(AnularAltaResultado.Ok, resultado.Resultado);
            Assert.Equal(2, resultado.CargosEliminados);
            Assert.True(resultado.PersonaEliminada);
            Assert.Equal(0, Contar(c, t, "SELECT COUNT(*) FROM JUGADORES WHERE PK_id_jugador = @id", ("@id", id)));
            Assert.Equal(0, Contar(c, t, "SELECT COUNT(*) FROM PAGOS WHERE FK_id_jugador = @id", ("@id", id)));
            Assert.Equal(0, Contar(c, t, "SELECT COUNT(*) FROM JUGADORES_PERIODOS WHERE FK_id_jugador = @id", ("@id", id)));
            Assert.Equal(0, Contar(c, t, "SELECT COUNT(*) FROM PERSONAS WHERE dni = '99999961'"));
            // Con el DNI libre se puede volver a dar de alta.
            Assert.True(CrearJugador(c, t, "99999961") > 0);
        }
        finally { t.Rollback(); }
    }

    [IntegracionFact]
    public void ConUnPagoCobrado_NoSeBorraNada()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        try
        {
            int id = CrearJugador(c, t, "99999962");
            CargarCuotaEInscripcion(c, t, id);
            Ejecutar(c, t,
                "INSERT INTO PAGOS (PK_id_pago, FK_id_jugador, monto_base, monto_final, fecha_pago, metodo_pago, fecha_vencimiento, estado, concepto, fecha_hora_registro) " +
                "VALUES (NEXT VALUE FOR dbo.SEQ_PAGOS, @id, 100000, 30000, @hoy, 'Efectivo', @venc, 1, 'Cuota', SYSDATETIME())",
                ("@id", id), ("@hoy", Hoy), ("@venc", new DateTime(Hoy.Year, Hoy.Month, 1)));
            int pagosAntes = Contar(c, t, "SELECT COUNT(*) FROM PAGOS WHERE FK_id_jugador = @id", ("@id", id));

            var resultado = JugadoresDao.AnularAltaCore(c, t, id);

            Assert.Equal(AnularAltaResultado.TieneMovimientos, resultado.Resultado);
            Assert.Contains("un pago cobrado", resultado.Motivos!);
            Assert.Equal(1, Contar(c, t, "SELECT COUNT(*) FROM JUGADORES WHERE PK_id_jugador = @id", ("@id", id)));
            Assert.Equal(pagosAntes, Contar(c, t, "SELECT COUNT(*) FROM PAGOS WHERE FK_id_jugador = @id", ("@id", id)));
            Assert.Equal(1, Contar(c, t, "SELECT COUNT(*) FROM PERSONAS WHERE dni = '99999962'"));
        }
        finally { t.Rollback(); }
    }

    [IntegracionFact]
    public void ConLaFichaMedicaCargada_NoSeBorraNada()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        try
        {
            int id = CrearJugador(c, t, "99999963");
            CargarCuotaEInscripcion(c, t, id);
            Ejecutar(c, t, "INSERT INTO FICHAS_MEDICAS (FK_id_jugador, grupo_sanguineo, factor) VALUES (@id, 'A', '+')", ("@id", id));

            var resultado = JugadoresDao.AnularAltaCore(c, t, id);

            Assert.Equal(AnularAltaResultado.TieneMovimientos, resultado.Resultado);
            Assert.Contains("datos cargados en la ficha médica", resultado.Motivos!);
            Assert.Equal(1, Contar(c, t, "SELECT COUNT(*) FROM JUGADORES WHERE PK_id_jugador = @id", ("@id", id)));
            Assert.Equal(2, Contar(c, t, "SELECT COUNT(*) FROM PAGOS WHERE FK_id_jugador = @id", ("@id", id)));
        }
        finally { t.Rollback(); }
    }

    [IntegracionFact]
    public void UnaFichaMedicaVacia_NoImpideAnular()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        try
        {
            int id = CrearJugador(c, t, "99999964");
            Ejecutar(c, t, "INSERT INTO FICHAS_MEDICAS (FK_id_jugador) VALUES (@id)", ("@id", id));

            var resultado = JugadoresDao.AnularAltaCore(c, t, id);

            Assert.Equal(AnularAltaResultado.Ok, resultado.Resultado);
            Assert.Equal(0, Contar(c, t, "SELECT COUNT(*) FROM FICHAS_MEDICAS WHERE FK_id_jugador = @id", ("@id", id)));
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
            Assert.Equal(AnularAltaResultado.NoEncontrado, JugadoresDao.AnularAltaCore(c, t, 2_000_000_000).Resultado);
        }
        finally { t.Rollback(); }
    }
}
