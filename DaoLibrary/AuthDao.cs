using Microsoft.Data.SqlClient;
using EntityLibrary;

namespace DaoLibrary;

public class AuthDao
{
    private readonly string _cadenaConexion;

    public AuthDao(string cadenaConexion) => _cadenaConexion = cadenaConexion;

    public Usuario? ValidarLogin(string email, string contrasenia)
    {
        using var conexion = SqlConnectionFactory.Open(_cadenaConexion);
        const string query = "SELECT PK_id_usuario, email, nombre, apellido, dni, contrasenia, password_hash, FK_id_rol, token_version FROM USUARIO WHERE email = @email AND activo = 1";
        using var comando = new SqlCommand(query, conexion);
        comando.Parameters.Add("@email", System.Data.SqlDbType.NVarChar, 254).Value = email;
        using var reader = comando.ExecuteReader();
        if (!reader.Read()) return null;

        var hash = reader["password_hash"] as string;
        var legacy = reader["contrasenia"] as string;
        if (hash is not null ? !PasswordHasher.Verify(contrasenia, hash) : !string.Equals(contrasenia, legacy, StringComparison.Ordinal))
            return null;

        var usuario = new Usuario
        {
            IdUsuario = Convert.ToInt32(reader["PK_id_usuario"]),
            Email = reader["email"].ToString() ?? string.Empty,
            Nombre = reader["nombre"] as string ?? string.Empty,
            Apellido = reader["apellido"] as string ?? string.Empty,
            Dni = reader["dni"] as string ?? string.Empty,
            IdRol = reader["FK_id_rol"] != DBNull.Value ? Convert.ToInt32(reader["FK_id_rol"]) : 0,
            TokenVersion = Convert.ToInt32(reader["token_version"])
        };
        var necesitaMigracion = hash is null;
        reader.Close();
        if (necesitaMigracion) ActualizarHash(conexion, usuario.IdUsuario, contrasenia);
        return usuario;
    }

    public bool UsuarioActivoConRol(int idUsuario, int idRol, int tokenVersion)
    {
        using var conexion = SqlConnectionFactory.Open(_cadenaConexion);
        using var comando = new SqlCommand("SELECT 1 FROM USUARIO WHERE PK_id_usuario = @id AND FK_id_rol = @rol AND activo = 1 AND token_version = @version", conexion);
        comando.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = idUsuario;
        comando.Parameters.Add("@rol", System.Data.SqlDbType.Int).Value = idRol;
        comando.Parameters.Add("@version", System.Data.SqlDbType.Int).Value = tokenVersion;
        return comando.ExecuteScalar() is not null;
    }

    private static void ActualizarHash(SqlConnection conexion, int idUsuario, string contrasenia)
    {
        using var comando = new SqlCommand("UPDATE USUARIO SET password_hash = @hash, contrasenia = NULL WHERE PK_id_usuario = @id AND activo = 1 AND password_hash IS NULL", conexion);
        comando.Parameters.Add("@hash", System.Data.SqlDbType.NVarChar, 512).Value = PasswordHasher.Hash(contrasenia);
        comando.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = idUsuario;
        comando.ExecuteNonQuery();
    }

}
