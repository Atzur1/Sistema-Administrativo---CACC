namespace ApiGestion.Tests;

using DaoLibrary;
using EntityLibrary;
using Microsoft.Data.SqlClient;

// El SQL del estado de deuda, contra la base real dentro de una transacción que se revierte. Comprueba que coincida con
// las reglas del resto del Administrativo: el vencimiento del día 10, la beca aplicada al leer y la baja.
// Los períodos se calculan respecto de hoy para que la prueba sirva cualquier día del año.
[Collection(BaseDeDatosCollection.Nombre)]
public class EstadoDeudaDaoIntegrationTests
{
    private static string Conexion => Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable)!;

    private static void Ejecutar(SqlConnection c, SqlTransaction t, string sql, params (string Nombre, object Valor)[] parametros)
    {
        using var cmd = new SqlCommand(sql, c, t);
        foreach (var (nombre, valor) in parametros) cmd.Parameters.AddWithValue(nombre, valor);
        cmd.ExecuteNonQuery();
    }

    private static void CuotaPendiente(SqlConnection c, SqlTransaction t, int jugador, DateTime periodo, decimal monto) =>
        Ejecutar(c, t, "INSERT INTO PAGOS (FK_id_jugador, monto_base, monto_final, fecha_vencimiento, estado, concepto) VALUES (@j, @m, @m, @p, 0, 'Cuota')",
            ("@j", jugador), ("@m", monto), ("@p", periodo));

    // Un jugador sin otras cuentas pendientes y un DNI propio de la prueba.
    private static (int Jugador, string Dni, int Categoria) CrearJugador(SqlConnection c, SqlTransaction t)
    {
        string dni = "97" + Random.Shared.Next(100000, 999999);
        int categoria;
        using (var cmd = new SqlCommand("SELECT TOP (1) PK_id_categoria FROM CATEGORIAS ORDER BY PK_id_categoria", c, t))
            categoria = Convert.ToInt32(cmd.ExecuteScalar());
        var player = new Player
        {
            FirstName = "Prueba", LastName = "Estado", Dni = dni, BirthDate = new DateTime(2010, 1, 1),
            Gender = "Masculino", CategoryId = categoria, JoinDate = new DateTime(2020, 1, 1)
        };
        int jugador = PlayerDAO.InsertPlayer(c, t, PlayerDAO.InsertPerson(c, t, player), player);
        return (jugador, dni, categoria);
    }

    [IntegracionFact]
    public void ContarLasCuotasVencidasYLaDeudaConLaBecaAplicada()
    {
        using var connection = new SqlConnection(Conexion);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            var (jugador, dni, _) = CrearJugador(connection, transaction);
            DateTime ultimo = CalendarioCuotas.UltimoPeriodoEmitible(RelojNegocio.Hoy);
            DateTime vencida1 = ultimo.AddMonths(-1), vencida2 = ultimo.AddMonths(-2);

            CuotaPendiente(connection, transaction, jugador, vencida2, 100_000m);  // vencida
            CuotaPendiente(connection, transaction, jugador, vencida1, 100_000m);  // vencida, con beca del 50 %
            CuotaPendiente(connection, transaction, jugador, ultimo, 100_000m);    // todavía no vence
            Ejecutar(connection, transaction, "INSERT INTO PAGOS (FK_id_jugador, monto_base, monto_final, fecha_vencimiento, estado, concepto) VALUES (@j, 70000, 70000, @p, 0, 'Inscripcion')",
                ("@j", jugador), ("@p", vencida2));                                  // la inscripción suma a la deuda, no a las cuotas vencidas
            Ejecutar(connection, transaction, @"INSERT INTO JUGADORES_DESCUENTOS (FK_id_jugador, FK_id_descuento, tipo_valor, porcentaje, fecha_inicio, fecha_fin)
                VALUES (@j, (SELECT TOP (1) PK_id_descuento FROM TIPOS_DESCUENTO ORDER BY PK_id_descuento), '%', 50, @ini, @fin)",
                ("@j", jugador), ("@ini", vencida1), ("@fin", CalendarioCuotas.FinDelPeriodo(vencida1)));

            var estado = EstadoDeudaDao.ObtenerPorDniCore(connection, transaction, new[] { dni }, null).Single();

            Assert.Equal(jugador, estado.IdJugador);
            Assert.Equal(2, estado.CuotasVencidas);
            Assert.Equal(100_000m + 50_000m + 100_000m + 70_000m, estado.DeudaTotal);
            Assert.Equal(EstadoDeportivo.Inhabilitado, estado.Estado);
            Assert.False(estado.AccesoPermitido);
        }
        finally { transaction.Rollback(); }
    }

    [IntegracionFact]
    public void UnaBecaDel100PorCiento_DejaLaCuotaSinDeudaYNoCuentaComoVencida()
    {
        using var connection = new SqlConnection(Conexion);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            var (jugador, dni, _) = CrearJugador(connection, transaction);
            DateTime vencida = CalendarioCuotas.UltimoPeriodoEmitible(RelojNegocio.Hoy).AddMonths(-1);
            CuotaPendiente(connection, transaction, jugador, vencida, 100_000m);
            Ejecutar(connection, transaction, @"INSERT INTO JUGADORES_DESCUENTOS (FK_id_jugador, FK_id_descuento, tipo_valor, porcentaje, fecha_inicio, fecha_fin)
                VALUES (@j, (SELECT TOP (1) PK_id_descuento FROM TIPOS_DESCUENTO ORDER BY PK_id_descuento), '%', 100, @ini, @fin)",
                ("@j", jugador), ("@ini", vencida), ("@fin", CalendarioCuotas.FinDelPeriodo(vencida)));

            var estado = EstadoDeudaDao.ObtenerPorDniCore(connection, transaction, new[] { dni }, null).Single();

            Assert.Equal(0, estado.CuotasVencidas);
            Assert.Equal(0m, estado.DeudaTotal);
            Assert.Equal(EstadoDeportivo.Habilitado, estado.Estado);
        }
        finally { transaction.Rollback(); }
    }

    [IntegracionFact]
    public void ElJugadorSinDeuda_Aparece_ConCeroYUnaBajaSeReconoce()
    {
        using var connection = new SqlConnection(Conexion);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            var (jugador, dni, _) = CrearJugador(connection, transaction);

            var limpio = EstadoDeudaDao.ObtenerPorDniCore(connection, transaction, new[] { dni }, null).Single();
            Assert.Equal(0, limpio.CuotasVencidas);
            Assert.Equal(0m, limpio.DeudaTotal);
            Assert.Equal(EstadoDeportivo.Habilitado, limpio.Estado);

            Ejecutar(connection, transaction, "UPDATE JUGADORES SET fecha_baja = @b WHERE PK_id_jugador = @j", ("@b", RelojNegocio.Hoy), ("@j", jugador));
            var baja = EstadoDeudaDao.ObtenerPorDniCore(connection, transaction, new[] { dni }, null).Single();
            Assert.Equal(EstadoDeportivo.Baja, baja.Estado);
            Assert.Equal(RelojNegocio.Hoy, baja.FechaBaja);
        }
        finally { transaction.Rollback(); }
    }

    [IntegracionFact]
    public void ElFiltroDeCategorias_DejaAfueraALosDeOtraCategoria_YAdmiteLaDeMembresia()
    {
        using var connection = new SqlConnection(Conexion);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            var (jugador, dni, categoria) = CrearJugador(connection, transaction);
            int otra;
            using (var cmd = new SqlCommand("SELECT TOP (1) PK_id_categoria FROM CATEGORIAS WHERE PK_id_categoria <> @c ORDER BY PK_id_categoria", connection, transaction))
            {
                cmd.Parameters.AddWithValue("@c", categoria);
                otra = Convert.ToInt32(cmd.ExecuteScalar());
            }

            Assert.Single(EstadoDeudaDao.ObtenerPorDniCore(connection, transaction, new[] { dni }, new[] { categoria }));
            Assert.Empty(EstadoDeudaDao.ObtenerPorDniCore(connection, transaction, new[] { dni }, new[] { otra }));

            // Pertenece también a la otra categoría (JUGADORES_CATEGORIAS): su Director Técnico también lo ve.
            Ejecutar(connection, transaction, "INSERT INTO JUGADORES_CATEGORIAS (FK_id_jugador, FK_id_categoria) VALUES (@j, @c)", ("@j", jugador), ("@c", otra));
            Assert.Single(EstadoDeudaDao.ObtenerPorDniCore(connection, transaction, new[] { dni }, new[] { otra }));
        }
        finally { transaction.Rollback(); }
    }

    [IntegracionFact]
    public void UnDniQueNoEsDeNingunJugador_NoDevuelveNada()
    {
        using var connection = new SqlConnection(Conexion);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            Assert.Empty(EstadoDeudaDao.ObtenerPorDniCore(connection, transaction, new[] { "10000001" }, null));
        }
        finally { transaction.Rollback(); }
    }
}
