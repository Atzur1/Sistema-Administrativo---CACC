using System.Data;
using Microsoft.Data.SqlClient;

namespace DaoLibrary;

public sealed class AccountAccessDao(string connectionString)
{
    public string? CreateResetToken(string email, string tokenHash, DateTime expiresUtc)
    {
        using var connection = SqlConnectionFactory.Open(connectionString);
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        int? userId;
        using (var find = new SqlCommand("SELECT PK_id_usuario FROM dbo.USUARIO WITH (UPDLOCK, HOLDLOCK) WHERE email = @email AND activo = 1 AND activacion_pendiente = 0 AND acceso_portal = 1 AND rol_portal = 1", connection, transaction))
        {
            find.Parameters.Add("@email", SqlDbType.NVarChar, 254).Value = email;
            var result = find.ExecuteScalar();
            userId = result is null ? null : Convert.ToInt32(result);
        }
        if (userId is null) { transaction.Commit(); return null; }

        using (var revoke = new SqlCommand("UPDATE dbo.TOKEN_ACCESO_CUENTA SET consumido_utc = SYSUTCDATETIME() WHERE id_usuario = @id AND tipo = 'RECUPERACION' AND consumido_utc IS NULL", connection, transaction))
        {
            revoke.Parameters.Add("@id", SqlDbType.Int).Value = userId.Value;
            revoke.ExecuteNonQuery();
        }
        using (var token = new SqlCommand(@"INSERT dbo.TOKEN_ACCESO_CUENTA(id_usuario, tipo, hash_token, vence_utc)
            VALUES (@id, 'RECUPERACION', @hash, @expires);", connection, transaction))
        {
            token.Parameters.Add("@id", SqlDbType.Int).Value = userId.Value;
            token.Parameters.Add("@hash", SqlDbType.Char, 64).Value = tokenHash;
            token.Parameters.Add("@expires", SqlDbType.DateTime2).Value = expiresUtc;
            token.ExecuteNonQuery();
        }
        transaction.Commit();
        return email;
    }

    public bool CompleteReset(string tokenHash, string passwordHash, string superAdminEmail)
    {
        using var connection = SqlConnectionFactory.Open(connectionString);
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        int? userId = null;
        using (var lookup = new SqlCommand(@"SELECT t.id_usuario FROM dbo.TOKEN_ACCESO_CUENTA t WITH (UPDLOCK, HOLDLOCK)
            INNER JOIN dbo.USUARIO u ON u.PK_id_usuario = t.id_usuario
            WHERE t.hash_token = @hash AND t.tipo = 'RECUPERACION' AND t.consumido_utc IS NULL
              AND t.vence_utc > SYSUTCDATETIME() AND u.email = @email AND u.acceso_portal = 1 AND u.rol_portal = 1
              AND u.activo = 1 AND u.activacion_pendiente = 0", connection, transaction))
        {
            lookup.Parameters.Add("@hash", SqlDbType.Char, 64).Value = tokenHash;
            lookup.Parameters.Add("@email", SqlDbType.NVarChar, 254).Value = superAdminEmail;
            var result = lookup.ExecuteScalar();
            if (result is not null) userId = Convert.ToInt32(result);
        }
        if (userId is null) { transaction.Commit(); return false; }

        using (SqlAuditActorContext.Push(userId))
        {
            using (var setActor = new SqlCommand("EXEC sys.sp_set_session_context @key=N'idUsuario', @value=@idUsuario;", connection, transaction))
            {
                setActor.Parameters.Add("@idUsuario", SqlDbType.Int).Value = userId.Value;
                setActor.ExecuteNonQuery();
            }
            using var update = new SqlCommand(@"UPDATE dbo.USUARIO SET password_hash = @password, contrasenia = NULL,
                token_version = token_version + 1
                WHERE PK_id_usuario = @id AND email = @email AND acceso_portal = 1 AND rol_portal = 1
                  AND activo = 1 AND activacion_pendiente = 0;", connection, transaction);
            update.Parameters.Add("@password", SqlDbType.NVarChar, 512).Value = passwordHash;
            update.Parameters.Add("@id", SqlDbType.Int).Value = userId.Value;
            update.Parameters.Add("@email", SqlDbType.NVarChar, 254).Value = superAdminEmail;
            if (update.ExecuteNonQuery() != 1) { transaction.Rollback(); return false; }
        }

        using (var consume = new SqlCommand("UPDATE dbo.TOKEN_ACCESO_CUENTA SET consumido_utc = SYSUTCDATETIME() WHERE hash_token = @hash AND consumido_utc IS NULL", connection, transaction))
        {
            consume.Parameters.Add("@hash", SqlDbType.Char, 64).Value = tokenHash;
            consume.ExecuteNonQuery();
        }
        transaction.Commit();
        return true;
    }
}
