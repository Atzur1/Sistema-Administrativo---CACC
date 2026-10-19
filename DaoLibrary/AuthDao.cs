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
        // Unificada: nombre, apellido y DNI viven en PERSONAS. Entra quien tiene acceso al Portal Administrativo o un rol del
        // Portal Deportivo (FK_id_rol); el Administrativo sigue autorizando solo con acceso_portal / rol_portal.
        string query = $@"SELECT TOP (2) PK_id_usuario, email, nombre, apellido, dni, contrasenia, token_version, acceso_portal, rol_portal,
                   FK_id_rol, ROLES.nombre_rol AS nombre_rol_deportivo
            FROM USUARIOS JOIN PERSONAS ON PERSONAS.PK_id_persona = USUARIOS.FK_id_persona
                 LEFT JOIN ROLES ON ROLES.PK_id_rol = USUARIOS.FK_id_rol
            WHERE {(buscado.Value.EsCorreo ? "email = @valor" : DniNormalizadoSql + " = @valor")}
              AND activo = 1 AND (acceso_portal = 1 OR FK_id_rol IS NOT NULL) AND activacion_pendiente = 0";
        using var comando = new SqlCommand(query, conexion);
        comando.Parameters.Add("@valor", System.Data.SqlDbType.NVarChar, 254).Value = buscado.Value.Valor;
        using var reader = comando.ExecuteReader();
        if (!reader.Read())
        {
            // Mismo costo que verificar una contraseña real: sin esto, "ese DNI no existe" respondía
            // notablemente más rápido que "contraseña incorrecta" y delataba qué DNI tienen cuenta.
            PasswordHasher.VerifyPassword(contrasenia, HashDeReferencia);
            return null;
        }

        var hash = reader["contrasenia"] as string;

        var usuario = new Usuario
        {
            IdUsuario = Convert.ToInt32(reader["PK_id_usuario"]),
            Email = reader["email"].ToString() ?? string.Empty,
            Nombre = reader["nombre"] as string ?? string.Empty,
            Apellido = reader["apellido"] as string ?? string.Empty,
            Dni = reader["dni"] as string ?? string.Empty,
            AccesoPortal = Convert.ToBoolean(reader["acceso_portal"]),
            RolPortal = reader["rol_portal"] != DBNull.Value ? Convert.ToInt32(reader["rol_portal"]) : null,
            TokenVersion = Convert.ToInt32(reader["token_version"]),
            IdRolDeportivo = reader["FK_id_rol"] != DBNull.Value ? Convert.ToInt32(reader["FK_id_rol"]) : null,
            NombreRolDeportivo = reader["nombre_rol_deportivo"] as string
        };
        // La constraint CK_USUARIO_acceso_portal_requiere_rol garantiza que rol_portal
        // no sea NULL cuando acceso_portal = 1, que es lo único que llega hasta acá.
        usuario.IdRol = usuario.AccesoPortal ? usuario.RolPortal!.Value : 0;
        if (reader.Read()) return null; // identificador ambiguo

        // Solo contraseñas con hash (BCrypt, o PBKDF2 de antes del cambio). La columna contrasenia en texto plano ya
        // no se lee: una cuenta sin contrasenia no puede entrar hasta crear su contraseña con el enlace de
        // activación o de "¿Olvidaste tu contraseña?". Se gasta igual el tiempo de una verificación para no delatarla.
        if (hash is null)
        {
            PasswordHasher.VerifyPassword(contrasenia, HashDeReferencia);
            return null;
        }

        if (!PasswordHasher.VerifyPassword(contrasenia, hash))
        {
            return null;
        }

        // Migración progresiva: la contraseña recién se conoce en un login correcto, así que es el único momento
        // en que un hash PBKDF2 se puede pasar a BCrypt.
        if (PasswordHasher.NeedsRehash(hash))
        {
            UpdatePasswordHash(usuario.IdUsuario, hash, PasswordHasher.HashPassword(contrasenia));
        }

        usuario.IdsCategorias = CategoriasDelStaff(usuario.IdUsuario);
        return usuario;
    }

    // Categorías que tiene asignadas (Director Técnico / Preparador Físico): las usa el Portal Deportivo para acotar lo que ve.
    private List<int> CategoriasDelStaff(int idUsuario)
    {
        var ids = new List<int>();
        using var conexion = SqlConnectionFactory.Open(_cadenaConexion);
        using var comando = new SqlCommand(@"SELECT sc.FK_id_categoria FROM STAFF s JOIN STAFF_CATEGORIAS sc ON sc.FK_id_staff = s.PK_id_staff
            WHERE s.FK_id_usuario = @id AND sc.FK_id_categoria IS NOT NULL", conexion);
        comando.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = idUsuario;
        using var lector = comando.ExecuteReader();
        while (lector.Read()) ids.Add(lector.GetInt32(0));
        return ids;
    }

    // Hash válido de una contraseña al azar, solo para gastar el mismo tiempo que una verificación real.
    private static readonly string HashDeReferencia = PasswordHasher.HashPassword(Guid.NewGuid().ToString("N"));

    // Only replaces the hash that was just verified: if a password reset changed it in between, that newer
    // password wins. The account itself is recorded as the actor in AUDITORIA_CAMBIOS.
    public bool UpdatePasswordHash(int userId, string currentHash, string newHash)
    {
        using (SqlAuditActorContext.Push(userId))
        {
            using var connection = SqlConnectionFactory.Open(_cadenaConexion);
            using var command = new SqlCommand(
                "UPDATE dbo.USUARIOS SET contrasenia = @newHash WHERE PK_id_usuario = @id AND contrasenia = @currentHash",
                connection);
            command.Parameters.Add("@newHash", System.Data.SqlDbType.NVarChar, 512).Value = newHash;
            command.Parameters.Add("@currentHash", System.Data.SqlDbType.NVarChar, 512).Value = currentHash;
            command.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = userId;
            return command.ExecuteNonQuery() == 1;
        }
    }

    // Cierre de sesión en el servidor: subir token_version invalida todos los JWT emitidos hasta ahora
    // para esa cuenta (OnTokenValidated los compara), en vez de esperar a que venzan solos.
    public void InvalidarSesiones(int idUsuario)
    {
        using var conexion = SqlConnectionFactory.Open(_cadenaConexion);
        using var comando = new SqlCommand("UPDATE USUARIOS SET token_version = token_version + 1 WHERE PK_id_usuario = @id", conexion);
        comando.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = idUsuario;
        comando.ExecuteNonQuery();
    }

    // idRol 0 = cuenta sin acceso al Portal Administrativo (solo Portal Deportivo): se valida solo que siga activa.
    public bool UsuarioActivoConRol(int idUsuario, int idRol, int tokenVersion)
    {
        using var conexion = SqlConnectionFactory.Open(_cadenaConexion);
        using var comando = new SqlCommand(idRol == 0
            ? "SELECT 1 FROM USUARIOS WHERE PK_id_usuario = @id AND @rol = 0 AND activo = 1 AND token_version = @version"
            : "SELECT 1 FROM USUARIOS WHERE PK_id_usuario = @id AND rol_portal = @rol AND acceso_portal = 1 AND activo = 1 AND token_version = @version", conexion);
        comando.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = idUsuario;
        comando.Parameters.Add("@rol", System.Data.SqlDbType.Int).Value = idRol;
        comando.Parameters.Add("@version", System.Data.SqlDbType.Int).Value = tokenVersion;
        return comando.ExecuteScalar() is not null;
    }
}
