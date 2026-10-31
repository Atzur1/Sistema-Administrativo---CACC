using System.Data;
using Microsoft.Data.SqlClient;
using EntityLibrary;

namespace DaoLibrary;

// HU-018: recaudación de un mes, agrupada por medio de pago.
//
// Qué cuenta como cobro y con qué criterio, alineado con el resto del sistema:
//   - estado = 1 (cobrado). Las filas en estado = 0 son cuotas emitidas sin
//     pagar: no tienen metodo_pago cargado y no son plata que entró.
//   - Se filtra por fecha_pago, la misma columna que usa EstadisticasDao para
//     "recaudado del año". fecha_hora_registro es informativa y está en NULL
//     para los pagos anteriores a su migración, así que no sirve de filtro.
//   - Los pagos anulados no necesitan exclusión: anular hace DELETE de PAGOS y
//     copia la fila a PAGOS_ANULADOS, así que ya no están en la tabla.
//   - Entran todos los conceptos (Cuota e Inscripcion): la HU pide la
//     sumatoria de todos los cobros concretados del período.
//
// HU-107: la tabla se pagina y ordena en la base. Tarjetas, donut, conteo, página y exportación
// salen del mismo fragmento PagosDelPeriodo, así que no pueden contar conjuntos distintos.
public class RecaudacionDao : IRecaudacionDao
{
    private readonly string _cadenaConexion;

    public RecaudacionDao(string cadenaConexion)
    {
        _cadenaConexion = cadenaConexion;
    }

    // Los cobros de un período. Lo único que cambia entre las consultas es qué se hace con estas filas.
    internal const string PagosDelPeriodo = @"
            FROM PAGOS p
            JOIN JUGADORES j ON j.PK_id_jugador = p.FK_id_jugador
            JOIN PERSONAS pe ON pe.PK_id_persona = j.FK_id_persona
            LEFT JOIN CATEGORIAS c ON c.PK_id_categoria = j.FK_id_categoria
            WHERE p.estado = 1
              AND p.fecha_pago >= @desde
              AND p.fecha_pago < @hasta
              AND (@idCategoria IS NULL OR j.FK_id_categoria = @idCategoria)";

    private const string ColumnasDelDetalle = @"
            SELECT
                p.PK_id_pago,
                pe.apellido + ', ' + pe.nombre AS jugador,
                pe.Dni AS dni,
                ISNULL(c.nombre_categoria, '') AS categoria,
                p.monto_final AS monto,
                ISNULL(p.metodo_pago, '') AS medio_pago,
                p.fecha_pago,
                p.fecha_hora_registro";

    // Exportaciones: los totales y TODOS los cobros del período, del más reciente al más viejo.
    public RecaudacionPeriodo ObtenerDelPeriodo(int anio, int mes, int? idCategoria)
    {
        using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);
        RecaudacionPeriodo resultado = ObtenerTotalesCore(conexion, null, anio, mes, idCategoria);
        resultado.Detalle = ObtenerDetalleCore(conexion, null, anio, mes, idCategoria);
        return resultado;
    }

    // Tarjetas y donut de la pantalla: solo los totales, sin bajar las filas.
    public RecaudacionPeriodo ObtenerTotalesDelPeriodo(int anio, int mes, int? idCategoria)
    {
        using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);
        return ObtenerTotalesCore(conexion, null, anio, mes, idCategoria);
    }

    public PagosDelPeriodoPagina ObtenerPagosDelPeriodo(FiltroPagosDelPeriodo filtro)
    {
        using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);
        return ObtenerPagosDelPeriodoCore(conexion, null, filtro);
    }

    // Una sola pasada por la tabla: SUM y COUNT condicionales en vez de un
    // GROUP BY + tres consultas. Los subtotales salen de las mismas filas que
    // el total, así que efectivo + transferencia = total siempre.
    internal static RecaudacionPeriodo ObtenerTotalesCore(
        SqlConnection conexion, SqlTransaction? transaccion, int anio, int mes, int? idCategoria)
    {
        const string query = @"
            SELECT
                ISNULL(SUM(p.monto_final), 0) AS total,
                ISNULL(SUM(CASE WHEN p.metodo_pago = 'Efectivo' THEN p.monto_final ELSE 0 END), 0) AS efectivo,
                ISNULL(SUM(CASE WHEN p.metodo_pago = 'Transferencia' THEN p.monto_final ELSE 0 END), 0) AS transferencia,
                COUNT(*) AS pagos,
                -- ISNULL también en los contadores: SUM sobre cero filas devuelve
                -- NULL (a diferencia de COUNT, que devuelve 0), y un mes sin cobros
                -- es un caso normal, no un error.
                ISNULL(SUM(CASE WHEN p.metodo_pago = 'Efectivo' THEN 1 ELSE 0 END), 0) AS pagos_efectivo,
                ISNULL(SUM(CASE WHEN p.metodo_pago = 'Transferencia' THEN 1 ELSE 0 END), 0) AS pagos_transferencia" + PagosDelPeriodo;

        RecaudacionPeriodo resultado = new RecaudacionPeriodo();

        using SqlCommand comando = new SqlCommand(query, conexion, transaccion);
        CargarParametros(comando, anio, mes, idCategoria);

        using SqlDataReader reader = comando.ExecuteReader();
        if (!reader.Read())
        {
            return resultado;
        }

        resultado.Total = Convert.ToDecimal(reader["total"]);
        resultado.Efectivo = Convert.ToDecimal(reader["efectivo"]);
        resultado.Transferencia = Convert.ToDecimal(reader["transferencia"]);
        resultado.Pagos = Convert.ToInt32(reader["pagos"]);
        resultado.PagosEfectivo = Convert.ToInt32(reader["pagos_efectivo"]);
        resultado.PagosTransferencia = Convert.ToInt32(reader["pagos_transferencia"]);
        return resultado;
    }

    internal static List<PagoDelPeriodo> ObtenerDetalleCore(
        SqlConnection conexion, SqlTransaction? transaccion, int anio, int mes, int? idCategoria)
    {
        string query = ColumnasDelDetalle + PagosDelPeriodo + @"
            ORDER BY " + Orden(OrdenPagosDelPeriodo.Fecha, descendente: true);

        using SqlCommand comando = new SqlCommand(query, conexion, transaccion);
        CargarParametros(comando, anio, mes, idCategoria);
        return LeerDetalle(comando);
    }

    // HU-107: dos resultados en un solo viaje, igual que "Pagos registrados" (HU-088): cuántos cobros
    // tiene el período y la página pedida. El WHERE va antes del ORDER BY y el OFFSET/FETCH después,
    // así que se pagina sobre el período ya filtrado y no sobre la tabla entera.
    internal static PagosDelPeriodoPagina ObtenerPagosDelPeriodoCore(
        SqlConnection conexion, SqlTransaction? transaccion, FiltroPagosDelPeriodo filtro)
    {
        if (filtro.Pagina < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(filtro), "La página empieza en 1.");
        }
        if (filtro.TamanioPagina < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(filtro), "El tamaño de página tiene que ser mayor a cero.");
        }

        string query = "SELECT COUNT(*) AS total" + PagosDelPeriodo + ";" + ColumnasDelDetalle + PagosDelPeriodo + @"
            ORDER BY " + Orden(filtro.OrdenarPor, filtro.Descendente) + @"
            OFFSET @saltar ROWS FETCH NEXT @tomar ROWS ONLY;";

        var resultado = new PagosDelPeriodoPagina { Pagina = filtro.Pagina, TamanioPagina = filtro.TamanioPagina };

        using SqlCommand comando = new SqlCommand(query, conexion, transaccion);
        CargarParametros(comando, filtro.Anio, filtro.Mes, filtro.IdCategoria);
        comando.Parameters.Add("@saltar", SqlDbType.Int).Value = checked((filtro.Pagina - 1) * filtro.TamanioPagina);
        comando.Parameters.Add("@tomar", SqlDbType.Int).Value = filtro.TamanioPagina;

        using SqlDataReader reader = comando.ExecuteReader();
        if (reader.Read())
        {
            resultado.Total = Convert.ToInt32(reader["total"]);
        }

        reader.NextResult();
        resultado.Items = LeerFilas(reader);
        return resultado;
    }

    // El ORDER BY sale de esta lista cerrada (enum), nunca de un texto del cliente. Siempre termina en
    // PK_id_pago: dos cobros con la misma fecha o el mismo monto quedan siempre en el mismo orden y no
    // saltan de una página a otra entre consultas.
    // La fecha visible y el orden usan el timestamp real de registro. Solo los históricos sin timestamp
    // usan fecha_pago como respaldo; el filtro de período conserva el criterio vigente de HU-070.
    internal static string Orden(OrdenPagosDelPeriodo campo, bool descendente)
    {
        string direccion = descendente ? "DESC" : "ASC";
        return campo switch
        {
            OrdenPagosDelPeriodo.Fecha => $"COALESCE(p.fecha_hora_registro, CAST(p.fecha_pago AS datetime2)) {direccion}, p.PK_id_pago {direccion}",
            OrdenPagosDelPeriodo.Monto => $"p.monto_final {direccion}, p.PK_id_pago {direccion}",
            _ => throw new ArgumentOutOfRangeException(nameof(campo), campo, "Orden no soportado."),
        };
    }

    private static List<PagoDelPeriodo> LeerDetalle(SqlCommand comando)
    {
        using SqlDataReader reader = comando.ExecuteReader();
        return LeerFilas(reader);
    }

    private static List<PagoDelPeriodo> LeerFilas(SqlDataReader reader)
    {
        List<PagoDelPeriodo> filas = new List<PagoDelPeriodo>();
        while (reader.Read())
        {
            filas.Add(new PagoDelPeriodo
            {
                IdPago = Convert.ToInt64(reader["PK_id_pago"]),
                Jugador = reader["jugador"].ToString() ?? "",
                Dni = reader["dni"].ToString() ?? "",
                Categoria = reader["categoria"].ToString() ?? "",
                Monto = Convert.ToDecimal(reader["monto"]),
                MedioPago = reader["medio_pago"].ToString() ?? "",
                FechaPago = Convert.ToDateTime(reader["fecha_pago"]),
                FechaHoraRegistro = reader["fecha_hora_registro"] == DBNull.Value
                    ? null
                    : Convert.ToDateTime(reader["fecha_hora_registro"]),
            });
        }
        return filas;
    }

    // El período va como rango [primer día del mes, primer día del mes siguiente) y no como YEAR()/MONTH() sobre la
    // columna: así SQL Server puede usar un índice sobre fecha_pago en vez de calcular la función fila por fila (HU-070).
    private static void CargarParametros(SqlCommand comando, int anio, int mes, int? idCategoria)
    {
        DateTime desde = new DateTime(anio, mes, 1);
        comando.Parameters.Add("@desde", SqlDbType.Date).Value = desde;
        comando.Parameters.Add("@hasta", SqlDbType.Date).Value = desde.AddMonths(1);
        comando.Parameters.Add("@idCategoria", SqlDbType.Int).Value = (object?)idCategoria ?? DBNull.Value;
    }
}
