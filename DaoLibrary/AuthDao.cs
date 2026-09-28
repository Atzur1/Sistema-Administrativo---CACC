using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using EntityLibrary;

namespace DaoLibrary;

public class AuthDao
{
    private const int Iterations = 210_000;
    private readonly string _cadenaConexion;

    public AuthDao(string cadenaConexion) => _cadenaConexion = cadenaConexion;

    public Usuario? ValidarLogin(string email, string contrasenia)
    {
        using var conexion = new SqlConnection(_cadenaConexion);
        conexion.Open();
        const string query = "SELECT PK_id_usuario, email, contrasenia, password_hash, FK_id_rol FROM USUARIO WHERE email = @email AND activo = 1";
        using var comando = new SqlCommand(query, conexion);
        comando.Parameters.Add("@email", System.Data.SqlDbType.NVarChar, 254).Value = email;
        using var reader = comando.ExecuteReader();
        if (!reader.Read()) return null;

        var hash = reader["password_hash"] as string;
        var legacy = reader["contrasenia"] as string;
        if (hash is not null ? !VerifyPassword(contrasenia, hash) : !string.Equals(contrasenia, legacy, StringComparison.Ordinal))
            return null;

        var usuario = new Usuario
        {
            IdUsuario = Convert.ToInt32(reader["PK_id_usuario"]),
            Email = reader["email"].ToString() ?? string.Empty,
            IdRol = reader["FK_id_rol"] != DBNull.Value ? Convert.ToInt32(reader["FK_id_rol"]) : 0
        };
        var necesitaMigracion = hash is null;
        reader.Close();
        if (necesitaMigracion) ActualizarHash(conexion, usuario.IdUsuario, contrasenia);
        return usuario;
    }

    public bool UsuarioActivoConRol(int idUsuario, int idRol)
    {
        using var conexion = new SqlConnection(_cadenaConexion);
        conexion.Open();
        using var comando = new SqlCommand("SELECT 1 FROM USUARIO WHERE PK_id_usuario = @id AND FK_id_rol = @rol AND activo = 1", conexion);
        comando.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = idUsuario;
        comando.Parameters.Add("@rol", System.Data.SqlDbType.Int).Value = idRol;
        return comando.ExecuteScalar() is not null;
    }

    private static void ActualizarHash(SqlConnection conexion, int idUsuario, string contrasenia)
    {
        using var comando = new SqlCommand("UPDATE USUARIO SET password_hash = @hash, contrasenia = NULL WHERE PK_id_usuario = @id AND activo = 1 AND password_hash IS NULL", conexion);
        comando.Parameters.Add("@hash", System.Data.SqlDbType.NVarChar, 512).Value = HashPassword(contrasenia);
        comando.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = idUsuario;
        comando.ExecuteNonQuery();
    }

    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"PBKDF2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string encoded)
    {
        var parts = encoded.Split('$');
        if (parts.Length != 4 || parts[0] != "PBKDF2" || !int.TryParse(parts[1], out var rounds) || rounds < 100_000 || rounds > 1_000_000)
            return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, rounds, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }
}
