namespace ApiGestion.Tests;

using DaoLibrary;
using Microsoft.Data.SqlClient;

// Login con la activacion del Portal Administrativo pendiente (SCRUM-584), contra la base real y dentro de una transaccion
// que se revierte. Regla: el acceso al Administrativo exige activar la cuenta; el acceso al Deportivo es independiente y
// entra sin activacion.
[Collection(BaseDeDatosCollection.Nombre)]
public class AuthDaoActivacionPendienteIntegrationTests
{
    private const string Clave = "Clave-De-Prueba-77";

    private static (SqlConnection Conexion, SqlTransaction Transaccion) Abrir()
    {
        var conexion = new SqlConnection(Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable));
        conexion.Open();
        return (conexion, conexion.BeginTransaction());
    }

    // Una persona con contrasena y, segun el caso, rol deportivo, acceso al Administrativo y activacion pendiente.
    private static void CrearCuenta(SqlConnection c, SqlTransaction t, string dni, int? rolDeportivo, bool accesoPortal, bool activacionPendiente)
    {
        using var cmd = new SqlCommand(
            @"INSERT dbo.PERSONAS (nombre, apellido, dni) VALUES (N'Prueba', N'Activacion', @dni);
              DECLARE @persona INT = SCOPE_IDENTITY();
              INSERT dbo.USUARIOS (FK_id_persona, email, contrasenia, FK_id_rol, activo, acceso_portal, rol_portal, activacion_pendiente)
              VALUES (@persona, @email, @hash, @rol, 1, @acceso, CASE WHEN @acceso = 1 THEN 2 END, @pendiente);", c, t);
        cmd.Parameters.AddWithValue("@dni", dni);
        cmd.Parameters.AddWithValue("@email", $"{dni}@cacc.test");
        cmd.Parameters.AddWithValue("@hash", PasswordHasher.HashPassword(Clave));
        cmd.Parameters.AddWithValue("@rol", (object?)rolDeportivo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@acceso", accesoPortal);
        cmd.Parameters.AddWithValue("@pendiente", activacionPendiente);
        cmd.ExecuteNonQuery();
    }

    private static EntityLibrary.Usuario? Entrar(SqlConnection c, SqlTransaction t, string dni) =>
        new AuthDao(c.ConnectionString).ValidarLoginCore(c, t, dni, Clave);

    [IntegracionFact]
    public void ConActivacionPendiente_UnaPersonaConRolDeportivoEntraSinActivar_PeroSoloAlDeportivo()
    {
        var (c, t) = Abrir();
        try
        {
            CrearCuenta(c, t, "90610001", rolDeportivo: 3, accesoPortal: true, activacionPendiente: true);

            var usuario = Entrar(c, t, "90610001");

            Assert.NotNull(usuario);
            Assert.False(usuario!.AccesoPortal);   // el Administrativo todavia no rige
            Assert.Equal(0, usuario.IdRol);        // el token no lleva rol del Administrativo
            Assert.Null(usuario.RolPortal);
            Assert.Equal(3, usuario.IdRolDeportivo);
        }
        finally { t.Rollback(); c.Dispose(); }
    }

    [IntegracionFact]
    public void ActivadaLaCuenta_EntraConSuRolDelAdministrativo()
    {
        var (c, t) = Abrir();
        try
        {
            CrearCuenta(c, t, "90610002", rolDeportivo: 3, accesoPortal: true, activacionPendiente: false);

            var usuario = Entrar(c, t, "90610002");

            Assert.NotNull(usuario);
            Assert.True(usuario!.AccesoPortal);
            Assert.Equal(2, usuario.IdRol);
            Assert.Equal(2, usuario.RolPortal);
        }
        finally { t.Rollback(); c.Dispose(); }
    }

    [IntegracionFact]
    public void ConActivacionPendiente_QuienNoTieneRolDeportivoNoEntraDeNingunModo()
    {
        var (c, t) = Abrir();
        try
        {
            CrearCuenta(c, t, "90610003", rolDeportivo: null, accesoPortal: true, activacionPendiente: true);

            Assert.Null(Entrar(c, t, "90610003"));
        }
        finally { t.Rollback(); c.Dispose(); }
    }

    [IntegracionFact]
    public void ConActivacionPendiente_UnaContraseniaIncorrectaSigueRechazada()
    {
        var (c, t) = Abrir();
        try
        {
            CrearCuenta(c, t, "90610004", rolDeportivo: 3, accesoPortal: true, activacionPendiente: true);

            Assert.Null(new AuthDao(c.ConnectionString).ValidarLoginCore(c, t, "90610004", "otra-clave-cualquiera"));
        }
        finally { t.Rollback(); c.Dispose(); }
    }

    [IntegracionFact]
    public void ElDeportivoSinAccesoAlAdministrativo_SigueEntrandoComoAntes()
    {
        var (c, t) = Abrir();
        try
        {
            CrearCuenta(c, t, "90610005", rolDeportivo: 4, accesoPortal: false, activacionPendiente: false);

            var usuario = Entrar(c, t, "90610005");

            Assert.NotNull(usuario);
            Assert.False(usuario!.AccesoPortal);
            Assert.Equal(0, usuario.IdRol);
        }
        finally { t.Rollback(); c.Dispose(); }
    }
}
