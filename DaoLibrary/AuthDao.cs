using Microsoft.Data.SqlClient;
using EntityLibrary;

namespace DaoLibrary;

public class AuthDao
{
    private readonly string _cadenaConexion;

    public AuthDao(string cadenaConexion) => _cadenaConexion = cadenaConexion;

    // Expresión SQL de la columna dni sin puntos, espacios ni guiones, para compararla con el DNI ya normalizado
    // (IdentificadorCuenta.NormalizarDni). Los datos del otro equipo pueden venir con formato.
    internal const string DniNormalizadoSql = "REPLACE(REPLACE(REPLACE(dni, '.', ''), ' ', ''), '-', '')";

    // El usuario es el DNI; mientras dura la transición también se acepta el correo de la cuenta.
    public Usuario? ValidarLogin(string identificador, string contrasenia)
    {
        var buscado = IdentificadorCuenta.Interpretar(identificador);
        if (buscado is null) return null;

        using var conexion = SqlConnectionFactory.Open(_cadenaConexion);
        // acceso_portal es el gate real de ingreso: FK_id_rol es del otro equipo y no
        // autoriza nada acá. rol_portal (propio, 1 = SuperAdmin, 2 = Administrador) es
        // el único valor que se usa para el claim de rol del JWT. activacion_pendiente = 1
        // significa que UsuariosPortalDao.Habilitar ya dio el acceso pero la persona
        // todavía no completó el enlace de activación: sin esto, una contraseña vieja
        // que el otro equipo haya dejado cargada serviría para entrar antes de tiempo.
        // TOP (2): si dos cuentas respondieran al mismo identificador no se adivina cuál es; no se deja entrar a ninguna.
        string query = $@"SELECT TOP (2) PK_id_usuario, email, nombre, apellido, dni, contrasenia, password_hash, token_version, acceso_portal, rol_portal
            FROM USUARIO
            WHERE {(buscado.Value.EsCorreo ? "email = @valor" : DniNormalizadoSql + " = @valor")}
              AND activo = 1 AND acceso_portal = 1 AND activacion_pendiente = 0";
        using var comando = new SqlCommand(query, conexion);
        comando.Parameters.Add("@valor", System.Data.SqlDbType.NVarChar, 254).Value = buscado.Value.Valor;
        using var reader = comando.ExecuteReader();
        if (!reader.Read()) return null;

        var hash = reader["password_hash"] as string;
        var legacy = reader["contrasenia"] as string;

        var usuario = new Usuario
        {
            IdUsuario = Convert.ToInt32(reader["PK_id_usuario"]),
            Email = reader["email"].ToString() ?? string.Empty,
            Nombre = reader["nombre"] as string ?? string.Empty,
            Apellido = reader["apellido"] as string ?? string.Empty,
            Dni = reader["dni"] as string ?? string.Empty,
            AccesoPortal = Convert.ToBoolean(reader["acceso_portal"]),
            RolPortal = reader["rol_portal"] != DBNull.Value ? Convert.ToInt32(reader["rol_portal"]) : null,
            TokenVersion = Convert.ToInt32(reader["token_version"])
        };
        // La constraint CK_USUARIO_acceso_portal_requiere_rol garantiza que rol_portal
        // no sea NULL cuando acceso_portal = 1, que es lo único que llega hasta acá.
        usuario.IdRol = usuario.RolPortal!.Value;
        if (reader.Read()) return null; // identificador ambiguo

        if (hash is not null ? !PasswordHasher.Verify(contrasenia, hash) : !string.Equals(contrasenia, legacy, StringComparison.Ordinal))
            return null;

        var necesitaMigracion = hash is null;
        reader.Close();
        if (necesitaMigracion) ActualizarHash(conexion, usuario.IdUsuario, contrasenia);
        return usuario;
    }

    public bool UsuarioActivoConRol(int idUsuario, int idRol, int tokenVersion)
    {
        using var conexion = SqlConnectionFactory.Open(_cadenaConexion);
        using var comando = new SqlCommand("SELECT 1 FROM USUARIO WHERE PK_id_usuario = @id AND rol_portal = @rol AND acceso_portal = 1 AND activo = 1 AND token_version = @version", conexion);
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
