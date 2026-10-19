using System.Data;
using EntityLibrary;
using Microsoft.Data.SqlClient;

namespace DaoLibrary;

public interface IEstadoDeudaDao
{
    // Estado de deuda de los jugadores con esos DNI (ya normalizados: solo dígitos). categoriasPermitidas null = sin
    // restricción; una lista acota a los jugadores de esas categorías (la del jugador o cualquiera de las que integra).
    IReadOnlyList<EstadoDeudaJugador> ObtenerPorDni(IReadOnlyCollection<string> dnis, IReadOnlyCollection<int>? categoriasPermitidas);
}

// Fuente única del estado de deuda para el Portal Deportivo. Usa las mismas reglas que el resto del Administrativo
// (becas y descuentos de DescuentosSql, vencimiento del día 10 de SqlCalendarioCuotas), así el QR y los listados del
// Deportivo coinciden con Deudas y Morosidad. Reemplaza a la vista provisoria VISTA_ESTADO_DEUDA.
public class EstadoDeudaDao : IEstadoDeudaDao
{
    private readonly string _cadenaConexion;

    public EstadoDeudaDao(string cadenaConexion) => _cadenaConexion = cadenaConexion;

    public IReadOnlyList<EstadoDeudaJugador> ObtenerPorDni(IReadOnlyCollection<string> dnis, IReadOnlyCollection<int>? categoriasPermitidas)
    {
        if (dnis.Count == 0 || categoriasPermitidas is { Count: 0 }) return Array.Empty<EstadoDeudaJugador>();
        using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);
        return ObtenerPorDniCore(conexion, null, dnis, categoriasPermitidas);
    }

    internal static IReadOnlyList<EstadoDeudaJugador> ObtenerPorDniCore(
        SqlConnection conexion, SqlTransaction? transaccion, IReadOnlyCollection<string> dnis, IReadOnlyCollection<int>? categoriasPermitidas)
    {
        var parametros = new List<SqlParameter>();
        string listaDni = string.Join(", ", dnis.Select((dni, i) =>
        {
            parametros.Add(new SqlParameter($"@d{i}", SqlDbType.VarChar, 20) { Value = dni });
            return $"@d{i}";
        }));

        string filtroCategoria = "";
        if (categoriasPermitidas is not null)
        {
            string listaCategorias = string.Join(", ", categoriasPermitidas.Select((id, i) =>
            {
                parametros.Add(new SqlParameter($"@c{i}", SqlDbType.Int) { Value = id });
                return $"@c{i}";
            }));
            filtroCategoria = $@"AND (j.FK_id_categoria IN ({listaCategorias})
                       OR EXISTS (SELECT 1 FROM JUGADORES_CATEGORIAS jc
                                  WHERE jc.FK_id_jugador = j.PK_id_jugador AND jc.FK_id_categoria IN ({listaCategorias})))";
        }

        // Una fila por jugador, aunque no deba nada. Solo se leen las cuotas pendientes (estado = 0) de los jugadores pedidos.
        string query = $@"
            WITH seleccion AS (
                SELECT j.PK_id_jugador, p.dni, j.fecha_baja
                FROM JUGADORES j
                JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona
                WHERE p.dni IN ({listaDni}) {filtroCategoria}
            ),
            saldos AS (
                SELECT pg.FK_id_jugador, pg.concepto,
                       ({DescuentosSql.SaldoAjustadoClampleadoExpr}) AS saldo,
                       CASE WHEN {SqlCalendarioCuotas.EstaVencida("pg")} THEN 1 ELSE 0 END AS vencida
                FROM PAGOS pg
                {DescuentosSql.ApplyDescuentoActivo}
                WHERE pg.estado = 0 AND pg.FK_id_jugador IN (SELECT PK_id_jugador FROM seleccion)
            )
            SELECT s.PK_id_jugador, s.dni, s.fecha_baja,
                   ISNULL(SUM(x.saldo), 0) AS deuda_total,
                   COUNT(CASE WHEN x.saldo > 0 AND x.concepto = 'Cuota' AND x.vencida = 1 THEN 1 END) AS cuotas_vencidas
            FROM seleccion s
            LEFT JOIN saldos x ON x.FK_id_jugador = s.PK_id_jugador
            GROUP BY s.PK_id_jugador, s.dni, s.fecha_baja
            ORDER BY s.dni, s.PK_id_jugador";

        using SqlCommand comando = new SqlCommand(query, conexion, transaccion);
        comando.Parameters.AddRange(parametros.ToArray());
        var resultado = new List<EstadoDeudaJugador>();
        using SqlDataReader reader = comando.ExecuteReader();
        while (reader.Read())
        {
            resultado.Add(new EstadoDeudaJugador(
                Convert.ToInt32(reader["PK_id_jugador"]),
                (reader["dni"].ToString() ?? "").Trim(),
                Convert.ToInt32(reader["cuotas_vencidas"]),
                Convert.ToDecimal(reader["deuda_total"]),
                reader["fecha_baja"] == DBNull.Value ? null : Convert.ToDateTime(reader["fecha_baja"])));
        }
        return resultado;
    }
}
