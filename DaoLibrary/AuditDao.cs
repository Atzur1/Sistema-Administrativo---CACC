using System.Data;
using System.Text;
using EntityLibrary;
using Microsoft.Data.SqlClient;

namespace DaoLibrary;

public class AuditDao(string connectionString) : IAuditDao
{
    private const string CuotaSaldadaCondition =
        "(a.entidad = N'PAGOS' AND a.accion = 'DELETE' AND LOWER(JSON_VALUE(a.datos_antes, '$.estado')) = 'false')";

    public (IReadOnlyList<AuditEvent> Items, long Total) Search(
        DateTime? fromUtc,
        DateTime? toUtc,
        int? userId,
        string? userEmail,
        string? entity,
        string? action,
        int page,
        int pageSize)
    {
        var where = new StringBuilder(" WHERE 1 = 1");
        var parameters = new List<SqlParameter>();

        if (fromUtc.HasValue)
        {
            where.Append(" AND a.fecha_utc >= @desde");
            parameters.Add(new SqlParameter("@desde", SqlDbType.DateTime2) { Value = fromUtc.Value });
        }
        if (toUtc.HasValue)
        {
            where.Append(" AND a.fecha_utc < @hasta");
            parameters.Add(new SqlParameter("@hasta", SqlDbType.DateTime2) { Value = toUtc.Value });
        }
        if (userId.HasValue)
        {
            where.Append(" AND a.id_usuario = @usuario");
            parameters.Add(new SqlParameter("@usuario", SqlDbType.Int) { Value = userId.Value });
        }
        if (!string.IsNullOrWhiteSpace(userEmail))
        {
            where.Append(" AND (a.email_usuario LIKE @persona OR a.nombre_usuario LIKE @persona OR a.apellido_usuario LIKE @persona OR a.dni_usuario LIKE @persona)");
            parameters.Add(new SqlParameter("@persona", SqlDbType.NVarChar, 254) { Value = "%" + userEmail.Trim() + "%" });
        }
        if (!string.IsNullOrWhiteSpace(entity))
        {
            where.Append(" AND a.entidad = @entidad");
            parameters.Add(new SqlParameter("@entidad", SqlDbType.NVarChar, 128) { Value = entity.Trim() });
        }
        else
        {
            // Sin un filtro de sección explícito, se excluyen los eventos de plumbing interno:
            // TOKEN_ACCESO_CUENTA (cada intento de recuperar contraseña deja un INSERT + un UPDATE
            // que no aportan nada a "qué pasó en el club") y PERSONA (el trigger no guarda ningún
            // dato propio, solo la acción). Siguen existiendo y son buscables a propósito con
            // "Sección" — esto solo los saca de la vista por defecto, no los borra ni los oculta
            // de forma permanente.
            where.Append(" AND a.entidad NOT IN ('TOKEN_ACCESO_CUENTA', 'PERSONA')");
        }
        if (!string.IsNullOrWhiteSpace(action))
        {
            var normalizedAction = action.Trim().ToUpperInvariant();

            // "Cuota saldada" no es una acción real de SQL Server (INSERT/UPDATE/DELETE): es un
            // DELETE puntual sobre PAGOS cuando un abono cubre el saldo total de una cuota pendiente
            // (PagosService.EliminarPago tiene un único llamador en todo el backend, y es ese). Se
            // trata como su propia categoría, mutuamente excluyente de "Eliminado", para que filtrar
            // por Eliminado no traiga mezclada una cuota saldada con una eliminación real.
            if (normalizedAction == "CUOTA_SALDADA")
            {
                where.Append(" AND ").Append(CuotaSaldadaCondition);
            }
            else if (normalizedAction == "DELETE")
            {
                where.Append(" AND a.accion = 'DELETE' AND NOT ").Append(CuotaSaldadaCondition);
            }
            else
            {
                where.Append(" AND a.accion = @accion");
                parameters.Add(new SqlParameter("@accion", SqlDbType.VarChar, 10) { Value = normalizedAction });
            }
        }

        using var connection = SqlConnectionFactory.Open(connectionString);
        using var countCommand = new SqlCommand("SELECT COUNT_BIG(*) FROM dbo.AUDITORIA_CAMBIOS a" + where, connection);
        countCommand.Parameters.AddRange(parameters.Select(CloneParameter).ToArray());
        var total = Convert.ToInt64(countCommand.ExecuteScalar());

        const string query = @"
            SELECT a.PK_id_evento, a.id_usuario, a.email_usuario, a.nombre_usuario, a.apellido_usuario, a.dni_usuario,
                   a.entidad, a.id_entidad, a.accion, a.fecha_utc, a.datos_antes, a.datos_despues
            FROM dbo.AUDITORIA_CAMBIOS a";
        using var command = new SqlCommand(query + where + @"
            ORDER BY a.fecha_utc DESC, a.PK_id_evento DESC
            OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;", connection);
        command.Parameters.AddRange(parameters.ToArray());
        command.Parameters.Add(new SqlParameter("@offset", SqlDbType.Int) { Value = checked((page - 1) * pageSize) });
        command.Parameters.Add(new SqlParameter("@pageSize", SqlDbType.Int) { Value = pageSize });

        var items = new List<AuditEvent>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            items.Add(new AuditEvent
            {
                Id = reader.GetInt64(0),
                UserId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                UserEmail = reader.IsDBNull(2) ? null : reader.GetString(2),
                UserName = reader.IsDBNull(3) ? null : reader.GetString(3),
                UserSurname = reader.IsDBNull(4) ? null : reader.GetString(4),
                UserDni = reader.IsDBNull(5) ? null : reader.GetString(5),
                Entity = reader.GetString(6),
                EntityId = reader.GetString(7),
                Action = reader.GetString(8),
                TimestampUtc = reader.GetDateTime(9),
                BeforeJson = reader.IsDBNull(10) ? null : reader.GetString(10),
                AfterJson = reader.IsDBNull(11) ? null : reader.GetString(11)
            });
        }

        return (items, total);
    }

    private static SqlParameter CloneParameter(SqlParameter source) => new(source.ParameterName, source.SqlDbType)
    {
        Size = source.Size,
        Value = source.Value
    };
}
