namespace ApiGestion.Tests;

using DaoLibrary;
using EntityLibrary;
using Microsoft.Data.SqlClient;

// SQL de la generación de cuotas por alta/baja, de la anulación de pagos y de la hora del club (SqlReloj),
// ejecutado contra una base real con los triggers de auditoría. Todo dentro de una transacción que se revierte.
[Collection(BaseDeDatosCollection.Nombre)]
public class PagosDaoAltaBajaYAnulacionIntegrationTests
{
    private static string Conexion => Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable)!;

    // Un jugador de prueba con un arancel propio de su categoría en un año que no choca con datos reales.
    private static int CrearJugador(SqlConnection connection, SqlTransaction transaction, DateTime alta, DateTime? baja, int idCategoria)
    {
        var player = new Player
        {
            FirstName = "Prueba", LastName = "Integracion", Dni = "99" + Random.Shared.Next(100000, 999999),
            BirthDate = new DateTime(2010, 1, 1), Gender = "Masculino", CategoryId = idCategoria, JoinDate = alta
        };
        int personId = PlayerDAO.InsertPerson(connection, transaction, player);
        int playerId = PlayerDAO.InsertPlayer(connection, transaction, personId, player);
        if (baja.HasValue)
        {
            using var cmd = new SqlCommand("UPDATE JUGADORES SET fecha_baja = @b WHERE PK_id_jugador = @id", connection, transaction);
            cmd.Parameters.AddWithValue("@b", baja.Value);
            cmd.Parameters.AddWithValue("@id", playerId);
            cmd.ExecuteNonQuery();
        }
        return playerId;
    }

    private static int PrimeraCategoria(SqlConnection connection, SqlTransaction transaction)
    {
        using var cmd = new SqlCommand("SELECT TOP (1) PK_id_categoria FROM CATEGORIAS ORDER BY PK_id_categoria", connection, transaction);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void ArancelDeCategoria(SqlConnection connection, SqlTransaction transaction, int idCategoria, decimal monto, DateTime desde)
    {
        using var cmd = new SqlCommand("INSERT INTO ARANCELES (genero, FK_id_categoria, monto, vigente_desde) VALUES (NULL, @c, @m, @d)", connection, transaction);
        cmd.Parameters.AddWithValue("@c", idCategoria);
        cmd.Parameters.AddWithValue("@m", monto);
        cmd.Parameters.AddWithValue("@d", desde);
        cmd.ExecuteNonQuery();
    }

    [IntegracionFact]
    public void LaCuotaSoloSeEmiteEnLosMesesEnQueElJugadorEstuvoEnElClub()
    {
        using var connection = new SqlConnection(Conexion);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            var dao = new PagosDao(Conexion);
            int categoria = PrimeraCategoria(connection, transaction);
            ArancelDeCategoria(connection, transaction, categoria, 12_345m, new DateTime(2098, 1, 1));
            int jugador = CrearJugador(connection, transaction, alta: new DateTime(2098, 3, 10), baja: new DateTime(2098, 5, 20), categoria);

            Assert.False(dao.EmitirCuotaDeJugador(connection, transaction, jugador, 2, 2098)); // antes del alta
            Assert.True(dao.EmitirCuotaDeJugador(connection, transaction, jugador, 3, 2098));  // mes del alta
            Assert.True(dao.EmitirCuotaDeJugador(connection, transaction, jugador, 5, 2098));  // mes de la baja
            Assert.False(dao.EmitirCuotaDeJugador(connection, transaction, jugador, 6, 2098)); // después de la baja
            Assert.False(dao.EmitirCuotaDeJugador(connection, transaction, jugador, 3, 2098)); // ya la tiene

            var cuota = dao.ObtenerPagoPendienteDeJugadorEnPeriodo(connection, transaction, jugador, 3, 2098);
            Assert.NotNull(cuota);
            Assert.Equal(12_345m, cuota!.MontoFinal);
            Assert.Null(dao.ObtenerDescuentoAplicableEnPeriodo(connection, transaction, jugador, cuota.FechaVencimiento!.Value));
        }
        finally { transaction.Rollback(); }
    }

    [IntegracionFact]
    public void AnularUnAbono_LoRegistraEnPagosAnuladosYLoSacaDePagos()
    {
        using var connection = new SqlConnection(Conexion);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            var dao = new PagosDao(Conexion);
            int categoria = PrimeraCategoria(connection, transaction);
            int jugador = CrearJugador(connection, transaction, alta: new DateTime(2098, 1, 1), baja: null, categoria);
            int idUsuario;
            using (var cmd = new SqlCommand("SELECT TOP (1) PK_id_usuario FROM USUARIO ORDER BY PK_id_usuario", connection, transaction))
                idUsuario = Convert.ToInt32(cmd.ExecuteScalar());

            int idAbono = dao.InsertarPago(connection, transaction, new Pago
            {
                IdJugador = jugador, MontoBase = 10_000m, MontoFinal = 10_000m, MetodoPago = "Efectivo",
                FechaPago = new DateTime(2098, 1, 5), FechaVencimiento = new DateTime(2098, 1, 1), Estado = true, IdUsuarioRegistro = idUsuario
            });

            var abono = dao.ObtenerPagoParaAnular(connection, transaction, idAbono);
            Assert.NotNull(abono);
            Assert.Equal("Cuota", abono!.Concepto);
            Assert.Single(dao.ObtenerFilasDeLaMismaDeuda(connection, transaction, abono));

            dao.RegistrarAnulacionYEliminarAbono(connection, transaction, abono, "Prueba de integración", idUsuario);

            Assert.Null(dao.ObtenerPagoParaAnular(connection, transaction, idAbono));
            using var anulado = new SqlCommand("SELECT monto, motivo, fecha_hora_anulacion FROM PAGOS_ANULADOS WHERE id_pago = @id", connection, transaction);
            anulado.Parameters.AddWithValue("@id", idAbono);
            using var reader = anulado.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(10_000m, reader.GetDecimal(0));
            Assert.Equal("Prueba de integración", reader.GetString(1));
        }
        finally { transaction.Rollback(); }
    }

    [IntegracionFact]
    public void LaHoraDelClubSaleEnHoraArgentina()
    {
        using var connection = SqlConnectionFactory.Open(Conexion);
        using var cmd = new SqlCommand($"SELECT {SqlReloj.Ahora}, {SqlReloj.Hoy}, SYSUTCDATETIME()", connection);
        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read());

        var diferencia = reader.GetDateTime(2) - reader.GetDateTime(0);
        Assert.InRange(diferencia.TotalHours, 2.99, 3.01); // UTC-3, sin horario de verano
        Assert.Equal(reader.GetDateTime(0).Date, reader.GetDateTime(1));
        Assert.InRange((RelojNegocio.Ahora - reader.GetDateTime(0)).TotalMinutes, -2, 2);
    }

    // Consultas de solo lectura cuyo SQL cambió (GETDATE -> SqlReloj): se ejecutan para comprobar que siguen siendo válidas.
    [IntegracionFact]
    public void LasConsultasConLaHoraDelClubSiguenSiendoValidas()
    {
        var pagos = new PagosDao(Conexion);
        pagos.ObtenerResumen();
        pagos.ObtenerResumenHoy();
        new EstadisticasDao(Conexion).ObtenerResumenGeneral();
        new DiscountDao(Conexion).GetActiveDiscounts();
        new DiscountDao(Conexion).GetAllDiscounts();
        new EnrollmentFeeDAO(Conexion).GetCurrentEnrollmentFee();
        Assert.NotNull(new JugadoresDao(Conexion).ListarJugadores());
        // Consultas que ahora leen fecha_baja o cancelada_por_baja.
        pagos.ObtenerPendientesAgrupados();
        pagos.GetPlayerAccounts(onlyDebtors: false);
    }

    // Regla del club: al dar de baja, la beca se cancela sola. Lo que el jugador pagó con la beca vigente sigue
    // PAGADO; lo que quedó pendiente conserva el descuento de esos meses y se marca "cancelada por baja"; y las
    // cuotas sin pagos de meses posteriores a la baja se quitan.
    [IntegracionFact]
    public void DarDeBaja_CancelaLaBecaYRespetaLoYaPagado()
    {
        using var connection = new SqlConnection(Conexion);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            var dao = new PagosDao(Conexion);
            int categoria = PrimeraCategoria(connection, transaction);
            ArancelDeCategoria(connection, transaction, categoria, 10_000m, new DateTime(2098, 1, 1));
            int jugador = CrearJugador(connection, transaction, alta: new DateTime(2098, 1, 1), baja: null, categoria);

            // Beca del 50 % todo 2098.
            int idBeca;
            using (var beca = new SqlCommand(@"
                DECLARE @nueva TABLE (id INT);
                INSERT INTO JUGADORES_DESCUENTOS (FK_id_jugador, FK_id_descuento, tipo_valor, porcentaje, fecha_inicio, fecha_fin)
                OUTPUT INSERTED.PK_id_jugador_descuento INTO @nueva (id)
                VALUES (@j, (SELECT TOP (1) PK_id_descuento FROM TIPO_DESCUENTO ORDER BY PK_id_descuento), '%', 50, '2098-01-01', '2098-12-31');
                SELECT id FROM @nueva;", connection, transaction))
            {
                beca.Parameters.AddWithValue("@j", jugador);
                idBeca = Convert.ToInt32(beca.ExecuteScalar());
            }

            Assert.True(dao.EmitirCuotaDeJugador(connection, transaction, jugador, 1, 2098));
            Assert.True(dao.EmitirCuotaDeJugador(connection, transaction, jugador, 2, 2098));
            Assert.True(dao.EmitirCuotaDeJugador(connection, transaction, jugador, 3, 2098));

            // Enero: pagado completo con la beca vigente (5.000), como lo deja PagosService.
            var enero = dao.ObtenerPagoPendienteDeJugadorEnPeriodo(connection, transaction, jugador, 1, 2098)!;
            dao.InsertarPago(connection, transaction, new Pago
            {
                IdJugador = jugador, MontoBase = 10_000m, MontoFinal = 5_000m, MetodoPago = "Efectivo", IdJugadorDescuento = idBeca,
                FechaPago = new DateTime(2098, 1, 5), FechaVencimiento = new DateTime(2098, 1, 1), Estado = true
            });
            dao.EliminarPago(connection, transaction, enero.IdPago);

            var baja = JugadoresDao.DarDeBajaCore(connection, transaction, jugador, new DateTime(2098, 2, 15));

            Assert.Equal(BajaJugadorResultado.Ok, baja.Resultado);
            Assert.Equal(1, baja.CuotasEliminadas);      // marzo: posterior a la baja y sin pagos
            Assert.Equal(1, baja.BeneficiosCancelados);  // la beca

            // Enero sigue pagado: sin fila pendiente y con su abono intacto.
            Assert.Null(dao.ObtenerPagoPendienteDeJugadorEnPeriodo(connection, transaction, jugador, 1, 2098));
            Assert.NotNull(dao.ObtenerPagoAbonadoDeJugadorEnPeriodo(connection, transaction, jugador, 1, 2098));
            Assert.Null(dao.ObtenerPagoPendienteDeJugadorEnPeriodo(connection, transaction, jugador, 3, 2098));

            // Febrero: pendiente, con el 50 % todavía aplicado y la marca de beca cancelada por baja.
            var febrero = dao.ObtenerPagoPendienteDeJugadorEnPeriodo(connection, transaction, jugador, 2, 2098)!;
            using var saldo = new SqlCommand($@"
                SELECT ({DescuentosSql.SaldoAjustadoExpr}) AS saldo, d.cancelada_por_baja
                FROM PAGOS pg {DescuentosSql.ApplyDescuentoActivo}
                WHERE pg.PK_id_pago = @id", connection, transaction);
            saldo.Parameters.AddWithValue("@id", febrero.IdPago);
            using (var reader = saldo.ExecuteReader())
            {
                Assert.True(reader.Read());
                Assert.Equal(5_000m, Convert.ToDecimal(reader["saldo"]));
                Assert.True(Convert.ToBoolean(reader["cancelada_por_baja"]));
            }

            using var becaGuardada = new SqlCommand(
                "SELECT estado_activo, cancelada_por_baja, fecha_cancelacion FROM JUGADORES_DESCUENTOS WHERE PK_id_jugador_descuento = @id",
                connection, transaction);
            becaGuardada.Parameters.AddWithValue("@id", idBeca);
            using var b = becaGuardada.ExecuteReader();
            Assert.True(b.Read());
            Assert.False(b.GetBoolean(0));
            Assert.True(b.GetBoolean(1));
            Assert.Equal(new DateTime(2098, 2, 15), b.GetDateTime(2));
        }
        finally { transaction.Rollback(); }
    }

    // Reactivar es un reingreso: la fecha de alta pasa a ser el día de la reactivación, así los meses en que
    // estuvo afuera no aparecen como cuotas faltantes. La cuota del mes se emite solo si ya pasó el día 10.
    [IntegracionFact]
    public void Reactivar_EsUnReingresoYRespetaElDia10()
    {
        using var connection = new SqlConnection(Conexion);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            var dao = new PagosDao(Conexion);
            int categoria = PrimeraCategoria(connection, transaction);
            ArancelDeCategoria(connection, transaction, categoria, 12_345m, new DateTime(2098, 1, 1));
            int antes = CrearJugador(connection, transaction, alta: new DateTime(2098, 1, 10), baja: new DateTime(2098, 3, 20), categoria);
            int despues = CrearJugador(connection, transaction, alta: new DateTime(2098, 1, 10), baja: new DateTime(2098, 3, 20), categoria);

            Assert.True(JugadoresDao.ReactivarCore(connection, transaction, antes, new DateTime(2098, 7, 9)));
            Assert.True(JugadoresDao.ReactivarCore(connection, transaction, despues, new DateTime(2098, 7, 10)));
            Assert.False(JugadoresDao.ReactivarCore(connection, transaction, despues, new DateTime(2098, 7, 10))); // ya estaba activo

            Assert.Null(dao.ObtenerPagoPendienteDeJugadorEnPeriodo(connection, transaction, antes, 7, 2098));   // la genera el 10
            Assert.NotNull(dao.ObtenerPagoPendienteDeJugadorEnPeriodo(connection, transaction, despues, 7, 2098));

            // Abril a junio (estuvo afuera) no figuran como faltantes.
            foreach (int mes in new[] { 4, 5, 6 })
            {
                Assert.DoesNotContain(CuotasFaltantesDao.ObtenerFaltantesDelMesCore(connection, transaction, new DateTime(2098, mes, 1)),
                    c => c.IdJugador == antes || c.IdJugador == despues);
            }

            using var cmd = new SqlCommand("SELECT fecha_alta, fecha_baja FROM JUGADORES WHERE PK_id_jugador = @id", connection, transaction);
            cmd.Parameters.AddWithValue("@id", antes);
            using var r = cmd.ExecuteReader();
            Assert.True(r.Read());
            Assert.Equal(new DateTime(2098, 7, 9), r.GetDateTime(0));
            Assert.True(r.IsDBNull(1));
        }
        finally { transaction.Rollback(); }
    }

    // "Pagos registrados" paginado en la base: busca por nombre sin acentos ni mayúsculas, filtra por estado
    // y devuelve el total para el paginador.
    [IntegracionFact]
    public void PagosRegistrados_FiltraYPaginaEnLaBase()
    {
        using var connection = new SqlConnection(Conexion);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            var dao = new PagosDao(Conexion);
            int categoria = PrimeraCategoria(connection, transaction);
            string apellido = "Zúñiga" + Random.Shared.Next(1000, 9999);
            var player = new Player
            {
                FirstName = "Prueba", LastName = apellido, Dni = "97" + Random.Shared.Next(100000, 999999),
                BirthDate = new DateTime(2010, 1, 1), Gender = "Masculino", CategoryId = categoria, JoinDate = new DateTime(2096, 1, 1)
            };
            int jugador = PlayerDAO.InsertPlayer(connection, transaction, PlayerDAO.InsertPerson(connection, transaction, player), player);

            // Tres cuotas pagas completas y una inscripción con un abono parcial (las cuotas solo se listan
            // cuando están pagas completas; la inscripción, también con saldo pendiente).
            foreach (int mes in new[] { 1, 2, 3 })
            {
                dao.InsertarPago(connection, transaction, new Pago
                {
                    IdJugador = jugador, MontoBase = 10_000m, MontoFinal = 10_000m, MetodoPago = "Efectivo",
                    FechaPago = new DateTime(2096, mes, 5), FechaVencimiento = new DateTime(2096, mes, 1), Estado = true, Concepto = "Cuota"
                });
            }
            dao.InsertarPago(connection, transaction, new Pago
            {
                IdJugador = jugador, MontoBase = 10_000m, MontoFinal = 4_000m, MetodoPago = "Efectivo", FechaPago = new DateTime(2096, 1, 6),
                FechaVencimiento = new DateTime(2096, 1, 1), Estado = true, Concepto = "Inscripcion"
            });
            dao.InsertarPago(connection, transaction, new Pago
            {
                IdJugador = jugador, MontoBase = 10_000m, MontoFinal = 6_000m, FechaVencimiento = new DateTime(2096, 1, 1), Estado = false, Concepto = "Inscripcion"
            });

            // Sin acentos ni mayúsculas encuentra igual.
            string busqueda = apellido.Replace("ú", "u").Replace("ñ", "n").ToUpperInvariant();
            var primera = PagosDao.ObtenerPagosRegistradosCore(connection, transaction, new FiltroPagosRegistrados { Busqueda = busqueda, TamanioPagina = 3 });
            Assert.Equal(4, primera.Total);
            Assert.Equal(3, primera.Items.Count);
            Assert.True(primera.TotalSinFiltros >= 4);

            var segunda = PagosDao.ObtenerPagosRegistradosCore(connection, transaction, new FiltroPagosRegistrados { Busqueda = busqueda, TamanioPagina = 3, Pagina = 2 });
            Assert.Single(segunda.Items);
            Assert.Empty(primera.Items.Select(p => p.IdPago).Intersect(segunda.Items.Select(p => p.IdPago)));

            var parciales = PagosDao.ObtenerPagosRegistradosCore(connection, transaction, new FiltroPagosRegistrados { Busqueda = busqueda, Estado = "Parcial" });
            var parcial = Assert.Single(parciales.Items);
            Assert.Equal("Inscripcion", parcial.Concepto);
            Assert.Equal(4_000m, parcial.Monto);

            // Los comodines de LIKE se buscan como texto.
            Assert.Equal(0, PagosDao.ObtenerPagosRegistradosCore(connection, transaction, new FiltroPagosRegistrados { Busqueda = "%" + apellido }).Total);
        }
        finally { transaction.Rollback(); }
    }
}
