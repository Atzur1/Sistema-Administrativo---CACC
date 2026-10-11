using Microsoft.Data.SqlClient;
using EntityLibrary;

namespace DaoLibrary;

public interface IRecaudacionDao
{
    RecaudacionPeriodo ObtenerDelPeriodo(int anio, int mes, int? idCategoria);
}

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
public class RecaudacionDao : IRecaudacionDao
{
    private readonly string _cadenaConexion;

    public RecaudacionDao(string cadenaConexion)
    {
        _cadenaConexion = cadenaConexion;
    }

    public RecaudacionPeriodo ObtenerDelPeriodo(int anio, int mes, int? idCategoria)
    {
        RecaudacionPeriodo resultado = new RecaudacionPeriodo();

        using SqlConnection conexion = new SqlConnection(_cadenaConexion);
        conexion.Open();

        ObtenerTotales(conexion, anio, mes, idCategoria, resultado);
        resultado.Detalle = ObtenerDetalle(conexion, anio, mes, idCategoria);

        return resultado;
    }

    // Una sola pasada por la tabla: SUM y COUNT condicionales en vez de un
    // GROUP BY + tres consultas. Los subtotales salen de las mismas filas que
    // el total, así que efectivo + transferencia = total siempre.
    private static void ObtenerTotales(
        SqlConnection conexion, int anio, int mes, int? idCategoria, RecaudacionPeriodo destino)
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
                ISNULL(SUM(CASE WHEN p.metodo_pago = 'Transferencia' THEN 1 ELSE 0 END), 0) AS pagos_transferencia
            FROM PAGOS p
            JOIN JUGADORES j ON j.PK_id_jugador = p.FK_id_jugador
            WHERE p.estado = 1
              AND YEAR(p.fecha_pago) = @anio
              AND MONTH(p.fecha_pago) = @mes
              AND (@idCategoria IS NULL OR j.FK_id_categoria = @idCategoria)";

        using SqlCommand comando = new SqlCommand(query, conexion);
        CargarParametros(comando, anio, mes, idCategoria);

        using SqlDataReader reader = comando.ExecuteReader();
        if (!reader.Read())
        {
            return;
        }

        destino.Total = Convert.ToDecimal(reader["total"]);
        destino.Efectivo = Convert.ToDecimal(reader["efectivo"]);
        destino.Transferencia = Convert.ToDecimal(reader["transferencia"]);
        destino.Pagos = Convert.ToInt32(reader["pagos"]);
        destino.PagosEfectivo = Convert.ToInt32(reader["pagos_efectivo"]);
        destino.PagosTransferencia = Convert.ToInt32(reader["pagos_transferencia"]);
    }

    private static List<PagoDelPeriodo> ObtenerDetalle(
        SqlConnection conexion, int anio, int mes, int? idCategoria)
    {
        List<PagoDelPeriodo> filas = new List<PagoDelPeriodo>();

        const string query = @"
            SELECT
                p.PK_id_pago,
                pe.apellido + ', ' + pe.nombre AS jugador,
                pe.Dni AS dni,
                ISNULL(c.nombre_categoria, '') AS categoria,
                p.monto_final AS monto,
                ISNULL(p.metodo_pago, '') AS medio_pago
            FROM PAGOS p
            JOIN JUGADORES j ON j.PK_id_jugador = p.FK_id_jugador
            JOIN PERSONA pe ON pe.PK_id_persona = j.FK_id_persona
            LEFT JOIN CATEGORIAS c ON c.PK_id_categoria = j.FK_id_categoria
            WHERE p.estado = 1
              AND YEAR(p.fecha_pago) = @anio
              AND MONTH(p.fecha_pago) = @mes
              AND (@idCategoria IS NULL OR j.FK_id_categoria = @idCategoria)
            ORDER BY p.fecha_pago DESC, p.PK_id_pago DESC";

        using SqlCommand comando = new SqlCommand(query, conexion);
        CargarParametros(comando, anio, mes, idCategoria);

        using SqlDataReader reader = comando.ExecuteReader();
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
            });
        }

        return filas;
    }

    private static void CargarParametros(SqlCommand comando, int anio, int mes, int? idCategoria)
    {
        comando.Parameters.AddWithValue("@anio", anio);
        comando.Parameters.AddWithValue("@mes", mes);
        comando.Parameters.AddWithValue("@idCategoria", (object?)idCategoria ?? DBNull.Value);
    }
}
