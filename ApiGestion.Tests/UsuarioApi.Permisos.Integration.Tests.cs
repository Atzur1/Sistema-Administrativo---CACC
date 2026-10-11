namespace ApiGestion.Tests;

using DaoLibrary;
using Microsoft.Data.SqlClient;

// database/produccion/03_permisos_usuario_api.sql, ejecutado de verdad contra SQL Server: el usuario técnico de la
// API (cacc_api) puede hacer todo lo que hace la API, pero no borrar ni modificar la auditoría, ni cambiar la
// estructura. Todo dentro de una transacción que se revierte: no deja el usuario creado.
[Collection(BaseDeDatosCollection.Nombre)]
public class UsuarioApiPermisosIntegrationTests
{
    private static string Conexion => Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable)!;

    private static string ScriptPermisos()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "database", "produccion")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "database", "produccion", "03_permisos_usuario_api.sql"));
    }

    private static void Ejecutar(SqlConnection c, SqlTransaction t, string sql)
    {
        using var cmd = new SqlCommand(sql, c, t);
        cmd.ExecuteNonQuery();
    }

    // Error 229 (permiso denegado) u otro error de permisos al intentar la operación como cacc_api.
    private static SqlException Rechaza(SqlConnection c, SqlTransaction t, string sql) =>
        Assert.Throws<SqlException>(() => Ejecutar(c, t, sql));

    [IntegracionFact]
    public void ElUsuarioDeLaApi_TrabajaNormalmentePeroNoPuedeTocarLaAuditoriaNiLaEstructura()
    {
        using var connection = new SqlConnection(Conexion);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            Ejecutar(connection, transaction, "IF USER_ID(N'cacc_api') IS NULL CREATE USER [cacc_api] WITHOUT LOGIN;");
            Ejecutar(connection, transaction, ScriptPermisos());

            Ejecutar(connection, transaction, "EXECUTE AS USER = N'cacc_api';");
            try
            {
                // Lo que hace la API: leer, usar la secuencia de ids, tomar el bloqueo de cobros y fijar el operador.
                Ejecutar(connection, transaction, "SELECT TOP (1) * FROM dbo.PAGOS; SELECT TOP (1) * FROM dbo.AUDITORIA_CAMBIOS;");
                Ejecutar(connection, transaction, "DECLARE @id INT = NEXT VALUE FOR dbo.SEQ_PAGOS;");
                Ejecutar(connection, transaction, "DECLARE @r INT; EXEC @r = sys.sp_getapplock @Resource = N'prueba', @LockMode = 'Exclusive', @LockOwner = 'Transaction';");
                Ejecutar(connection, transaction, "EXEC sys.sp_set_session_context @key = N'idUsuario', @value = NULL;");
                Ejecutar(connection, transaction, "UPDATE dbo.PAGOS SET monto_final = monto_final WHERE 1 = 0; DELETE FROM dbo.PAGOS WHERE 1 = 0;");

                // Lo que no puede hacer.
                Rechaza(connection, transaction, "DELETE FROM dbo.AUDITORIA_CAMBIOS WHERE 1 = 0;");
                Rechaza(connection, transaction, "UPDATE dbo.AUDITORIA_CAMBIOS SET accion = accion WHERE 1 = 0;");
                Rechaza(connection, transaction, "TRUNCATE TABLE dbo.AUDITORIA_CAMBIOS;");
                Rechaza(connection, transaction, "DELETE FROM dbo.PAGOS_ANULADOS WHERE 1 = 0;");
                Rechaza(connection, transaction, "ALTER TABLE dbo.PAGOS DISABLE TRIGGER ALL;");
                Rechaza(connection, transaction, "CREATE TABLE dbo.PruebaPermisos (id INT);");
                Rechaza(connection, transaction, "DELETE FROM dbo.__CaccMigraciones WHERE 1 = 0;");
            }
            finally
            {
                Ejecutar(connection, transaction, "REVERT;");
            }
        }
        finally { transaction.Rollback(); }
    }
}
