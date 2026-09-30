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
            // Sin un filtro de sección explícito, la vista por defecto se acota a lo que el
            // club realmente quiere ver de un vistazo: pagos (incluye cuotas saldadas, que
            // viven como una acción dentro de PAGOS, no como su propia sección) y aranceles.
            // Todo el resto (Usuarios, Jugadores, Categorías, Becas, TOKEN_ACCESO_CUENTA,
            // PERSONA) sigue existiendo y es buscable a propósito con "Sección" — esto solo
            // lo saca de la vista por defecto, no lo borra ni lo oculta de forma permanente.
            where.Append(" AND a.entidad IN ('PAGOS', 'ARANCELES')");
        }
        if (!string.IsNullOrWhiteSpace(action))
        {
            var normalizedAction = action.Trim().ToUpperInvariant();

            // Estos tres son los únicos que el filtro "Tipo de cambio" ofrece (ver
            // AuditoriaController) — cada uno ya implica su sección, no son acciones
            // crudas de SQL Server sueltas.
            if (normalizedAction == "PAGO_REALIZADO")
            {
                // Un alta en PAGOS: se registró un pago.
                where.Append(" AND a.entidad = 'PAGOS' AND a.accion = 'INSERT'");
            }
            else if (normalizedAction == "ARANCEL_ACTUALIZADO")
            {
                where.Append(" AND a.entidad = 'ARANCELES'");
            }
            else if (normalizedAction == "DELETE")
            {
                // "Cuota saldada" no es una acción real de SQL Server: es un DELETE puntual
                // sobre PAGOS cuando un abono cubre el saldo total de una cuota pendiente
                // (PagosService.EliminarPago tiene un único llamador en todo el backend, y
                // es ese). Se excluye acá para que "Eliminado" no traiga mezclada una cuota
                // saldada con una eliminación real — sigue existiendo como etiqueta de fila
                // en el frontend, pero no es algo que el filtro ofrezca buscar aparte.
                where.Append(" AND a.accion = 'DELETE' AND NOT ").Append(CuotaSaldadaCondition);
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
