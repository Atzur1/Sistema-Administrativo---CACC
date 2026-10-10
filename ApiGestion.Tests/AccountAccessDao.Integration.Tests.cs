namespace ApiGestion.Tests;

using DaoLibrary;
using Microsoft.Data.SqlClient;

// Recuperación de contraseña para Administradores (rol 2), no solo para el SuperAdmin. El SQL corre contra una base
// de desarrollo real (CACC_TEST_DB, ver PlayerDao.Integration.Tests.cs) dentro de una transacción que se revierte,
// así que no deja tokens, contraseñas ni auditoría. Usa una cuenta Administrador que ya exista en esa base.
[Collection(BaseDeDatosCollection.Nombre)]
public class AccountAccessDaoIntegrationTests
{
    private static readonly DateTime Vence = DateTime.UtcNow.AddMinutes(30);

    private static string NuevoHash() => Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"); // 64 caracteres, como SHA-256 en hexa

    // Abre la conexión y la transacción de la prueba. El llamador siempre la revierte.
    private static (SqlConnection Connection, SqlTransaction Transaction) Abrir()
    {
        var connection = new SqlConnection(Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable));
        connection.Open();
        return (connection, connection.BeginTransaction());
    }

    private static (int Id, string Email) AdministradorHabilitado(SqlConnection connection, SqlTransaction transaction)
    {
        using var command = new SqlCommand(
            @"SELECT TOP (1) PK_id_usuario, email FROM dbo.USUARIO
              WHERE rol_portal = 2 AND acceso_portal = 1 AND activo = 1 AND activacion_pendiente = 0
              ORDER BY PK_id_usuario", connection, transaction);
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read(), "La base de prueba necesita una cuenta Administrador (rol 2) habilitada y con la contraseña ya creada.");
        return (reader.GetInt32(0), reader.GetString(1).Trim());
    }

    private static int Contar(SqlConnection connection, SqlTransaction transaction, string query, int id)
    {
        using var command = new SqlCommand(query, connection, transaction);
        command.Parameters.AddWithValue("@id", id);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void Ejecutar(SqlConnection connection, SqlTransaction transaction, string query, int id)
    {
        using var command = new SqlCommand(query, connection, transaction);
        command.Parameters.AddWithValue("@id", id);
        command.ExecuteNonQuery();
    }

    [IntegracionFact]
    public void ForgotPassword_AnAdministratorGetsARecoveryToken()
    {
        var (connection, transaction) = Abrir();
        try
        {
            var (id, email) = AdministradorHabilitado(connection, transaction);

            DestinatarioCorreo? resultado = AccountAccessDao.CreateResetTokenCore(connection, transaction, email, NuevoHash(), Vence);

            Assert.Equal(email, resultado?.Email);
            Assert.Equal(1, Contar(connection, transaction,
                "SELECT COUNT(*) FROM dbo.TOKEN_ACCESO_CUENTA WHERE id_usuario = @id AND tipo = 'RECUPERACION' AND consumido_utc IS NULL", id));
        }
        finally { transaction.Rollback(); connection.Dispose(); }
    }

    private static string DniDe(SqlConnection connection, SqlTransaction transaction, int id)
    {
        using var command = new SqlCommand("SELECT dni FROM dbo.USUARIO WHERE PK_id_usuario = @id", connection, transaction);
        command.Parameters.AddWithValue("@id", id);
        var dni = command.ExecuteScalar() as string;
        Assert.False(string.IsNullOrWhiteSpace(dni), "La cuenta Administrador de la base de prueba necesita tener DNI cargado.");
        return dni!;
    }

    [IntegracionFact]
    public void ForgotPassword_TheDniIdentifiesTheAccountAndTheLinkGoesToItsOwnEmail()
    {
        var (connection, transaction) = Abrir();
        try
        {
            var (id, email) = AdministradorHabilitado(connection, transaction);
            string dni = DniDe(connection, transaction, id);

            // Con puntos o sin ellos es el mismo DNI; el enlace va al correo de la cuenta, no a lo que se escribió.
            string conFormato = dni[..^3] + "." + dni[^3..];
            Assert.Equal(email, AccountAccessDao.CreateResetTokenCore(connection, transaction, dni, NuevoHash(), Vence)?.Email);
            Assert.Equal(email, AccountAccessDao.CreateResetTokenCore(connection, transaction, conFormato, NuevoHash(), Vence)?.Email);
        }
        finally { transaction.Rollback(); connection.Dispose(); }
    }

    [IntegracionFact]
    public void ForgotPassword_AnUnknownDniGetsNothing()
    {
        var (connection, transaction) = Abrir();
        try
        {
            Assert.Null(AccountAccessDao.CreateResetTokenCore(connection, transaction, "99999999", NuevoHash(), Vence));
            Assert.Null(AccountAccessDao.CreateResetTokenCore(connection, transaction, "no es un dni", NuevoHash(), Vence));
        }
        finally { transaction.Rollback(); connection.Dispose(); }
    }

    [IntegracionFact]
    public void ForgotPassword_AnUnknownEmailGetsNothing()
    {
        var (connection, transaction) = Abrir();
        try
        {
            DestinatarioCorreo? resultado = AccountAccessDao.CreateResetTokenCore(connection, transaction, "no-existe@cacc.invalid", NuevoHash(), Vence);

            Assert.Null(resultado);
        }
        finally { transaction.Rollback(); connection.Dispose(); }
    }

    [IntegracionFact]
    public void AnotherRecoveryRequest_LeavesOnlyTheLatestLinkValid()
    {
        var (connection, transaction) = Abrir();
        try
        {
            var (id, email) = AdministradorHabilitado(connection, transaction);

            AccountAccessDao.CreateResetTokenCore(connection, transaction, email, NuevoHash(), Vence);
            AccountAccessDao.CreateResetTokenCore(connection, transaction, email, NuevoHash(), Vence);

            Assert.Equal(1, Contar(connection, transaction,
                "SELECT COUNT(*) FROM dbo.TOKEN_ACCESO_CUENTA WHERE id_usuario = @id AND tipo = 'RECUPERACION' AND consumido_utc IS NULL", id));
        }
        finally { transaction.Rollback(); connection.Dispose(); }
    }

    [IntegracionFact]
    public void TheRecoveryLink_LetsAnAdministratorChooseANewPasswordOnlyOnce()
    {
        var (connection, transaction) = Abrir();
        try
        {
            var (id, email) = AdministradorHabilitado(connection, transaction);
            string hash = NuevoHash();
            AccountAccessDao.CreateResetTokenCore(connection, transaction, email, hash, Vence);

            Assert.True(AccountAccessDao.CompleteAccountAccessCore(connection, transaction, hash, "hash-de-prueba"));
            Assert.Equal(1, Contar(connection, transaction,
                "SELECT COUNT(*) FROM dbo.USUARIO WHERE PK_id_usuario = @id AND password_hash = 'hash-de-prueba'", id));

            // De un solo uso: el mismo enlace no sirve una segunda vez.
            Assert.False(AccountAccessDao.CompleteAccountAccessCore(connection, transaction, hash, "otro-hash"));
        }
        finally { transaction.Rollback(); connection.Dispose(); }
    }

    [IntegracionFact]
    public void AnExpiredLink_IsRefused()
    {
        var (connection, transaction) = Abrir();
        try
        {
            var (_, email) = AdministradorHabilitado(connection, transaction);
            string hash = NuevoHash();
            AccountAccessDao.CreateResetTokenCore(connection, transaction, email, hash, DateTime.UtcNow.AddMinutes(-1));

            Assert.False(AccountAccessDao.CompleteAccountAccessCore(connection, transaction, hash, "hash-de-prueba"));
        }
        finally { transaction.Rollback(); connection.Dispose(); }
    }

    [IntegracionFact]
    public void SuperAdminButton_SendsTheLinkToTheAccountsOwnEmail()
    {
        var (connection, transaction) = Abrir();
        try
        {
            var (id, email) = AdministradorHabilitado(connection, transaction);

            DestinatarioCorreo? destino = AccountAccessDao.CreateResetTokenForUserCore(connection, transaction, id, NuevoHash(), Vence);

            Assert.Equal(email, destino?.Email); // al correo de la propia cuenta, nunca al de quien lo pide
        }
        finally { transaction.Rollback(); connection.Dispose(); }
    }

    // HU-068 (devolución de QA): la auditoría tiene que registrar como autor al SuperAdmin que pidió el enlace, no a
    // la cuenta afectada. El autor llega a SQL Server por SqlAuditActorContext (lo carga AuditActorMiddleware con el
    // idUsuario del token) y SqlConnectionFactory.Open lo deja en SESSION_CONTEXT; el trigger de TOKEN_ACCESO_CUENTA
    // solo cae en la cuenta afectada cuando la conexión no trae autor. Por eso esta prueba abre la conexión con
    // SqlConnectionFactory, igual que el endpoint, y no con Abrir().
    [IntegracionFact]
    public void SuperAdminButton_RecordsTheSuperAdminWhoAskedAsTheAuthor()
    {
        const int superAdminQueLoPide = 987654321; // distinto de cualquier cuenta real, para no confundirlo con la afectada
        using var actor = SqlAuditActorContext.Push(superAdminQueLoPide);
        using var connection = SqlConnectionFactory.Open(Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable)!);
        using var transaction = connection.BeginTransaction();
        try
        {
            var (id, _) = CuentaConContrasena(connection, transaction);

            AccountAccessDao.CreateResetTokenForUserCore(connection, transaction, id, NuevoHash(), Vence);

            using var command = new SqlCommand(
                @"SELECT TOP (1) a.id_usuario FROM dbo.AUDITORIA_CAMBIOS a
                  WHERE a.entidad = N'TOKEN_ACCESO_CUENTA' AND a.accion = 'INSERT'
                    AND JSON_VALUE(a.datos_despues, '$.idUsuario') = CONVERT(NVARCHAR(20), @id)
                  ORDER BY a.PK_id_evento DESC", connection, transaction);
            command.Parameters.AddWithValue("@id", id);
            Assert.Equal(superAdminQueLoPide, Convert.ToInt32(command.ExecuteScalar()));
        }
        finally { transaction.Rollback(); }
    }

    // Cualquier cuenta con acceso y la contraseña ya creada (SuperAdmin o Administrador): la prueba de autoría no
    // depende del rol de la cuenta afectada, y así corre también en una base que solo tiene al SuperAdmin.
    private static (int Id, string Email) CuentaConContrasena(SqlConnection connection, SqlTransaction transaction)
    {
        using var command = new SqlCommand(
            @"SELECT TOP (1) PK_id_usuario, email FROM dbo.USUARIO
              WHERE acceso_portal = 1 AND activo = 1 AND activacion_pendiente = 0 AND email IS NOT NULL
              ORDER BY PK_id_usuario", connection, transaction);
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read(), "La base de prueba necesita una cuenta con acceso al portal y la contraseña ya creada.");
        return (reader.GetInt32(0), reader.GetString(1).Trim());
    }

    [IntegracionFact]
    public void SuperAdminButton_DoesNotApplyToAnAccountThatNeverCreatedItsPassword()
    {
        var (connection, transaction) = Abrir();
        try
        {
            var (id, _) = AdministradorHabilitado(connection, transaction);
            Ejecutar(connection, transaction, "UPDATE dbo.USUARIO SET activacion_pendiente = 1 WHERE PK_id_usuario = @id", id);

            // Esa cuenta recibe el correo de "Crea tu contraseña", no el de reestablecer.
            Assert.Null(AccountAccessDao.CreateResetTokenForUserCore(connection, transaction, id, NuevoHash(), Vence));
        }
        finally { transaction.Rollback(); connection.Dispose(); }
    }

    [IntegracionFact]
    public void SuperAdminButton_DoesNotApplyToAnInactiveAccount()
    {
        var (connection, transaction) = Abrir();
        try
        {
            var (id, _) = AdministradorHabilitado(connection, transaction);
            Ejecutar(connection, transaction, "UPDATE dbo.USUARIO SET activo = 0 WHERE PK_id_usuario = @id", id);

            Assert.Null(AccountAccessDao.CreateResetTokenForUserCore(connection, transaction, id, NuevoHash(), Vence));
        }
        finally { transaction.Rollback(); connection.Dispose(); }
    }
}
