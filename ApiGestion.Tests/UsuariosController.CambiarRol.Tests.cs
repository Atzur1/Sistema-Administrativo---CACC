namespace ApiGestion.Tests;

using System.ComponentModel.DataAnnotations;
using ApiGestion.Controllers;
using ApiGestion.Models;
using DaoLibrary;
using EntityLibrary;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

// HU-105: el cambio de rol viaja con la revisión que vio la pantalla y responde el estado canónico; cada rechazo
// tiene su código para que la pantalla sepa si reconciliar o solo avisar.
public class UsuariosControllerCambiarRolTests
{
    private sealed class DaoFalso : IUsuariosPortalDao
    {
        public (CambiarRolResultado, CambioDeRol?) Respuesta { get; set; } = (CambiarRolResultado.Ok, null);
        public (int Id, int Rol, int Revision)? Recibido { get; private set; }

        public (CambiarRolResultado Resultado, CambioDeRol? Cambio) CambiarRol(int idUsuario, int nuevoRolPortal, int revisionEsperada)
        {
            Recibido = (idUsuario, nuevoRolPortal, revisionEsperada);
            return Respuesta;
        }

        public IReadOnlyList<UsuarioCandidato> ListarCandidatos() => throw new NotSupportedException();
        public IReadOnlyList<UsuarioPortal> ListarHabilitados() => throw new NotSupportedException();
        public IReadOnlyList<UsuarioPortal> ListarDeshabilitados() => throw new NotSupportedException();
        public EditarCandidatoResultado EditarDatosCandidato(int idUsuario, string nombre, string apellido, string dni) => throw new NotSupportedException();
        public (HabilitarResultado Resultado, string? Email) Habilitar(int idUsuario, string emailConfirmado, int rolPortal) => throw new NotSupportedException();
        public bool Deshabilitar(int idUsuario) => throw new NotSupportedException();
        public bool Reactivar(int idUsuario, int rolPortal) => throw new NotSupportedException();
    }

    private static (UsuariosController Controller, DaoFalso Dao) Crear()
    {
        var dao = new DaoFalso();
        // Cambiar el rol no manda correos ni toca tokens de acceso.
        return (new UsuariosController(dao, null!, null!, NullLogger<UsuariosController>.Instance), dao);
    }

    [Fact]
    public void ConLaRevisionAlDia_DevuelveElEstadoCanonico()
    {
        var (controller, dao) = Crear();
        dao.Respuesta = (CambiarRolResultado.Ok, new CambioDeRol(5, 2, 1, 8));

        var respuesta = controller.CambiarRol(5, new RoleChangeRequestDTO { RolPortal = 1, RevisionEsperada = 7 });

        Assert.Equal((5, 1, 7), dao.Recibido);
        var cuerpo = Assert.IsType<RoleChangeResponseDTO>(Assert.IsType<OkObjectResult>(respuesta).Value);
        Assert.Equal((5, 2, 1, 8), (cuerpo.IdUsuario, cuerpo.RolAnterior, cuerpo.RolPortal, cuerpo.Revision));
    }

    [Theory]
    [InlineData(CambiarRolResultado.RevisionDesactualizada, 409, "revision-desactualizada")]
    [InlineData(CambiarRolResultado.UltimoSuperAdmin, 409, "ultimo-superadmin")]
    [InlineData(CambiarRolResultado.NoEncontrado, 404, "no-encontrada")]
    [InlineData(CambiarRolResultado.RolInvalido, 400, "rol-invalido")]
    public void CadaRechazoTieneSuEstadoYSuCodigo(CambiarRolResultado resultado, int estado, string codigo)
    {
        var (controller, dao) = Crear();
        dao.Respuesta = (resultado, null);

        var respuesta = Assert.IsAssignableFrom<ObjectResult>(controller.CambiarRol(5, new RoleChangeRequestDTO { RolPortal = 2, RevisionEsperada = 3 }));

        Assert.Equal(estado, respuesta.StatusCode);
        Assert.Equal(codigo, respuesta.Value!.GetType().GetProperty("codigo")!.GetValue(respuesta.Value));
    }

    [Fact]
    public void SinRevision_ElPedidoNoEsValido()
    {
        var sinRevision = new RoleChangeRequestDTO { RolPortal = 2 };
        var conRevision = new RoleChangeRequestDTO { RolPortal = 2, RevisionEsperada = 0 };

        Assert.False(Validator.TryValidateObject(sinRevision, new ValidationContext(sinRevision), null, validateAllProperties: true));
        Assert.True(Validator.TryValidateObject(conRevision, new ValidationContext(conRevision), null, validateAllProperties: true));
    }

    // Reactivar sigue con su propio contrato: solo el rol, sin revisión.
    [Fact]
    public void Reactivar_SigueSinPedirRevision()
    {
        var reactivar = new ChangeRoleRequestDTO { RolPortal = 1 };

        Assert.True(Validator.TryValidateObject(reactivar, new ValidationContext(reactivar), null, validateAllProperties: true));
        Assert.Null(typeof(ChangeRoleRequestDTO).GetProperty("RevisionEsperada"));
    }
}
