namespace ApiGestion.Tests;

using DaoLibrary;
using EntityLibrary;
using Microsoft.Data.SqlClient;

// La inscripción anual contra la base real, dentro de una transacción que se revierte: la emisión de la siguiente al cumplirse
// el año, quién la recibe y quién no, el reingreso de un jugador dado de baja y la convivencia de dos años en PAGOS.
// Requiere la migración V20261021_01 (índice único por jugador y año) aplicada en la base de prueba.
[Collection(BaseDeDatosCollection.Nombre)]
public class InscripcionAnualIntegrationTests
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

    // La inscripción de ese género, vigente desde hace mucho (así rige también en los aniversarios que se prueban).
    private static void ArancelDeInscripcion(SqlConnection c, SqlTransaction t, string genero, decimal monto, DateTime? desde = null)
    {
        Ejecutar(c, t, "DELETE FROM ARANCELES_INSCRIPCION WHERE genero = @g", ("@g", genero));
        Ejecutar(c, t, "INSERT INTO ARANCELES_INSCRIPCION (genero, monto, vigente_desde) VALUES (@g, @m, @d)",
            ("@g", genero), ("@m", monto), ("@d", desde ?? Hoy.AddDays(-2000)));
    }

    private static int CrearJugador(SqlConnection c, SqlTransaction t, string dni, DateTime alta, string genero = "Masculino", DateTime? baja = null)
    {
        long categoria;
        using (var cmd = new SqlCommand("SELECT TOP (1) PK_id_categoria FROM CATEGORIAS ORDER BY PK_id_categoria", c, t))
        {
            categoria = Convert.ToInt64(cmd.ExecuteScalar());
        }
        var jugador = new Player
        {
            FirstName = "Prueba", LastName = "Anual", Dni = dni, BirthDate = new DateTime(2010, 1, 1),
            Gender = genero, CategoryId = categoria, JoinDate = alta
        };
        int id = PlayerDAO.InsertPlayer(c, t, PlayerDAO.InsertPerson(c, t, jugador), jugador);
        if (baja != null) Ejecutar(c, t, "UPDATE JUGADORES SET fecha_baja = @b WHERE PK_id_jugador = @id", ("@b", baja.Value), ("@id", id));
        return id;
    }

    private static List<(decimal Monto, DateTime Vencimiento, int Estado)> Inscripciones(SqlConnection c, SqlTransaction t, int idJugador)
    {
        var lista = new List<(decimal, DateTime, int)>();
        using var cmd = new SqlCommand("SELECT monto_final, fecha_vencimiento, estado FROM PAGOS WHERE FK_id_jugador = @id AND concepto = 'Inscripcion' ORDER BY fecha_vencimiento", c, t);
        cmd.Parameters.AddWithValue("@id", idJugador);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) lista.Add((reader.GetDecimal(0), reader.GetDateTime(1), Convert.ToInt32(reader[2])));
        return lista;
    }

    [IntegracionFact]
    public void AlCumplirseElAnioSeEmiteLaSiguiente_SoloUnaVez()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        try
        {
            ArancelDeInscripcion(c, t, "Masculino", 70_000m);
            DateTime alta = Hoy.AddYears(-1).AddDays(-3);
            int id = CrearJugador(c, t, "99999981", alta);
            PlayerDAO.InsertPendingCharge(c, t, id, 60_000m, alta, "Inscripcion"); // la del año pasado

            int primera = PlayerDAO.GenerarInscripcionesAnuales(c, t, Hoy);
            int segunda = PlayerDAO.GenerarInscripcionesAnuales(c, t, Hoy);

            var filas = Inscripciones(c, t, id);
            Assert.True(primera >= 1);
            Assert.Equal(0, segunda); // idempotente
            Assert.Equal(2, filas.Count);
            Assert.Equal(60_000m, filas[0].Monto);                       // la del año anterior no se toca
            Assert.Equal((70_000m, Hoy.AddDays(-3), 0), filas[1]);        // la nueva: arancel vigente, vence el aniversario
        }
        finally { t.Rollback(); }
    }

    [IntegracionFact]
    public void QuienNuncaTuvoInscripcion_LaRecibeEnElAniversarioDeSuAlta()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        try
        {
            ArancelDeInscripcion(c, t, "Femenino", 30_000m);
            DateTime alta = Hoy.AddYears(-2).AddDays(-5); // sin ninguna inscripción (antes no se cobraba)
            int id = CrearJugador(c, t, "99999982", alta, "Femenino");

            PlayerDAO.GenerarInscripcionesAnuales(c, t, Hoy);

            var filas = Inscripciones(c, t, id);
            Assert.Single(filas);
            Assert.Equal((30_000m, Hoy.AddDays(-5), 0), filas[0]);
        }
        finally { t.Rollback(); }
    }

    [IntegracionFact]
    public void DentroDelAnio_NoSeEmiteNada()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        try
        {
            ArancelDeInscripcion(c, t, "Masculino", 70_000m);
            DateTime alta = Hoy.AddDays(-100);
            int id = CrearJugador(c, t, "99999983", alta);
            PlayerDAO.InsertPendingCharge(c, t, id, 70_000m, alta, "Inscripcion");

            PlayerDAO.GenerarInscripcionesAnuales(c, t, Hoy);

            Assert.Single(Inscripciones(c, t, id));
        }
        finally { t.Rollback(); }
    }

    [IntegracionFact]
    public void QuienEstaDeBaja_NoRecibeLaSiguiente()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        try
        {
            ArancelDeInscripcion(c, t, "Masculino", 70_000m);
            DateTime alta = Hoy.AddYears(-1).AddDays(-3);
            int id = CrearJugador(c, t, "99999984", alta, baja: Hoy.AddDays(-30));
            PlayerDAO.InsertPendingCharge(c, t, id, 70_000m, alta, "Inscripcion");

            PlayerDAO.GenerarInscripcionesAnuales(c, t, Hoy);

            Assert.Single(Inscripciones(c, t, id)); // solo cuenta hasta terminar el año que ya tenía
        }
        finally { t.Rollback(); }
    }

    [IntegracionFact]
    public void UnAniversarioMuyViejoNoSeCobraAhora()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        try
        {
            ArancelDeInscripcion(c, t, "Masculino", 70_000m);
            DateTime alta = Hoy.AddYears(-1).AddDays(-(InscripcionAnual.DiasParaEmitir + 10));
            int id = CrearJugador(c, t, "99999985", alta);
            PlayerDAO.InsertPendingCharge(c, t, id, 70_000m, alta, "Inscripcion");

            PlayerDAO.GenerarInscripcionesAnuales(c, t, Hoy);

            Assert.Single(Inscripciones(c, t, id));
        }
        finally { t.Rollback(); }
    }

    [IntegracionFact]
    public void SiElArancelEmpezoDespuesDelAniversario_NoSeCobraEseAnio()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        try
        {
            // Las mujeres no pagaban; el arancel empezó hace 2 días, después de su aniversario de hace 5.
            ArancelDeInscripcion(c, t, "Femenino", 30_000m, desde: Hoy.AddDays(-2));
            int id = CrearJugador(c, t, "99999986", Hoy.AddYears(-2).AddDays(-5), "Femenino");

            PlayerDAO.GenerarInscripcionesAnuales(c, t, Hoy);

            Assert.Empty(Inscripciones(c, t, id));
        }
        finally { t.Rollback(); }
    }

    [IntegracionFact]
    public void ConLaInscripcionEnCero_NoSeEmiteNada()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        try
        {
            ArancelDeInscripcion(c, t, "Femenino", 0m);
            int id = CrearJugador(c, t, "99999987", Hoy.AddYears(-1).AddDays(-3), "Femenino");

            PlayerDAO.GenerarInscripcionesAnuales(c, t, Hoy);

            Assert.Empty(Inscripciones(c, t, id));
        }
        finally { t.Rollback(); }
    }

    [IntegracionFact]
    public void ElReingresoDentroDelAnio_NoLaPagaDeNuevo()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        try
        {
            ArancelDeInscripcion(c, t, "Masculino", 70_000m);
            DateTime alta = Hoy.AddDays(-100);
            int id = CrearJugador(c, t, "99999988", alta, baja: Hoy.AddDays(-20));
            PlayerDAO.InsertPendingCharge(c, t, id, 70_000m, alta, "Inscripcion");

            Assert.True(JugadoresDao.ReactivarCore(c, t, id, Hoy));

            Assert.Single(Inscripciones(c, t, id));
        }
        finally { t.Rollback(); }
    }

    [IntegracionFact]
    public void ElReingresoDespuesDeTerminadoElAnio_EmpiezaUnoNuevoHoy()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        try
        {
            ArancelDeInscripcion(c, t, "Masculino", 75_000m);
            DateTime alta = Hoy.AddYears(-1).AddDays(-100);
            int id = CrearJugador(c, t, "99999989", alta, baja: Hoy.AddDays(-200));
            PlayerDAO.InsertPendingCharge(c, t, id, 70_000m, alta, "Inscripcion");

            Assert.True(JugadoresDao.ReactivarCore(c, t, id, Hoy));

            var filas = Inscripciones(c, t, id);
            Assert.Equal(2, filas.Count);
            Assert.Equal((75_000m, Hoy, 0), filas[1]); // el arancel de hoy, con el año nuevo desde hoy
        }
        finally { t.Rollback(); }
    }

    [IntegracionFact]
    public void ElReingresoDeQuienNuncaTuvoInscripcion_SigueElAnioDeSuAlta()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        try
        {
            ArancelDeInscripcion(c, t, "Femenino", 30_000m);
            int dentro = CrearJugador(c, t, "99999978", Hoy.AddDays(-100), "Femenino", baja: Hoy.AddDays(-20)); // año de su alta en curso
            int fuera = CrearJugador(c, t, "99999977", Hoy.AddDays(-500), "Femenino", baja: Hoy.AddDays(-20));  // su año ya terminó

            Assert.True(JugadoresDao.ReactivarCore(c, t, dentro, Hoy));
            Assert.True(JugadoresDao.ReactivarCore(c, t, fuera, Hoy));

            Assert.Empty(Inscripciones(c, t, dentro));
            Assert.Equal([(30_000m, Hoy, 0)], Inscripciones(c, t, fuera));
        }
        finally { t.Rollback(); }
    }

    [IntegracionFact]
    public void DosAniosConviven_PeroNoDosDelMismoAnio()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        try
        {
            int id = CrearJugador(c, t, "99999980", Hoy.AddYears(-2));
            PlayerDAO.InsertPendingCharge(c, t, id, 60_000m, Hoy.AddYears(-1), "Inscripcion"); // sin pagar
            PlayerDAO.InsertPendingCharge(c, t, id, 70_000m, Hoy, "Inscripcion");              // la del año nuevo

            Assert.Equal(2, Inscripciones(c, t, id).Count);

            // Misma fecha: lo impide el índice único. Punto de guardado para no perder la transacción.
            t.Save("antes");
            Assert.Throws<SqlException>(() => PlayerDAO.InsertPendingCharge(c, t, id, 70_000m, Hoy, "Inscripcion"));
            t.Rollback("antes");
        }
        finally { t.Rollback(); }
    }

    [IntegracionFact]
    public void LasFilasDeUnaMismaDeudaDeInscripcionSonLasDeSuAnio()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        try
        {
            int id = CrearJugador(c, t, "99999979", Hoy.AddYears(-2));
            DateTime anioAnterior = Hoy.AddYears(-1);
            // Un pago parcial por año (cada fila lleva el vencimiento de su año) y el saldo pendiente del año actual.
            Ejecutar(c, t, @"INSERT INTO PAGOS (PK_id_pago, FK_id_jugador, monto_base, monto_final, fecha_pago, metodo_pago, fecha_vencimiento, estado, concepto)
                             VALUES (NEXT VALUE FOR dbo.SEQ_PAGOS, @id, 60000, 60000, @pago, 'Efectivo', @v, 1, 'Inscripcion')",
                ("@id", id), ("@pago", anioAnterior), ("@v", anioAnterior));
            Ejecutar(c, t, @"INSERT INTO PAGOS (PK_id_pago, FK_id_jugador, monto_base, monto_final, fecha_pago, metodo_pago, fecha_vencimiento, estado, concepto)
                             VALUES (NEXT VALUE FOR dbo.SEQ_PAGOS, @id, 70000, 20000, @pago, 'Efectivo', @v, 1, 'Inscripcion')",
                ("@id", id), ("@pago", Hoy), ("@v", Hoy));
            PlayerDAO.InsertPendingCharge(c, t, id, 50_000m, Hoy, "Inscripcion");

            var dao = new PagosDao(Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable)!);
            var filas = dao.ObtenerFilasDeLaMismaDeuda(c, t, new Pago { IdJugador = id, Concepto = "Inscripcion", FechaVencimiento = Hoy });

            Assert.Equal(2, filas.Count);                          // el pago parcial y el saldo del año actual
            Assert.All(filas, f => Assert.Equal(Hoy, f.FechaVencimiento!.Value.Date));
        }
        finally { t.Rollback(); }
    }
}
