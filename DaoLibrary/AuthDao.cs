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
        string query = $@"SELECT TOP (2) PK_id_usuario, email, nombre, apellido, dni, password_hash, token_version, acceso_portal, rol_portal
            FROM USUARIO
            WHERE {(buscado.Value.EsCorreo ? "email = @valor" : DniNormalizadoSql + " = @valor")}
              AND activo = 1 AND acceso_portal = 1 AND activacion_pendiente = 0";
        using var comando = new SqlCommand(query, conexion);
        comando.Parameters.Add("@valor", System.Data.SqlDbType.NVarChar, 254).Value = buscado.Value.Valor;
        using var reader = comando.ExecuteReader();
        if (!reader.Read())
        {
            // Mismo costo que verificar una contraseña real: sin esto, "ese DNI no existe" respondía
            // notablemente más rápido que "contraseña incorrecta" y delataba qué DNI tienen cuenta.
            PasswordHasher.Verify(contrasenia, HashDeReferencia);
            return null;
        }

        var hash = reader["password_hash"] as string;

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

        // Solo contraseñas con hash (PBKDF2). La columna contrasenia en texto plano ya no se lee: una cuenta
        // sin password_hash no puede entrar hasta crear su contraseña con el enlace de activación o de
        // "¿Olvidaste tu contraseña?". Se gasta igual el tiempo de una verificación para no delatarla.
        if (hash is null)
        {
            PasswordHasher.Verify(contrasenia, HashDeReferencia);
            return null;
        }

        return PasswordHasher.Verify(contrasenia, hash) ? usuario : null;
    }

    // Hash válido de una contraseña al azar, solo para gastar el mismo tiempo que una verificación real.
    private static readonly string HashDeReferencia = PasswordHasher.Hash(Guid.NewGuid().ToString("N"));

    // Cierre de sesión en el servidor: subir token_version invalida todos los JWT emitidos hasta ahora
    // para esa cuenta (OnTokenValidated los compara), en vez de esperar a que venzan solos.
    public void InvalidarSesiones(int idUsuario)
    {
        using var conexion = SqlConnectionFactory.Open(_cadenaConexion);
        using var comando = new SqlCommand("UPDATE USUARIO SET token_version = token_version + 1 WHERE PK_id_usuario = @id", conexion);
        comando.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = idUsuario;
        comando.ExecuteNonQuery();
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
}
