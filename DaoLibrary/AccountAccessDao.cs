using System.Data;
using Microsoft.Data.SqlClient;

namespace DaoLibrary;

// Estado de acceso de una cuenta habilitada: a dónde mandar el correo y cuál de los dos enlaces
// corresponde (todavía no creó su contraseña = primer ingreso; ya la tiene = reestablecer).
public sealed record EstadoAcceso(string Email, bool ActivacionPendiente, string? Nombre = null);

// A quién va un correo de acceso: la dirección de la cuenta y su nombre, para saludarla en el correo
// (null si la cuenta no lo tiene cargado).
public sealed record DestinatarioCorreo(string Email, string? Nombre);

public sealed class AccountAccessDao(string connectionString)
{
    // ===== Recuperación de contraseña (olvidé mi contraseña) =====

    // Crea el token de recuperación para la cuenta con ese DNI (o, en la transición, ese correo). Sirve para
    // cualquier rol con acceso al portal (SuperAdmin y Administrador). Devuelve a quién hay que mandar el enlace
    // (el correo de la cuenta y su nombre) si existe y está en condiciones, o null — el llamador responde igual
    // en los dos casos.
    public DestinatarioCorreo? CreateResetToken(string identifier, string tokenHash, DateTime expiresUtc) =>
        EnTransaccion((connection, transaction) => CreateResetTokenCore(connection, transaction, identifier, tokenHash, expiresUtc));

    // Lo mismo, pero lo pide un SuperAdmin para una cuenta puntual (botón "Resetear contraseña" de Usuarios y
    // Permisos). El enlace va siempre al correo de la propia cuenta, nunca a quien lo pidió.
    public DestinatarioCorreo? CreateResetTokenForUser(int userId, string tokenHash, DateTime expiresUtc) =>
        EnTransaccion((connection, transaction) => CreateResetTokenForUserCore(connection, transaction, userId, tokenHash, expiresUtc));

    internal static DestinatarioCorreo? CreateResetTokenCore(SqlConnection connection, SqlTransaction transaction, string identifier, string tokenHash, DateTime expiresUtc)
    {
        var wanted = IdentificadorCuenta.Interpretar(identifier);
        if (wanted is null) return null;

        int userId;
        string? email;
        string? nombre;
        // TOP (2): si dos cuentas respondieran al mismo identificador no se adivina cuál es; no se manda nada.
        using (var find = new SqlCommand(
            $@"SELECT TOP (2) PK_id_usuario, email, nombre FROM dbo.USUARIO WITH (UPDLOCK, HOLDLOCK)
               WHERE {(wanted.Value.EsCorreo ? "email = @valor" : AuthDao.DniNormalizadoSql + " = @valor")}
                 AND activo = 1 AND activacion_pendiente = 0 AND acceso_portal = 1",
            connection, transaction))
        {
            find.Parameters.Add("@valor", SqlDbType.NVarChar, 254).Value = wanted.Value.Valor;
            using var reader = find.ExecuteReader();
            if (!reader.Read()) return null;
            userId = reader.GetInt32(0);
            email = reader.IsDBNull(1) ? null : reader.GetString(1).Trim();
            nombre = LeerNombre(reader, 2);
            if (reader.Read()) return null;
        }
        // El enlace va al correo que la cuenta tiene cargado, nunca a lo que escribió quien lo pidió.
        if (string.IsNullOrWhiteSpace(email)) return null;

        InsertResetToken(connection, transaction, userId, tokenHash, expiresUtc);
        return new DestinatarioCorreo(email, nombre);
    }

    internal static DestinatarioCorreo? CreateResetTokenForUserCore(SqlConnection connection, SqlTransaction transaction, int userId, string tokenHash, DateTime expiresUtc)
    {
        string? email;
        string? nombre;
        using (var find = new SqlCommand(
            "SELECT email, nombre FROM dbo.USUARIO WITH (UPDLOCK, HOLDLOCK) WHERE PK_id_usuario = @id AND activo = 1 AND activacion_pendiente = 0 AND acceso_portal = 1",
            connection, transaction))
        {
            find.Parameters.Add("@id", SqlDbType.Int).Value = userId;
            using var reader = find.ExecuteReader();
            if (!reader.Read()) return null;
            email = reader.IsDBNull(0) ? null : reader.GetString(0).Trim();
            nombre = LeerNombre(reader, 1);
        }
        if (string.IsNullOrWhiteSpace(email)) return null;

        InsertResetToken(connection, transaction, userId, tokenHash, expiresUtc);
        return new DestinatarioCorreo(email, nombre);
    }

    // El nombre de USUARIO para saludar en el correo; vacío cuenta como sin cargar.
    private static string? LeerNombre(SqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return null;
        var nombre = reader.GetString(ordinal).Trim();
        return nombre.Length == 0 ? null : nombre;
    }

    // Un solo enlace de recuperación vigente por cuenta: pedir otro deja sin efecto el anterior.
    private static void InsertResetToken(SqlConnection connection, SqlTransaction transaction, int userId, string tokenHash, DateTime expiresUtc)
    {
        using (var revoke = new SqlCommand("UPDATE dbo.TOKEN_ACCESO_CUENTA SET consumido_utc = SYSUTCDATETIME() WHERE id_usuario = @id AND tipo = 'RECUPERACION' AND consumido_utc IS NULL", connection, transaction))
        {
            revoke.Parameters.Add("@id", SqlDbType.Int).Value = userId;
            revoke.ExecuteNonQuery();
        }
        using var token = new SqlCommand(@"INSERT dbo.TOKEN_ACCESO_CUENTA(id_usuario, tipo, hash_token, vence_utc)
            VALUES (@id, 'RECUPERACION', @hash, @expires);", connection, transaction);
        token.Parameters.Add("@id", SqlDbType.Int).Value = userId;
        token.Parameters.Add("@hash", SqlDbType.Char, 64).Value = tokenHash;
        token.Parameters.Add("@expires", SqlDbType.DateTime2).Value = expiresUtc;
        token.ExecuteNonQuery();
    }

    // ===== Primer ingreso (activación) =====

    // Token de activación para una cuenta recién habilitada (acceso_portal=1,
    // activacion_pendiente=1) por UsuariosPortalDao.Habilitar. Sirve para cualquier rol.
    public void CreateActivationToken(int userId, string tokenHash, DateTime expiresUtc)
    {
        using var connection = SqlConnectionFactory.Open(connectionString);
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);

        using (var revoke = new SqlCommand("UPDATE dbo.TOKEN_ACCESO_CUENTA SET consumido_utc = SYSUTCDATETIME() WHERE id_usuario = @id AND tipo = 'ACTIVACION' AND consumido_utc IS NULL", connection, transaction))
        {
            revoke.Parameters.Add("@id", SqlDbType.Int).Value = userId;
            revoke.ExecuteNonQuery();
        }
        using (var token = new SqlCommand(@"INSERT dbo.TOKEN_ACCESO_CUENTA(id_usuario, tipo, hash_token, vence_utc)
            VALUES (@id, 'ACTIVACION', @hash, @expires);", connection, transaction))
        {
            token.Parameters.Add("@id", SqlDbType.Int).Value = userId;
            token.Parameters.Add("@hash", SqlDbType.Char, 64).Value = tokenHash;
            token.Parameters.Add("@expires", SqlDbType.DateTime2).Value = expiresUtc;
            token.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    // Correo y situación de una cuenta habilitada y activa, o null si no está en condiciones de recibir un
    // enlace (sin acceso al portal, o inactiva en la base del otro equipo).
    public EstadoAcceso? ObtenerEstadoAcceso(int userId)
    {
        using var connection = SqlConnectionFactory.Open(connectionString);
        using var command = new SqlCommand(
            "SELECT email, activacion_pendiente, nombre FROM dbo.USUARIO WHERE PK_id_usuario = @id AND activo = 1 AND acceso_portal = 1",
            connection);
        command.Parameters.Add("@id", SqlDbType.Int).Value = userId;
        using var reader = command.ExecuteReader();
        if (!reader.Read() || reader.IsDBNull(0)) return null;
        return new EstadoAcceso(reader.GetString(0).Trim(), Convert.ToBoolean(reader.GetValue(1)), LeerNombre(reader, 2));
    }

    // ===== Completar cualquiera de los dos flujos =====

    // Completa cualquiera de los dos flujos de token (RECUPERACION o ACTIVACION): el token en sí, de un
    // solo uso y con vencimiento, es la única prueba de identidad de este endpoint público — no hay más dato
    // del usuario para cruzar acá (la pantalla solo pide la contraseña nueva). RECUPERACION vale para
    // cualquier rol con acceso y la contraseña ya creada; ACTIVACION vale para cualquier rol recién
    // habilitado y además limpia activacion_pendiente.
    public bool CompleteAccountAccess(string tokenHash, string passwordHash)
    {
        using var connection = SqlConnectionFactory.Open(connectionString);
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);

        if (!CompleteAccountAccessCore(connection, transaction, tokenHash, passwordHash))
        {
            transaction.Rollback();
            return false;
        }

        transaction.Commit();
        return true;
    }

    internal static bool CompleteAccountAccessCore(SqlConnection connection, SqlTransaction transaction, string tokenHash, string passwordHash)
    {
        int? userId = null;
        using (var lookup = new SqlCommand(@"SELECT t.id_usuario FROM dbo.TOKEN_ACCESO_CUENTA t WITH (UPDLOCK, HOLDLOCK)
            INNER JOIN dbo.USUARIO u ON u.PK_id_usuario = t.id_usuario
            WHERE t.hash_token = @hash AND t.consumido_utc IS NULL AND t.vence_utc > SYSUTCDATETIME()
              AND u.activo = 1 AND u.acceso_portal = 1
              AND (t.tipo = 'ACTIVACION' OR (t.tipo = 'RECUPERACION' AND u.activacion_pendiente = 0))",
            connection, transaction))
        {
            lookup.Parameters.Add("@hash", SqlDbType.Char, 64).Value = tokenHash;
            var result = lookup.ExecuteScalar();
            if (result is not null) userId = Convert.ToInt32(result);
        }
        if (userId is null) return false;

        using (SqlAuditActorContext.Push(userId))
        {
            using (var setActor = new SqlCommand("EXEC sys.sp_set_session_context @key=N'idUsuario', @value=@idUsuario;", connection, transaction))
            {
                setActor.Parameters.Add("@idUsuario", SqlDbType.Int).Value = userId.Value;
                setActor.ExecuteNonQuery();
            }
            using var update = new SqlCommand(@"UPDATE dbo.USUARIO SET password_hash = @password, contrasenia = NULL,
                token_version = token_version + 1, activacion_pendiente = 0
                WHERE PK_id_usuario = @id AND activo = 1 AND acceso_portal = 1;", connection, transaction);
            update.Parameters.Add("@password", SqlDbType.NVarChar, 512).Value = passwordHash;
            update.Parameters.Add("@id", SqlDbType.Int).Value = userId.Value;
            if (update.ExecuteNonQuery() != 1) return false;
        }

        using var consume = new SqlCommand("UPDATE dbo.TOKEN_ACCESO_CUENTA SET consumido_utc = SYSUTCDATETIME() WHERE hash_token = @hash AND consumido_utc IS NULL", connection, transaction);
        consume.Parameters.Add("@hash", SqlDbType.Char, 64).Value = tokenHash;
        consume.ExecuteNonQuery();
        return true;
    }

    private T EnTransaccion<T>(Func<SqlConnection, SqlTransaction, T> operation)
    {
        using var connection = SqlConnectionFactory.Open(connectionString);
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        T result = operation(connection, transaction);
        transaction.Commit();
        return result;
    }
}
