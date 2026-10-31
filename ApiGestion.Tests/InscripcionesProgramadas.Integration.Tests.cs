namespace ApiGestion.Tests;

using DaoLibrary;
using EntityLibrary;
using Microsoft.Data.SqlClient;

// Una inscripción que empieza a regir DESPUÉS de que el jugador entró se le cobra el día en que rige, siempre que haya entrado
// con la inscripción ya cargada (programada). Contra la base real, dentro de una transacción que se revierte.
[Collection(BaseDeDatosCollection.Nombre)]
public class InscripcionesProgramadasIntegrationTests
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

    // La inscripción femenina que ya rige hoy, cargada hace `diasAtras` días (la auditoría dice cuándo se cargó).
    private static void InscripcionFemeninaCargadaHace(SqlConnection c, SqlTransaction t, decimal monto, int diasAtras)
    {
        Ejecutar(c, t, "DELETE FROM ARANCELES_INSCRIPCION WHERE genero = 'Femenino' AND vigente_desde = @d", ("@d", Hoy));
        Ejecutar(c, t, "INSERT INTO ARANCELES_INSCRIPCION (genero, monto, vigente_desde) VALUES ('Femenino', @m, @d)", ("@m", monto), ("@d", Hoy));
        Ejecutar(c, t, @"UPDATE ac SET fecha_utc = DATEADD(DAY, -@dias, fecha_utc)
                         FROM AUDITORIA_CAMBIOS ac
                         WHERE ac.entidad = N'ARANCELES_INSCRIPCION' AND ac.accion = 'INSERT'
                           AND ac.id_entidad = CONVERT(NVARCHAR(128), (SELECT PK_id_arancel_inscripcion FROM ARANCELES_INSCRIPCION WHERE genero = 'Femenino' AND vigente_desde = @d))",
            ("@dias", diasAtras), ("@d", Hoy));
    }

    private static int CrearJugadora(SqlConnection c, SqlTransaction t, string dni, DateTime alta, DateTime? baja = null)
    {
        long categoria;
        using (var cmd = new SqlCommand("SELECT TOP (1) PK_id_categoria FROM CATEGORIAS ORDER BY PK_id_categoria", c, t))
        {
            categoria = Convert.ToInt64(cmd.ExecuteScalar());
        }
        var jugadora = new Player
        {
            FirstName = "Prueba", LastName = "Inscripcion", Dni = dni, BirthDate = new DateTime(2010, 1, 1),
            Gender = "Femenino", CategoryId = categoria, JoinDate = alta
        };
        int id = PlayerDAO.InsertPlayer(c, t, PlayerDAO.InsertPerson(c, t, jugadora), jugadora);
        if (baja != null) Ejecutar(c, t, "UPDATE JUGADORES SET fecha_baja = @b WHERE PK_id_jugador = @id", ("@b", baja.Value), ("@id", id));
        return id;
    }

    private static List<(decimal Monto, DateTime Vencimiento, int Estado)> Inscripciones(SqlConnection c, SqlTransaction t, int idJugador)
    {
        var lista = new List<(decimal, DateTime, int)>();
        using var cmd = new SqlCommand("SELECT monto_final, fecha_vencimiento, estado FROM PAGOS WHERE FK_id_jugador = @id AND concepto = 'Inscripcion'", c, t);
        cmd.Parameters.AddWithValue("@id", idJugador);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) lista.Add((reader.GetDecimal(0), reader.GetDateTime(1), Convert.ToInt32(reader[2])));
        return lista;
    }

    [IntegracionFact]
    public void QuienEntroConLaInscripcionYaProgramada_LaPagaElDiaQueRige()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        try
        {
            InscripcionFemeninaCargadaHace(c, t, 30_000m, diasAtras: 10);
            int entroDespues = CrearJugadora(c, t, "99999991", Hoy.AddDays(-5));

            PlayerDAO.GenerarInscripcionesProgramadas(c, t);

            var inscripciones = Inscripciones(c, t, entroDespues);
            Assert.Single(inscripciones);
            Assert.Equal(30_000m, inscripciones[0].Monto);
            Assert.Equal(Hoy, inscripciones[0].Vencimiento);
            Assert.Equal(0, inscripciones[0].Estado); // pendiente
        }
        finally { t.Rollback(); }
    }

    [IntegracionFact]
    public void QuienEstabaAntesDeQueSeProgramara_NoRecibeNingunCobro()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        try
        {
            InscripcionFemeninaCargadaHace(c, t, 30_000m, diasAtras: 10);
            int yaEstaba = CrearJugadora(c, t, "99999992", Hoy.AddDays(-20));

            PlayerDAO.GenerarInscripcionesProgramadas(c, t);

            Assert.Empty(Inscripciones(c, t, yaEstaba));
        }
        finally { t.Rollback(); }
    }

    [IntegracionFact]
    public void NoSeDuplicaNiSeLeCobraAQuienYaTieneInscripcionOEstaDeBaja()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        try
        {
            InscripcionFemeninaCargadaHace(c, t, 30_000m, diasAtras: 10);
            int conInscripcion = CrearJugadora(c, t, "99999993", Hoy.AddDays(-5));
            PlayerDAO.InsertPendingCharge(c, t, conInscripcion, 25_000m, Hoy.AddDays(-5), "Inscripcion");
            int deBaja = CrearJugadora(c, t, "99999994", Hoy.AddDays(-5), baja: Hoy.AddDays(-1));
            int nueva = CrearJugadora(c, t, "99999995", Hoy.AddDays(-3));

            int primera = PlayerDAO.GenerarInscripcionesProgramadas(c, t);
            int segunda = PlayerDAO.GenerarInscripcionesProgramadas(c, t);

            Assert.Single(Inscripciones(c, t, conInscripcion));          // sigue la que tenía, a su monto
            Assert.Equal(25_000m, Inscripciones(c, t, conInscripcion)[0].Monto);
            Assert.Empty(Inscripciones(c, t, deBaja));
            Assert.Single(Inscripciones(c, t, nueva));
            Assert.True(primera >= 1);
            Assert.Equal(0, segunda);                                    // idempotente
        }
        finally { t.Rollback(); }
    }

    // SCRUM-565: la API no corrió el día en que regía la primera inscripción y al día siguiente ya regía otra.
    [IntegracionFact]
    public void SiLaApiEstuvoCaida_ElJugadorPagaLaInscripcionEnLaQueEntro_NoLaSiguiente()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        try
        {
            // $80.000 rige desde ayer (cargada hace 7 días) y $90.000 rige desde hoy (cargada ayer): la vigente hoy es la segunda.
            Ejecutar(c, t, "DELETE FROM ARANCELES_INSCRIPCION WHERE genero = 'Femenino'");
            Ejecutar(c, t, "INSERT INTO ARANCELES_INSCRIPCION (genero, monto, vigente_desde) VALUES ('Femenino', 80000, @d)", ("@d", Hoy.AddDays(-1)));
            Ejecutar(c, t, "INSERT INTO ARANCELES_INSCRIPCION (genero, monto, vigente_desde) VALUES ('Femenino', 90000, @d)", ("@d", Hoy));
            Ejecutar(c, t, @"UPDATE ac SET fecha_utc = DATEADD(DAY, -7, fecha_utc) FROM AUDITORIA_CAMBIOS ac
                             WHERE ac.entidad = N'ARANCELES_INSCRIPCION' AND ac.accion = 'INSERT'
                               AND ac.id_entidad = CONVERT(NVARCHAR(128), (SELECT PK_id_arancel_inscripcion FROM ARANCELES_INSCRIPCION WHERE genero = 'Femenino' AND monto = 80000))");
            int entroEnLaPrimera = CrearJugadora(c, t, "99999975", Hoy.AddDays(-4));

            PlayerDAO.GenerarInscripcionesProgramadas(c, t);

            Assert.Equal([(80_000m, Hoy.AddDays(-1), 0)], Inscripciones(c, t, entroEnLaPrimera));
        }
        finally { t.Rollback(); }
    }

    [IntegracionFact]
    public void ConLaInscripcionEnCero_NoSeCobraNada()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        try
        {
            // Una inscripción de $0 (el género no la paga) no genera deuda aunque hayan entrado jugadores después de cargarla.
            Ejecutar(c, t, "DELETE FROM ARANCELES_INSCRIPCION WHERE genero = 'Femenino' AND vigente_desde = @d", ("@d", Hoy));
            Ejecutar(c, t, "INSERT INTO ARANCELES_INSCRIPCION (genero, monto, vigente_desde) VALUES ('Femenino', 0, @d)", ("@d", Hoy));
            Ejecutar(c, t, @"UPDATE ac SET fecha_utc = DATEADD(DAY, -10, fecha_utc) FROM AUDITORIA_CAMBIOS ac
                             WHERE ac.entidad = N'ARANCELES_INSCRIPCION' AND ac.accion = 'INSERT'
                               AND ac.id_entidad = CONVERT(NVARCHAR(128), (SELECT PK_id_arancel_inscripcion FROM ARANCELES_INSCRIPCION WHERE genero = 'Femenino' AND vigente_desde = @d))",
                ("@d", Hoy));
            int jugadora = CrearJugadora(c, t, "99999996", Hoy.AddDays(-5));

            PlayerDAO.GenerarInscripcionesProgramadas(c, t);

            Assert.Empty(Inscripciones(c, t, jugadora));
        }
        finally { t.Rollback(); }
    }
}
