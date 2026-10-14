namespace DaoLibrary;

using Microsoft.Data.SqlClient;

public class DatabasePermissionsDao
{
    private readonly string _connectionString;

    public DatabasePermissionsDao(string connectionString)
    {
        _connectionString = connectionString;
    }

    // True when the login the API uses could delete AUDITORIA_CAMBIOS without leaving a trace: sysadmin and
    // db_owner are not reached by a DENY, and an explicit DELETE permission overrides it.
    public bool CanDeleteAudit()
    {
        using SqlConnection connection = SqlConnectionFactory.Open(_connectionString);
        using var command = new SqlCommand(
            "SELECT IS_SRVROLEMEMBER('sysadmin'), IS_ROLEMEMBER('db_owner'), HAS_PERMS_BY_NAME('dbo.AUDITORIA_CAMBIOS', 'OBJECT', 'DELETE')",
            connection);
        using SqlDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return false;
        }
        return reader.GetValue(0) is 1 || reader.GetValue(1) is 1 || reader.GetValue(2) is 1;
    }
}
