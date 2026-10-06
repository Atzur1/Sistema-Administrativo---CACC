namespace ApiGestion.Tests;

using DaoLibrary;
using Microsoft.Data.SqlClient;

// Habilitar y corregir candidatos con el DNI como usuario del login. Corre contra una base de desarrollo real
// (CACC_TEST_DB, ver PlayerDao.Integration.Tests.cs) dentro de una transacción que se revierte, así que no deja
// cuentas ni auditoría. Cada prueba crea sus propias filas de USUARIO.
public class UsuariosPortalDaoIntegrationTests
{
    private static (SqlConnection Connection, SqlTransaction Transaction) Abrir()
    {
        var connection = new SqlConnection(Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable));
        connection.Open();
        return (connection, connection.BeginTransaction());
    }

    // USUARIO no usa IDENTITY: el id sale de MAX+1, igual que en el resto del sistema.
    private static int InsertarUsuario(SqlConnection connection, SqlTransaction transaction, string? dni, string email, bool conAcceso)
    {
        using var command = new SqlCommand(
            @"DECLARE @id INT = (SELECT ISNULL(MAX(PK_id_usuario), 0) + 1 FROM dbo.USUARIO WITH (TABLOCKX));
              INSERT dbo.USUARIO (PK_id_usuario, nombre, apellido, dni, email, FK_id_rol, activo, acceso_portal, rol_portal)
              VALUES (@id, N'Prueba', N'Dni', @dni, @email, 2, 1, @acceso, CASE WHEN @acceso = 1 THEN 2 END);
              SELECT @id;", connection, transaction);
        command.Parameters.AddWithValue("@dni", (object?)dni ?? DBNull.Value);
        command.Parameters.AddWithValue("@email", email);
        command.Parameters.AddWithValue("@acceso", conAcceso ? 1 : 0);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static string? DniGuardado(SqlConnection connection, SqlTransaction transaction, int id)
    {
        using var command = new SqlCommand("SELECT dni FROM dbo.USUARIO WHERE PK_id_usuario = @id", connection, transaction);
        command.Parameters.AddWithValue("@id", id);
        return command.ExecuteScalar() as string;
    }

    [IntegracionFact]
    public void Habilitar_GuardaElDniSoloConDigitos()
    {
        var (connection, transaction) = Abrir();
        try
        {
            int id = InsertarUsuario(connection, transaction, "90.111.222", "dni-con-puntos@cacc.test", conAcceso: false);

            var (resultado, email) = UsuariosPortalDao.HabilitarCore(connection, transaction, id, "dni-con-puntos@cacc.test", 2);

            Assert.Equal(HabilitarResultado.Ok, resultado);
            Assert.Equal("dni-con-puntos@cacc.test", email);
            Assert.Equal("90111222", DniGuardado(connection, transaction, id));
        }
        finally { transaction.Rollback(); connection.Dispose(); }
    }

    [IntegracionFact]
    public void Habilitar_SinDniValidoNoHabilita()
    {
        var (connection, transaction) = Abrir();
        try
        {
            int sinDni = InsertarUsuario(connection, transaction, null, "sin-dni@cacc.test", conAcceso: false);
            int dniInvalido = InsertarUsuario(connection, transaction, "abc", "dni-invalido@cacc.test", conAcceso: false);

            Assert.Equal(HabilitarResultado.DniInvalido, UsuariosPortalDao.HabilitarCore(connection, transaction, sinDni, "sin-dni@cacc.test", 2).Resultado);
            Assert.Equal(HabilitarResultado.DniInvalido, UsuariosPortalDao.HabilitarCore(connection, transaction, dniInvalido, "dni-invalido@cacc.test", 2).Resultado);
        }
        finally { transaction.Rollback(); connection.Dispose(); }
    }

    [IntegracionFact]
    public void Habilitar_NoPermiteElDniDeOtraCuentaConAcceso()
    {
        var (connection, transaction) = Abrir();
        try
        {
            InsertarUsuario(connection, transaction, "90333444", "primera@cacc.test", conAcceso: true);
            int segunda = InsertarUsuario(connection, transaction, "90.333.444", "segunda@cacc.test", conAcceso: false);

            var (resultado, _) = UsuariosPortalDao.HabilitarCore(connection, transaction, segunda, "segunda@cacc.test", 2);

            Assert.Equal(HabilitarResultado.DniEnUso, resultado); // aunque venga con puntos
        }
        finally { transaction.Rollback(); connection.Dispose(); }
    }

    [IntegracionFact]
    public void Habilitar_ElDniDeUnaCuentaSinAccesoNoMolesta()
    {
        var (connection, transaction) = Abrir();
        try
        {
            InsertarUsuario(connection, transaction, "90555666", "sin-acceso@cacc.test", conAcceso: false);
            int otra = InsertarUsuario(connection, transaction, "90555666", "otra@cacc.test", conAcceso: false);

            Assert.Equal(HabilitarResultado.Ok, UsuariosPortalDao.HabilitarCore(connection, transaction, otra, "otra@cacc.test", 2).Resultado);
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

            var resultado = UsuariosPortalDao.EditarDatosCandidatoCore(connection, transaction, id, "Nombre", "Apellido", "90.999.000", "corregir@cacc.test");

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
                UsuariosPortalDao.EditarDatosCandidatoCore(connection, transaction, id, "N", "A", "12", "candidata@cacc.test"));
            Assert.Equal(EditarCandidatoResultado.DniEnUso,
                UsuariosPortalDao.EditarDatosCandidatoCore(connection, transaction, id, "N", "A", "90121212", "candidata@cacc.test"));
            Assert.Equal("90343434", DniGuardado(connection, transaction, id)); // no se tocó
        }
        finally { transaction.Rollback(); connection.Dispose(); }
    }

    [IntegracionFact]
    public void ElIndiceUnicoRechazaDosCuentasConAccesoConElMismoDni()
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
}
