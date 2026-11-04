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

    // HU-105: cambio de rol condicionado a la revisión (token_version) y deshacer como una escritura compensatoria.

    // Una cuenta con acceso al portal. Operativa: ya completó la activación y tiene contraseña (puede ingresar hoy).
    private static int InsertarCuentaConAcceso(SqlConnection connection, SqlTransaction transaction, string dni, string email, int rolPortal, bool operativa = true)
    {
        int id = InsertarUsuario(connection, transaction, dni, email, conAcceso: true);
        using var command = new SqlCommand(
            @"UPDATE dbo.USUARIOS SET rol_portal = @rol, activacion_pendiente = @pendiente,
                contrasenia = CASE WHEN @pendiente = 0 THEN N'hash-de-prueba' END
              WHERE PK_id_usuario = @id", connection, transaction);
        command.Parameters.AddWithValue("@rol", rolPortal);
        command.Parameters.AddWithValue("@pendiente", operativa ? 0 : 1);
        command.Parameters.AddWithValue("@id", id);
        command.ExecuteNonQuery();
        return id;
    }

    private static (int Rol, int Revision) RolYRevision(SqlConnection connection, SqlTransaction transaction, int id)
    {
        using var command = new SqlCommand("SELECT rol_portal, token_version FROM dbo.USUARIOS WHERE PK_id_usuario = @id", connection, transaction);
        command.Parameters.AddWithValue("@id", id);
        using var r = command.ExecuteReader();
        Assert.True(r.Read());
        return (r.GetInt32(0), r.GetInt32(1));
    }

    // Que ninguna otra cuenta de la base cuente como SuperAdmin (se revierte con la transacción).
    private static void SinOtrosSuperAdmins(SqlConnection connection, SqlTransaction transaction)
    {
        using var command = new SqlCommand("UPDATE dbo.USUARIOS SET rol_portal = 2 WHERE rol_portal = 1", connection, transaction);
        command.ExecuteNonQuery();
    }

    [IntegracionFact]
    public void CambiarRol_DevuelveElEstadoCanonicoLeidoEnLaMismaTransaccion()
    {
        var (connection, transaction) = Abrir();
        try
        {
            int id = InsertarCuentaConAcceso(connection, transaction, "90410001", "rol-canonico@cacc.test", rolPortal: 2);
            var (_, revision) = RolYRevision(connection, transaction, id);

            var (resultado, cambio) = UsuariosPortalDao.CambiarRolCore(connection, transaction, id, 1, revision);

            Assert.Equal(CambiarRolResultado.Ok, resultado);
            Assert.Equal(new CambioDeRol(id, 2, 1, revision + 1), cambio);
            Assert.Equal((1, revision + 1), RolYRevision(connection, transaction, id));
        }
        finally { transaction.Rollback(); connection.Dispose(); }
    }

    [IntegracionFact]
    public void Deshacer_EsOtraEscrituraConElRolAnteriorYLaRevisionDevuelta_YLaRevisionNuncaBaja()
    {
        var (connection, transaction) = Abrir();
        try
        {
            InsertarCuentaConAcceso(connection, transaction, "90410012", "otro-super-1@cacc.test", rolPortal: 1); // para poder volver a 2
            int id = InsertarCuentaConAcceso(connection, transaction, "90410002", "rol-deshacer@cacc.test", rolPortal: 2);
            var (_, inicial) = RolYRevision(connection, transaction, id);
            var (_, cambio) = UsuariosPortalDao.CambiarRolCore(connection, transaction, id, 1, inicial);

            var (resultado, deshecho) = UsuariosPortalDao.CambiarRolCore(connection, transaction, id, cambio!.RolAnterior, cambio.Revision);

            Assert.Equal(CambiarRolResultado.Ok, resultado);
            Assert.Equal(new CambioDeRol(id, 1, 2, inicial + 2), deshecho);
            Assert.Equal((2, inicial + 2), RolYRevision(connection, transaction, id));
        }
        finally { transaction.Rollback(); connection.Dispose(); }
    }

    [IntegracionFact]
    public void UnPedidoConUnaRevisionVieja_NoPisa_AunqueElRolHayaVueltoAlMismoValor()
    {
        var (connection, transaction) = Abrir();
        try
        {
            InsertarCuentaConAcceso(connection, transaction, "90410013", "otro-super-2@cacc.test", rolPortal: 1);
            int id = InsertarCuentaConAcceso(connection, transaction, "90410003", "rol-aba@cacc.test", rolPortal: 2);
            var (_, vieja) = RolYRevision(connection, transaction, id);
            var (_, ida) = UsuariosPortalDao.CambiarRolCore(connection, transaction, id, 1, vieja);
            UsuariosPortalDao.CambiarRolCore(connection, transaction, id, 2, ida!.Revision); // A -> B -> A

            var (resultado, cambio) = UsuariosPortalDao.CambiarRolCore(connection, transaction, id, 1, vieja);

            Assert.Equal(CambiarRolResultado.RevisionDesactualizada, resultado);
            Assert.Null(cambio);
            Assert.Equal((2, vieja + 2), RolYRevision(connection, transaction, id));
        }
        finally { transaction.Rollback(); connection.Dispose(); }
    }

    [IntegracionFact]
    public void ElUltimoSuperAdmin_SoloCuentaALosQuePuedenIngresar()
    {
        var (connection, transaction) = Abrir();
        try
        {
            SinOtrosSuperAdmins(connection, transaction);
            int unico = InsertarCuentaConAcceso(connection, transaction, "90410004", "super-unico@cacc.test", rolPortal: 1);
            int pendiente = InsertarCuentaConAcceso(connection, transaction, "90410005", "super-pendiente@cacc.test", rolPortal: 1, operativa: false);
            var (_, revision) = RolYRevision(connection, transaction, unico);

            Assert.Equal(CambiarRolResultado.UltimoSuperAdmin, UsuariosPortalDao.CambiarRolCore(connection, transaction, unico, 2, revision).Resultado);
            Assert.Equal((1, revision), RolYRevision(connection, transaction, unico));

            using (var activar = new SqlCommand(
                "UPDATE dbo.USUARIOS SET activacion_pendiente = 0, contrasenia = N'hash-de-prueba' WHERE PK_id_usuario = @id", connection, transaction))
            {
                activar.Parameters.AddWithValue("@id", pendiente);
                activar.ExecuteNonQuery();
            }
            Assert.Equal(CambiarRolResultado.Ok, UsuariosPortalDao.CambiarRolCore(connection, transaction, unico, 2, revision).Resultado);
        }
        finally { transaction.Rollback(); connection.Dispose(); }
    }

    // HU-061 (LAST-05, SCRUM-583): deshabilitar al único SuperAdmin operativo tampoco se permite aunque haya otro con
    // la activación pendiente; una vez que ese otro activa su cuenta, sí.
    [IntegracionFact]
    public void Deshabilitar_ElUltimoSuperAdmin_NoCuentaALosQueTienenLaActivacionPendiente()
    {
        var (connection, transaction) = Abrir();
        try
        {
            SinOtrosSuperAdmins(connection, transaction);
            int unico = InsertarCuentaConAcceso(connection, transaction, "90410014", "super-unico-desh@cacc.test", rolPortal: 1);
            int pendiente = InsertarCuentaConAcceso(connection, transaction, "90410015", "super-pendiente-desh@cacc.test", rolPortal: 1, operativa: false);

            Assert.False(UsuariosPortalDao.DeshabilitarCore(connection, transaction, unico));
            Assert.Equal(1, RolYRevision(connection, transaction, unico).Rol);

            using (var activar = new SqlCommand(
                "UPDATE dbo.USUARIOS SET activacion_pendiente = 0, contrasenia = N'hash-de-prueba' WHERE PK_id_usuario = @id", connection, transaction))
            {
                activar.Parameters.AddWithValue("@id", pendiente);
                activar.ExecuteNonQuery();
            }

            Assert.True(UsuariosPortalDao.DeshabilitarCore(connection, transaction, unico));
        }
        finally { transaction.Rollback(); connection.Dispose(); }
    }

    [IntegracionFact]
    public void CambiarRol_DeUnaCuentaSinAccesoOConRolInvalido_NoEscribe()
    {
        var (connection, transaction) = Abrir();
        try
        {
            int sinAcceso = InsertarUsuario(connection, transaction, "90410006", "rol-sin-acceso@cacc.test", conAcceso: false);
            int conAcceso = InsertarCuentaConAcceso(connection, transaction, "90410007", "rol-invalido@cacc.test", rolPortal: 2);
            var (_, revision) = RolYRevision(connection, transaction, conAcceso);

            Assert.Equal(CambiarRolResultado.NoEncontrado, UsuariosPortalDao.CambiarRolCore(connection, transaction, sinAcceso, 1, 0).Resultado);
            Assert.Equal(CambiarRolResultado.RolInvalido, UsuariosPortalDao.CambiarRolCore(connection, transaction, conAcceso, 3, revision).Resultado);
            Assert.Equal((2, revision), RolYRevision(connection, transaction, conAcceso));
        }
        finally { transaction.Rollback(); connection.Dispose(); }
    }

    // El cambio y su deshacer quedan como dos UPDATE reales de USUARIOS en la auditoría, con quién los hizo y el rol
    // de antes y de después (el trigger toma al operador de SESSION_CONTEXT, que en la API pone AuditActorMiddleware).
    [IntegracionFact]
    public void CambiarYDeshacer_QuedanComoDosEventosDeUsuariosConElOperador()
    {
        var (connection, transaction) = Abrir();
        try
        {
            int operador = InsertarCuentaConAcceso(connection, transaction, "90410008", "operador@cacc.test", rolPortal: 1);
            int id = InsertarCuentaConAcceso(connection, transaction, "90410009", "auditada@cacc.test", rolPortal: 2);
            var (_, revision) = RolYRevision(connection, transaction, id);
            using (var actor = new SqlCommand("EXEC sys.sp_set_session_context @key=N'idUsuario', @value=@idUsuario;", connection, transaction))
            {
                actor.Parameters.AddWithValue("@idUsuario", operador);
                actor.ExecuteNonQuery();
            }

            var (_, cambio) = UsuariosPortalDao.CambiarRolCore(connection, transaction, id, 1, revision);
            UsuariosPortalDao.CambiarRolCore(connection, transaction, id, cambio!.RolAnterior, cambio.Revision);

            using var leer = new SqlCommand(
                @"SELECT id_usuario, JSON_VALUE(datos_antes, '$.rolPortal'), JSON_VALUE(datos_despues, '$.rolPortal')
                  FROM dbo.AUDITORIA_CAMBIOS
                  WHERE entidad = N'USUARIOS' AND id_entidad = @id AND accion = 'UPDATE' AND id_usuario = @operador
                  ORDER BY PK_id_evento", connection, transaction);
            leer.Parameters.AddWithValue("@id", id.ToString());
            leer.Parameters.AddWithValue("@operador", operador);
            using var r = leer.ExecuteReader();
            var eventos = new List<(int Operador, string Antes, string Despues)>();
            while (r.Read()) eventos.Add((r.GetInt32(0), r.GetString(1), r.GetString(2)));

            Assert.Equal(new[] { (operador, "2", "1"), (operador, "1", "2") }, eventos);
        }
        finally { transaction.Rollback(); connection.Dispose(); }
    }
}
