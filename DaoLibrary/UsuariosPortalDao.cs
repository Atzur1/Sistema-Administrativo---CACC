using System.Data;
using EntityLibrary;
using Microsoft.Data.SqlClient;

namespace DaoLibrary
{
    public class UsuariosPortalDao : IUsuariosPortalDao
    {
        private readonly string _cadenaConexion;

        public UsuariosPortalDao(string cadenaConexion)
        {
            _cadenaConexion = cadenaConexion;
        }

        public IReadOnlyList<UsuarioCandidato> ListarCandidatos()
        {
            var resultado = new List<UsuarioCandidato>();

            // password_hash IS NULL: nunca completó una activación, así que nunca tuvo
            // contraseña propia. A alguien que ya la tuvo (se deshabilitó después de estar
            // activo) no le corresponde este flujo — ver ListarDeshabilitados/Reactivar.
            const string query = @"
                SELECT PK_id_usuario, nombre, apellido, dni, email, FK_id_rol
                FROM USUARIO
                WHERE activo = 1 AND acceso_portal = 0 AND FK_id_rol IN (1, 2) AND password_hash IS NULL
                ORDER BY apellido, nombre";

            using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);
            using SqlCommand comando = new SqlCommand(query, conexion);
            using SqlDataReader reader = comando.ExecuteReader();
            while (reader.Read())
            {
                resultado.Add(new UsuarioCandidato
                {
                    IdUsuario = Convert.ToInt32(reader["PK_id_usuario"]),
                    Nombre = reader["nombre"] as string ?? string.Empty,
                    Apellido = reader["apellido"] as string ?? string.Empty,
                    Dni = reader["dni"] as string ?? string.Empty,
                    Email = reader["email"] as string ?? string.Empty,
                    FkIdRolSugerido = Convert.ToInt32(reader["FK_id_rol"])
                });
            }

            return resultado;
        }

        public IReadOnlyList<UsuarioPortal> ListarHabilitados()
        {
            var resultado = new List<UsuarioPortal>();

            const string query = @"
                SELECT PK_id_usuario, nombre, apellido, dni, email, rol_portal, activo, activacion_pendiente
                FROM USUARIO
                WHERE acceso_portal = 1
                ORDER BY apellido, nombre";

            using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);
            using SqlCommand comando = new SqlCommand(query, conexion);
            using SqlDataReader reader = comando.ExecuteReader();
            while (reader.Read())
            {
                resultado.Add(new UsuarioPortal
                {
                    IdUsuario = Convert.ToInt32(reader["PK_id_usuario"]),
                    Nombre = reader["nombre"] as string ?? string.Empty,
                    Apellido = reader["apellido"] as string ?? string.Empty,
                    Dni = reader["dni"] as string ?? string.Empty,
                    Email = reader["email"] as string ?? string.Empty,
                    RolPortal = Convert.ToInt32(reader["rol_portal"]),
                    Activo = Convert.ToBoolean(reader["activo"]),
                    ActivacionPendiente = Convert.ToBoolean(reader["activacion_pendiente"])
                });
            }

            return resultado;
        }

        public IReadOnlyList<UsuarioPortal> ListarDeshabilitados()
        {
            var resultado = new List<UsuarioPortal>();

            // Ya tuvieron acceso alguna vez (tienen password_hash y un rol_portal de cuando
            // estaban habilitados) — reactivarlos no necesita token ni mail nuevo, solo
            // devolverles acceso_portal.
            const string query = @"
                SELECT PK_id_usuario, nombre, apellido, dni, email, rol_portal, activo, activacion_pendiente
                FROM USUARIO
                WHERE activo = 1 AND acceso_portal = 0 AND password_hash IS NOT NULL AND rol_portal IS NOT NULL
                ORDER BY apellido, nombre";

            using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);
            using SqlCommand comando = new SqlCommand(query, conexion);
            using SqlDataReader reader = comando.ExecuteReader();
            while (reader.Read())
            {
                resultado.Add(new UsuarioPortal
                {
                    IdUsuario = Convert.ToInt32(reader["PK_id_usuario"]),
                    Nombre = reader["nombre"] as string ?? string.Empty,
                    Apellido = reader["apellido"] as string ?? string.Empty,
                    Dni = reader["dni"] as string ?? string.Empty,
                    Email = reader["email"] as string ?? string.Empty,
                    RolPortal = Convert.ToInt32(reader["rol_portal"]),
                    Activo = Convert.ToBoolean(reader["activo"]),
                    ActivacionPendiente = Convert.ToBoolean(reader["activacion_pendiente"])
                });
            }

            return resultado;
        }

        public bool Reactivar(int idUsuario)
        {
            using var conexion = SqlConnectionFactory.Open(_cadenaConexion);
            using var comando = new SqlCommand(
                @"UPDATE dbo.USUARIO SET acceso_portal = 1
                  WHERE PK_id_usuario = @id AND activo = 1 AND acceso_portal = 0
                    AND password_hash IS NOT NULL AND rol_portal IS NOT NULL",
                conexion);
            comando.Parameters.Add("@id", SqlDbType.Int).Value = idUsuario;
            try
            {
                return comando.ExecuteNonQuery() == 1;
            }
            catch (SqlException excepcion) when (excepcion.Number is 2601 or 2627)
            {
                // Otra cuenta con acceso ya usa el mismo DNI (índice único del login): no se puede reactivar.
                return false;
            }
        }

        public EditarCandidatoResultado EditarDatosCandidato(int idUsuario, string nombre, string apellido, string dni, string email)
        {
            using var conexion = SqlConnectionFactory.Open(_cadenaConexion);
            using var transaccion = conexion.BeginTransaction(IsolationLevel.Serializable);
            var resultado = EditarDatosCandidatoCore(conexion, transaccion, idUsuario, nombre, apellido, dni, email);
            if (resultado == EditarCandidatoResultado.Ok) transaccion.Commit(); else transaccion.Rollback();
            return resultado;
        }

        internal static EditarCandidatoResultado EditarDatosCandidatoCore(
            SqlConnection conexion, SqlTransaction transaccion, int idUsuario, string nombre, string apellido, string dni, string email)
        {
            var dniNormalizado = IdentificadorCuenta.NormalizarDni(dni);
            if (dniNormalizado is null) return EditarCandidatoResultado.DniInvalido;
            if (DniEnUsoPorOtraCuenta(conexion, transaccion, dniNormalizado, idUsuario)) return EditarCandidatoResultado.DniEnUso;

            using var comando = new SqlCommand(
                @"UPDATE dbo.USUARIO SET nombre = @nombre, apellido = @apellido, dni = @dni, email = @email
                  WHERE PK_id_usuario = @id AND activo = 1 AND acceso_portal = 0",
                conexion, transaccion);
            comando.Parameters.Add("@nombre", SqlDbType.NVarChar, 100).Value = nombre.Trim();
            comando.Parameters.Add("@apellido", SqlDbType.NVarChar, 100).Value = apellido.Trim();
            comando.Parameters.Add("@dni", SqlDbType.NVarChar, 40).Value = dniNormalizado;
            comando.Parameters.Add("@email", SqlDbType.NVarChar, 254).Value = email.Trim();
            comando.Parameters.Add("@id", SqlDbType.Int).Value = idUsuario;
            return comando.ExecuteNonQuery() == 1 ? EditarCandidatoResultado.Ok : EditarCandidatoResultado.NoEncontrado;
        }

        // El DNI es el usuario del login: dos cuentas con acceso no pueden compartirlo (el índice único
        // UX_USUARIO_dni_acceso_portal lo garantiza; esto permite avisarlo con un mensaje claro).
        private static bool DniEnUsoPorOtraCuenta(SqlConnection conexion, SqlTransaction transaccion, string dniNormalizado, int idExcluido)
        {
            using var buscar = new SqlCommand(
                $@"SELECT TOP (1) 1 FROM dbo.USUARIO WITH (UPDLOCK, HOLDLOCK)
                   WHERE acceso_portal = 1 AND PK_id_usuario <> @id AND {AuthDao.DniNormalizadoSql} = @dni",
                conexion, transaccion);
            buscar.Parameters.Add("@id", SqlDbType.Int).Value = idExcluido;
            buscar.Parameters.Add("@dni", SqlDbType.NVarChar, 40).Value = dniNormalizado;
            return buscar.ExecuteScalar() is not null;
        }

        public (HabilitarResultado Resultado, string? Email) Habilitar(int idUsuario, string emailConfirmado, int rolPortal)
        {
            if (rolPortal != 1 && rolPortal != 2) return (HabilitarResultado.RolInvalido, null);

            using var conexion = SqlConnectionFactory.Open(_cadenaConexion);
            using var transaccion = conexion.BeginTransaction(IsolationLevel.Serializable);
            var resultado = HabilitarCore(conexion, transaccion, idUsuario, emailConfirmado, rolPortal);
            if (resultado.Resultado == HabilitarResultado.Ok) transaccion.Commit(); else transaccion.Rollback();
            return resultado;
        }

        internal static (HabilitarResultado Resultado, string? Email) HabilitarCore(
            SqlConnection conexion, SqlTransaction transaccion, int idUsuario, string emailConfirmado, int rolPortal)
        {
            string? email;
            string? dni;
            using (var buscar = new SqlCommand(
                "SELECT email, dni FROM dbo.USUARIO WITH (UPDLOCK, HOLDLOCK) WHERE PK_id_usuario = @id AND activo = 1 AND acceso_portal = 0",
                conexion, transaccion))
            {
                buscar.Parameters.Add("@id", SqlDbType.Int).Value = idUsuario;
                using var lector = buscar.ExecuteReader();
                if (!lector.Read()) return (HabilitarResultado.NoEncontrado, null);
                email = lector.IsDBNull(0) ? null : lector.GetString(0);
                dni = lector.IsDBNull(1) ? null : lector.GetString(1);
            }

            if (!string.Equals(email?.Trim(), emailConfirmado.Trim(), StringComparison.OrdinalIgnoreCase))
                return (HabilitarResultado.EmailNoCoincide, null);

            // Sin un DNI válido la cuenta no tendría usuario con el cual ingresar.
            var dniNormalizado = IdentificadorCuenta.NormalizarDni(dni);
            if (dniNormalizado is null) return (HabilitarResultado.DniInvalido, null);
            if (DniEnUsoPorOtraCuenta(conexion, transaccion, dniNormalizado, idUsuario)) return (HabilitarResultado.DniEnUso, null);

            using var actualizar = new SqlCommand(
                @"UPDATE dbo.USUARIO SET acceso_portal = 1, rol_portal = @rol, activacion_pendiente = 1, dni = @dni
                  WHERE PK_id_usuario = @id AND activo = 1 AND acceso_portal = 0",
                conexion, transaccion);
            actualizar.Parameters.Add("@dni", SqlDbType.NVarChar, 40).Value = dniNormalizado;
            actualizar.Parameters.Add("@rol", SqlDbType.Int).Value = rolPortal;
            actualizar.Parameters.Add("@id", SqlDbType.Int).Value = idUsuario;
            return actualizar.ExecuteNonQuery() == 1 ? (HabilitarResultado.Ok, email) : (HabilitarResultado.NoEncontrado, null);
        }

        public bool CambiarRol(int idUsuario, int nuevoRolPortal)
        {
            if (nuevoRolPortal != 1 && nuevoRolPortal != 2) return false;

            using var conexion = SqlConnectionFactory.Open(_cadenaConexion);
            using var transaccion = conexion.BeginTransaction(IsolationLevel.Serializable);

            using (var bloquear = new SqlCommand(
                "SELECT rol_portal FROM dbo.USUARIO WITH (UPDLOCK, HOLDLOCK) WHERE PK_id_usuario = @id AND acceso_portal = 1",
                conexion, transaccion))
            {
                bloquear.Parameters.Add("@id", SqlDbType.Int).Value = idUsuario;
                var actual = bloquear.ExecuteScalar();
                if (actual is null)
                {
                    transaccion.Rollback();
                    return false;
                }

                var rolActual = Convert.ToInt32(actual);
                if (rolActual == 1 && nuevoRolPortal != 1 && !QuedaOtroSuperAdminHabilitado(conexion, transaccion, idUsuario))
                {
                    transaccion.Rollback();
                    return false;
                }
            }

            using (var actualizar = new SqlCommand(
                "UPDATE dbo.USUARIO SET rol_portal = @rol, token_version = token_version + 1 WHERE PK_id_usuario = @id AND acceso_portal = 1",
                conexion, transaccion))
            {
                actualizar.Parameters.Add("@rol", SqlDbType.Int).Value = nuevoRolPortal;
                actualizar.Parameters.Add("@id", SqlDbType.Int).Value = idUsuario;
                if (actualizar.ExecuteNonQuery() != 1)
                {
                    transaccion.Rollback();
                    return false;
                }
            }

            transaccion.Commit();
            return true;
        }

        public bool Deshabilitar(int idUsuario)
        {
            using var conexion = SqlConnectionFactory.Open(_cadenaConexion);
            using var transaccion = conexion.BeginTransaction(IsolationLevel.Serializable);

            using (var bloquear = new SqlCommand(
                "SELECT rol_portal FROM dbo.USUARIO WITH (UPDLOCK, HOLDLOCK) WHERE PK_id_usuario = @id AND acceso_portal = 1",
                conexion, transaccion))
            {
                bloquear.Parameters.Add("@id", SqlDbType.Int).Value = idUsuario;
                var actual = bloquear.ExecuteScalar();
                if (actual is null)
                {
                    transaccion.Rollback();
                    return false;
                }

                var rolActual = Convert.ToInt32(actual);
                if (rolActual == 1 && !QuedaOtroSuperAdminHabilitado(conexion, transaccion, idUsuario))
                {
                    transaccion.Rollback();
                    return false;
                }
            }

            using (var actualizar = new SqlCommand(
                "UPDATE dbo.USUARIO SET acceso_portal = 0, token_version = token_version + 1 WHERE PK_id_usuario = @id AND acceso_portal = 1",
                conexion, transaccion))
            {
                actualizar.Parameters.Add("@id", SqlDbType.Int).Value = idUsuario;
                if (actualizar.ExecuteNonQuery() != 1)
                {
                    transaccion.Rollback();
                    return false;
                }
            }

            transaccion.Commit();
            return true;
        }

        // Nunca deja que la última operación sobre un SuperAdmin (bajarle el rol o
        // deshabilitarlo) apague el acceso de todo el club: siempre tiene que
        // quedar, como mínimo, otro SuperAdmin habilitado.
        private static bool QuedaOtroSuperAdminHabilitado(SqlConnection conexion, SqlTransaction transaccion, int idUsuarioExcluido)
        {
            using var contar = new SqlCommand(
                @"SELECT COUNT(*) FROM dbo.USUARIO WITH (UPDLOCK, HOLDLOCK)
                  WHERE rol_portal = 1 AND acceso_portal = 1 AND activo = 1 AND PK_id_usuario <> @id",
                conexion, transaccion);
            contar.Parameters.Add("@id", SqlDbType.Int).Value = idUsuarioExcluido;
            return Convert.ToInt32(contar.ExecuteScalar()) > 0;
        }
    }
}
