namespace ApiGestion.Tests;

using DaoLibrary;
using Microsoft.Data.SqlClient;

// Habilitar y corregir candidatos con el DNI como usuario del login. Corre contra la base unificada (CACC_TEST_DB,
// ver PlayerDao.Integration.Tests.cs) dentro de una transacción que se revierte, así que no deja cuentas ni
// auditoría. Cada prueba crea sus propias filas de PERSONAS y USUARIOS. En la base unificada el DNI vive en PERSONAS
// (solo dígitos, 6 a 9, único): no pueden existir dos personas con el mismo DNI ni un DNI con puntos guardado.
[Collection(BaseDeDatosCollection.Nombre)]
public class UsuariosPortalDaoIntegrationTests
{
    private static (SqlConnection Connection, SqlTransaction Transaction) Abrir()
    {
        var connection = new SqlConnection(Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable));
        connection.Open();
        return (connection, connection.BeginTransaction());
    }

    // PERSONAS y USUARIOS son IDENTITY y tienen triggers de auditoría: el id nuevo se lee con OUTPUT ... INTO.
    private static int InsertarUsuario(SqlConnection connection, SqlTransaction transaction, string? dni, string email, bool conAcceso)
    {
        using var command = new SqlCommand(
            @"INSERT dbo.PERSONAS (nombre, apellido, dni) VALUES (N'Prueba', N'Dni', @dni);
              DECLARE @persona INT = SCOPE_IDENTITY();
              DECLARE @nuevo TABLE (id INT);
              INSERT dbo.USUARIOS (FK_id_persona, email, FK_id_rol, activo, acceso_portal, rol_portal)
              OUTPUT INSERTED.PK_id_usuario INTO @nuevo
              VALUES (@persona, @email, 2, 1, @acceso, CASE WHEN @acceso = 1 THEN 2 END);
              SELECT id FROM @nuevo;", connection, transaction);
        command.Parameters.AddWithValue("@dni", (object?)dni ?? DBNull.Value);
        command.Parameters.AddWithValue("@email", email);
        command.Parameters.AddWithValue("@acceso", conAcceso ? 1 : 0);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static string? DniGuardado(SqlConnection connection, SqlTransaction transaction, int id)
    {
        using var command = new SqlCommand(
            "SELECT p.dni FROM dbo.USUARIOS u JOIN dbo.PERSONAS p ON p.PK_id_persona = u.FK_id_persona WHERE u.PK_id_usuario = @id", connection, transaction);
        command.Parameters.AddWithValue("@id", id);
        return command.ExecuteScalar() as string;
    }

    [IntegracionFact]
    public void Habilitar_ConUnDniValidoDejaElDniIntacto()
    {
        var (connection, transaction) = Abrir();
        try
        {
            int id = InsertarUsuario(connection, transaction, "90111222", "dni-valido@cacc.test", conAcceso: false);

            var (resultado, email) = UsuariosPortalDao.HabilitarCore(connection, transaction, id, "dni-valido@cacc.test", 2);

            Assert.Equal(HabilitarResultado.Ok, resultado);
            Assert.Equal("dni-valido@cacc.test", email);
            Assert.Equal("90111222", DniGuardado(connection, transaction, id));
        }
        finally { transaction.Rollback(); connection.Dispose(); }
    }

    [IntegracionFact]
    public void Habilitar_SinDniNoHabilita()
    {
        var (connection, transaction) = Abrir();
        try
        {
            int sinDni = InsertarUsuario(connection, transaction, null, "sin-dni@cacc.test", conAcceso: false);

            Assert.Equal(HabilitarResultado.DniInvalido, UsuariosPortalDao.HabilitarCore(connection, transaction, sinDni, "sin-dni@cacc.test", 2).Resultado);
        }
        finally { transaction.Rollback(); connection.Dispose(); }
    }

    [IntegracionFact]
    public void LaBaseRechazaUnDniQueNoSeaSoloDigitos()
    {
        var (connection, transaction) = Abrir();
        try
        {
            var conLetras = Assert.Throws<SqlException>(() => InsertarUsuario(connection, transaction, "abc", "letras@cacc.test", conAcceso: false));
            Assert.Equal(547, conLetras.Number); // CK_PERSONAS_dni_digitos
        }
        finally { transaction.Rollback(); connection.Dispose(); }
    }

    [IntegracionFact]
    public void EditarCandidato_CorrigeElDniYLoNormaliza()
    {
        var (connection, transaction) = Abrir();
        try
        {
            int id = InsertarUsuario(connection, transaction, "90777888", "corregir@cacc.test", conAcceso: false);

            var resultado = UsuariosPortalDao.EditarDatosCandidatoCore(connection, transaction, id, "Nombre", "Apellido", "90.999.000");

            Assert.Equal(EditarCandidatoResultado.Ok, resultado);
            Assert.Equal("90999000", DniGuardado(connection, transaction, id));
        }
        finally { transaction.Rollback(); connection.Dispose(); }
    }

    [IntegracionFact]
    public void EditarCandidato_RechazaUnDniInvalidoOEnUso()
    {
        var (connection, transaction) = Abrir();
        try
        {
            InsertarUsuario(connection, transaction, "90121212", "habilitada@cacc.test", conAcceso: true);
            int id = InsertarUsuario(connection, transaction, "90343434", "candidata@cacc.test", conAcceso: false);

            Assert.Equal(EditarCandidatoResultado.DniInvalido,
                UsuariosPortalDao.EditarDatosCandidatoCore(connection, transaction, id, "N", "A", "12"));
            Assert.Equal(EditarCandidatoResultado.DniEnUso,
                UsuariosPortalDao.EditarDatosCandidatoCore(connection, transaction, id, "N", "A", "90121212"));
            Assert.Equal("90343434", DniGuardado(connection, transaction, id)); // no se tocó
        }
        finally { transaction.Rollback(); connection.Dispose(); }
    }

    [IntegracionFact]
    public void ElDniEsUnicoEnLaBase()
    {
        var (connection, transaction) = Abrir();
        try
        {
            InsertarUsuario(connection, transaction, "90565656", "una@cacc.test", conAcceso: true);

            var error = Assert.Throws<SqlException>(() =>
                InsertarUsuario(connection, transaction, "90565656", "dos@cacc.test", conAcceso: true));
            Assert.Equal(2601, error.Number);
        }
        finally { transaction.Rollback(); connection.Dispose(); }
    }

    // Reactivar: el SuperAdmin elige el rol (la pantalla propone el que tenía). Un rol inválido no reactiva.
    [IntegracionFact]
    public void Reactivar_UsaElRolElegido()
    {
        var (connection, transaction) = Abrir();
        try
        {
            int id = InsertarUsuario(connection, transaction, "90333444", "reactivar-rol@cacc.test", conAcceso: true);
            using (var deshabilitar = new SqlCommand(
                "UPDATE dbo.USUARIOS SET acceso_portal = 0, contrasenia = N'hash-de-prueba' WHERE PK_id_usuario = @id", connection, transaction))
            {
                deshabilitar.Parameters.AddWithValue("@id", id);
                deshabilitar.ExecuteNonQuery();
            }

            Assert.False(UsuariosPortalDao.ReactivarCore(connection, transaction, id, 3));
            Assert.True(UsuariosPortalDao.ReactivarCore(connection, transaction, id, 1));

            using var leer = new SqlCommand("SELECT acceso_portal, rol_portal FROM dbo.USUARIOS WHERE PK_id_usuario = @id", connection, transaction);
            leer.Parameters.AddWithValue("@id", id);
            using var r = leer.ExecuteReader();
            Assert.True(r.Read());
            Assert.True(r.GetBoolean(0));
            Assert.Equal(1, r.GetInt32(1));
        }
        finally { transaction.Rollback(); connection.Dispose(); }
    }
}
