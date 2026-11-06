namespace ApiGestion.Tests;

using DaoLibrary;
using EntityLibrary;
using Microsoft.Data.SqlClient;

// HU-107: la tabla "Pagos del período" paginada y ordenada en SQL Server, contra una base real. Todo en un año
// que no choca con datos reales (2097) y dentro de una transacción que se revierte.
[Collection(BaseDeDatosCollection.Nombre)]
public class RecaudacionDaoIntegrationTests
{
    private static string Conexion => Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable)!;

    private const int Anio = 2097;
    private const int Mes = 3;

    private static int CrearJugador(SqlConnection connection, SqlTransaction transaction, int idCategoria)
    {
        var player = new Player
        {
            FirstName = "Recaudacion", LastName = "Prueba", Dni = "98" + Random.Shared.Next(100000, 999999),
            BirthDate = new DateTime(2010, 1, 1), Gender = "Masculino", CategoryId = idCategoria, JoinDate = new DateTime(2097, 1, 1)
        };
        int personId = PlayerDAO.InsertPerson(connection, transaction, player);
        return PlayerDAO.InsertPlayer(connection, transaction, personId, player);
    }

    private static List<int> Categorias(SqlConnection connection, SqlTransaction transaction)
    {
        using var cmd = new SqlCommand("SELECT TOP (2) PK_id_categoria FROM CATEGORIAS ORDER BY PK_id_categoria", connection, transaction);
        using var reader = cmd.ExecuteReader();
        var ids = new List<int>();
        while (reader.Read()) ids.Add(reader.GetInt32(0));
        return ids;
    }

    private static int Pago(SqlConnection connection, SqlTransaction transaction, int idJugador, decimal monto,
        DateTime fechaPago, DateTime? registro, string metodo = "Efectivo", bool cobrado = true)
    {
        using var cmd = new SqlCommand(@"
            INSERT INTO PAGOS (FK_id_jugador, monto_base, monto_final, fecha_pago, metodo_pago, fecha_vencimiento, estado, concepto, fecha_hora_registro)
            OUTPUT inserted.PK_id_pago INTO @ids
            VALUES (@j, @m, @m, @f, @metodo, @f, @estado, 'Cuota', @r);", connection, transaction);
        cmd.CommandText = "DECLARE @ids TABLE (id INT);" + cmd.CommandText + "SELECT id FROM @ids;";
        cmd.Parameters.AddWithValue("@j", idJugador);
        cmd.Parameters.AddWithValue("@m", monto);
        cmd.Parameters.AddWithValue("@f", fechaPago);
        cmd.Parameters.AddWithValue("@metodo", metodo);
        cmd.Parameters.AddWithValue("@estado", cobrado);
        cmd.Parameters.AddWithValue("@r", (object?)registro ?? DBNull.Value);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static FiltroPagosDelPeriodo Filtro(int pagina = 1, int tamanio = 10,
        OrdenPagosDelPeriodo orden = OrdenPagosDelPeriodo.Fecha, bool descendente = true, int? idCategoria = null, int mes = Mes)
        => new() { Anio = Anio, Mes = mes, Pagina = pagina, TamanioPagina = tamanio, OrdenarPor = orden, Descendente = descendente, IdCategoria = idCategoria };

    private static void EnTransaccion(Action<SqlConnection, SqlTransaction> prueba)
    {
        using var connection = new SqlConnection(Conexion);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try { prueba(connection, transaction); }
        finally { transaction.Rollback(); }
    }

    [IntegracionFact]
    public void MesConMasDe100Pagos_LasPaginasCubrenTodoSinRepetirNiPerder()
    {
        EnTransaccion((connection, transaction) =>
        {
            int jugador = CrearJugador(connection, transaction, Categorias(connection, transaction)[0]);
            var esperados = new HashSet<int>();
            for (int i = 0; i < 105; i++)
            {
                // Varios cobros por día y montos repetidos: el desempate por id es el que evita filas que saltan.
                var dia = new DateTime(Anio, Mes, 1 + i % 28);
                esperados.Add(Pago(connection, transaction, jugador, 1000m * (1 + i % 7), dia, dia.AddHours(9 + i % 10), i % 3 == 0 ? "Transferencia" : "Efectivo"));
            }
            // Fuera del período o sin cobrar: no cuentan en la tabla ni en los totales.
            Pago(connection, transaction, jugador, 777m, new DateTime(Anio, Mes - 1, 28), null);
            Pago(connection, transaction, jugador, 777m, new DateTime(Anio, Mes + 1, 1), null);
            Pago(connection, transaction, jugador, 777m, new DateTime(Anio, Mes, 15), null, cobrado: false);

            foreach (var orden in new[] { OrdenPagosDelPeriodo.Fecha, OrdenPagosDelPeriodo.Monto })
            {
                foreach (bool descendente in new[] { true, false })
                {
                    var vistos = new List<long>();
                    var primera = RecaudacionDao.ObtenerPagosDelPeriodoCore(connection, transaction, Filtro(1, 10, orden, descendente));
                    Assert.Equal(105, primera.Total);
                    Assert.Equal(11, primera.TotalPaginas);
                    for (int pagina = 1; pagina <= primera.TotalPaginas; pagina++)
                    {
                        var resultado = RecaudacionDao.ObtenerPagosDelPeriodoCore(connection, transaction, Filtro(pagina, 10, orden, descendente));
                        Assert.Equal(105, resultado.Total);
                        Assert.Equal(pagina == 11 ? 5 : 10, resultado.Items.Count);
                        vistos.AddRange(resultado.Items.Select(p => p.IdPago));
                    }

                    Assert.Equal(105, vistos.Count);
                    Assert.Equal(105, vistos.Distinct().Count());
                    Assert.True(esperados.SetEquals(vistos.Select(id => (int)id)));
                }
            }

            // Pasada la última página no hay filas, pero el total sigue siendo el del período.
            var despues = RecaudacionDao.ObtenerPagosDelPeriodoCore(connection, transaction, Filtro(12));
            Assert.Empty(despues.Items);
            Assert.Equal(105, despues.Total);
        });
    }

    [IntegracionFact]
    public void LosTotalesSonDelPeriodoCompletoYCoincidenConLaTabla()
    {
        EnTransaccion((connection, transaction) =>
        {
            var categorias = Categorias(connection, transaction);
            int jugadorA = CrearJugador(connection, transaction, categorias[0]);
            int jugadorB = CrearJugador(connection, transaction, categorias[1]);
            for (int i = 0; i < 30; i++)
            {
                var dia = new DateTime(Anio, Mes, 1 + i % 28);
                Pago(connection, transaction, i % 2 == 0 ? jugadorA : jugadorB, 2500m + i, dia, dia.AddHours(10), i % 4 == 0 ? "Transferencia" : "Efectivo");
            }

            var totales = RecaudacionDao.ObtenerTotalesCore(connection, transaction, Anio, Mes, null);
            var pagina = RecaudacionDao.ObtenerPagosDelPeriodoCore(connection, transaction, Filtro(2));
            var todas = Enumerable.Range(1, pagina.TotalPaginas)
                .SelectMany(n => RecaudacionDao.ObtenerPagosDelPeriodoCore(connection, transaction, Filtro(n)).Items).ToList();

            Assert.Equal(30, totales.Pagos);
            Assert.Equal(totales.Pagos, pagina.Total);
            Assert.Equal(totales.Total, todas.Sum(p => p.Monto));
            Assert.Equal(totales.Efectivo, todas.Where(p => p.MedioPago == "Efectivo").Sum(p => p.Monto));
            Assert.Equal(totales.PagosTransferencia, todas.Count(p => p.MedioPago == "Transferencia"));
            Assert.Equal(totales.Total, totales.Efectivo + totales.Transferencia);
            // Una página no es el período: la suma de 10 filas no es el total.
            Assert.NotEqual(totales.Total, pagina.Items.Sum(p => p.Monto));

            // Con filtro de categoría, tabla y totales siguen contando el mismo conjunto.
            var totalesA = RecaudacionDao.ObtenerTotalesCore(connection, transaction, Anio, Mes, categorias[0]);
            var paginaA = RecaudacionDao.ObtenerPagosDelPeriodoCore(connection, transaction, Filtro(idCategoria: categorias[0]));
            Assert.Equal(15, totalesA.Pagos);
            Assert.Equal(totalesA.Pagos, paginaA.Total);

            // La exportación trae las 30 filas, no la página, en el mismo orden que la tabla por defecto.
            var detalle = RecaudacionDao.ObtenerDetalleCore(connection, transaction, Anio, Mes, null);
            Assert.Equal(todas.Select(p => p.IdPago), detalle.Select(p => p.IdPago));
        });
    }

    [IntegracionFact]
    public void PorFecha_DentroDelMismoDiaOrdenaPorLaHoraReal()
    {
        EnTransaccion((connection, transaction) =>
        {
            int jugador = CrearJugador(connection, transaction, Categorias(connection, transaction)[0]);
            var dia = new DateTime(Anio, Mes, 10);
            // Se insertan desordenados: el id no coincide con la hora, así que si el orden saliera del id fallaría.
            int tarde = Pago(connection, transaction, jugador, 1000m, dia, dia.AddHours(18).AddMinutes(5));
            int manana = Pago(connection, transaction, jugador, 1000m, dia, dia.AddHours(8).AddMinutes(30));
            int mediodia = Pago(connection, transaction, jugador, 1000m, dia, dia.AddHours(12));
            int diaAnterior = Pago(connection, transaction, jugador, 1000m, dia.AddDays(-1), dia.AddDays(-1).AddHours(23).AddMinutes(59));
            int diaSiguiente = Pago(connection, transaction, jugador, 1000m, dia.AddDays(1), dia.AddDays(1).AddHours(7));

            var asc = RecaudacionDao.ObtenerPagosDelPeriodoCore(connection, transaction, Filtro(descendente: false)).Items.Select(p => (int)p.IdPago);
            var desc = RecaudacionDao.ObtenerPagosDelPeriodoCore(connection, transaction, Filtro(descendente: true)).Items.Select(p => (int)p.IdPago);

            Assert.Equal(new[] { diaAnterior, manana, mediodia, tarde, diaSiguiente }, asc);
            Assert.Equal(new[] { diaSiguiente, tarde, mediodia, manana, diaAnterior }, desc);
        });
    }

    [IntegracionFact]
    public void PorFecha_PriorizaRegistroRealAunqueDifieraDelDiaImputado()
    {
        EnTransaccion((connection, transaction) =>
        {
            int jugador = CrearJugador(connection, transaction, Categorias(connection, transaction)[0]);
            var dia = new DateTime(Anio, Mes, 10);
            int primero = Pago(connection, transaction, jugador, 1000m, dia.AddDays(1), dia.AddHours(8));
            int segundo = Pago(connection, transaction, jugador, 1000m, dia, dia.AddDays(1).AddHours(18));
            var asc = RecaudacionDao.ObtenerPagosDelPeriodoCore(connection, transaction, Filtro(descendente: false));
            var desc = RecaudacionDao.ObtenerPagosDelPeriodoCore(connection, transaction, Filtro());
            Assert.Equal(new long[] { primero, segundo }, asc.Items.Select(p => p.IdPago));
            Assert.Equal(new long[] { segundo, primero }, desc.Items.Select(p => p.IdPago));
        });
    }
    [IntegracionFact]
    public void PorMonto_OrdenaPorElValorNumericoYDesempataPorId()
    {
        EnTransaccion((connection, transaction) =>
        {
            int jugador = CrearJugador(connection, transaction, Categorias(connection, transaction)[0]);
            var dia = new DateTime(Anio, Mes, 5);
            // Como texto "$100.000" < "$20.000" < "$9.000": ordenado como número es al revés.
            int cien = Pago(connection, transaction, jugador, 100000m, dia, dia.AddHours(9));
            int veinte = Pago(connection, transaction, jugador, 20000m, dia, dia.AddHours(10));
            int nueve = Pago(connection, transaction, jugador, 9000m, dia, dia.AddHours(11));
            int veinteOtro = Pago(connection, transaction, jugador, 20000m, dia, dia.AddHours(12));

            var asc = RecaudacionDao.ObtenerPagosDelPeriodoCore(connection, transaction, Filtro(orden: OrdenPagosDelPeriodo.Monto, descendente: false)).Items;
            var desc = RecaudacionDao.ObtenerPagosDelPeriodoCore(connection, transaction, Filtro(orden: OrdenPagosDelPeriodo.Monto, descendente: true)).Items;

            Assert.Equal(new[] { nueve, veinte, veinteOtro, cien }, asc.Select(p => (int)p.IdPago));
            Assert.Equal(new[] { cien, veinteOtro, veinte, nueve }, desc.Select(p => (int)p.IdPago));
            Assert.Equal(new[] { 9000m, 20000m, 20000m, 100000m }, asc.Select(p => p.Monto));
        });
    }

    [IntegracionFact]
    public void CadaFilaTraeElDiaYLaHoraRealDeRegistro()
    {
        EnTransaccion((connection, transaction) =>
        {
            int jugador = CrearJugador(connection, transaction, Categorias(connection, transaction)[0]);
            var registro = new DateTime(Anio, Mes, 31, 14, 35, 12);
            Pago(connection, transaction, jugador, 5000m, registro.Date, registro);
            // Pago anterior a la columna fecha_hora_registro: solo tiene el día.
            Pago(connection, transaction, jugador, 5000m, new DateTime(Anio, Mes, 2), null);

            var filas = RecaudacionDao.ObtenerPagosDelPeriodoCore(connection, transaction, Filtro()).Items;

            Assert.Equal(new DateTime(Anio, Mes, 31), filas[0].FechaPago);
            Assert.Equal(registro, filas[0].FechaHoraRegistro);
            Assert.Equal(new DateTime(Anio, Mes, 2), filas[1].FechaPago);
            Assert.Null(filas[1].FechaHoraRegistro);
        });
    }

    [IntegracionFact]
    public void MesConPocosPagos_UnaSolaPagina_YMesVacio_SinPaginasNiErrores()
    {
        EnTransaccion((connection, transaction) =>
        {
            int jugador = CrearJugador(connection, transaction, Categorias(connection, transaction)[0]);
            for (int i = 0; i < 7; i++)
            {
                Pago(connection, transaction, jugador, 3000m, new DateTime(Anio, Mes, 3 + i), new DateTime(Anio, Mes, 3 + i, 10, 0, 0));
            }

            var pocos = RecaudacionDao.ObtenerPagosDelPeriodoCore(connection, transaction, Filtro());
            Assert.Equal(7, pocos.Total);
            Assert.Equal(1, pocos.TotalPaginas);
            Assert.Equal(7, pocos.Items.Count);
            Assert.Equal(21000m, RecaudacionDao.ObtenerTotalesCore(connection, transaction, Anio, Mes, null).Total);

            var vacio = RecaudacionDao.ObtenerPagosDelPeriodoCore(connection, transaction, Filtro(mes: 6));
            var totalesVacio = RecaudacionDao.ObtenerTotalesCore(connection, transaction, Anio, 6, null);
            Assert.Equal(0, vacio.Total);
            Assert.Equal(1, vacio.TotalPaginas);
            Assert.Empty(vacio.Items);
            Assert.Equal(0, totalesVacio.Pagos);
            Assert.Equal(0m, totalesVacio.Total);
        });
    }
}
