using System.Security.Cryptography;
using System.Text;
using ApiGestion.Models;
using ApiGestion.Services;
using DaoLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApiGestion.Controllers
{
    // Usuarios y Permisos (fase 2 de HU-043): alta/baja/cambio de rol de cuentas
    // administrativas. Solo el SuperAdmin puede tocar esto.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "1")]
    public class UsuariosController : ControllerBase
    {
        private readonly IUsuariosPortalDao _usuariosDao;
        private readonly AccountAccessDao _accountAccessDao;
        private readonly EmailLinkSender _emailSender;
        private readonly ILogger<UsuariosController> _logger;

        public UsuariosController(
            IUsuariosPortalDao usuariosDao,
            AccountAccessDao accountAccessDao,
            EmailLinkSender emailSender,
            ILogger<UsuariosController> logger)
        {
            _usuariosDao = usuariosDao;
            _accountAccessDao = accountAccessDao;
            _emailSender = emailSender;
            _logger = logger;
        }

        // GET api/usuarios/candidatos -> personas cargadas por el otro equipo con
        // FK_id_rol de SuperAdmin/Administrador que todavía no tienen acceso.
        [HttpGet("candidatos")]
        public IActionResult ListarCandidatos() => Ok(_usuariosDao.ListarCandidatos());

        // GET api/usuarios/habilitados -> tabla principal de la pantalla.
        [HttpGet("habilitados")]
        public IActionResult ListarHabilitados() => Ok(_usuariosDao.ListarHabilitados());

        // PUT api/usuarios/5 -> corrige nombre/apellido/mail de un candidato antes
        // de habilitarlo, por si el otro equipo los cargó mal.
        [HttpPut("{id}")]
        public IActionResult EditarCandidato(int id, [FromBody] EditarCandidatoRequest request)
        {
            if (!_usuariosDao.EditarDatosCandidato(id, request.Nombre, request.Apellido, request.Email))
                return BadRequest(new { mensaje = "No se encontró a ese candidato sin acceso todavía." });
            return Ok(new { mensaje = "Datos corregidos." });
        }

        // POST api/usuarios/5/habilitar -> otorga acceso_portal=1 + rol_portal y
        // dispara el mail de activación.
        [HttpPost("{id}/habilitar")]
        public async Task<IActionResult> Habilitar(int id, [FromBody] HabilitarUsuarioRequest request, CancellationToken cancellationToken)
        {
            var (resultado, email) = _usuariosDao.Habilitar(id, request.Email, request.RolPortal);
            if (resultado != HabilitarResultado.Ok || email is null)
            {
                var mensaje = resultado switch
                {
                    HabilitarResultado.EmailNoCoincide => "El mail no coincide con el que tiene cargado esa persona.",
                    HabilitarResultado.NoEncontrado => "No se encontró a esa persona sin acceso todavía, o ya está habilitada.",
                    _ => "No se pudo habilitar el acceso."
                };
                return BadRequest(new { mensaje });
            }

            try
            {
                var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
                _accountAccessDao.CreateActivationToken(id, tokenHash, DateTime.UtcNow.AddMinutes(30));
                await _emailSender.SendActivationLink(email, token, cancellationToken);
            }
            catch (Exception exception)
            {
                // El acceso ya quedó otorgado (auditado) aunque el mail falle: mejor
                // que el SuperAdmin reintente el envío a que quede a mitad de camino.
                _logger.LogError(exception, "El acceso de {IdUsuario} se habilitó pero falló el envío del correo de activación.", id);
                return Ok(new { mensaje = "Se habilitó el acceso, pero no se pudo enviar el correo de activación. Reintentá desde Usuarios y Permisos." });
            }

            return Ok(new { mensaje = "Cuenta habilitada. Se envió un correo para que active su acceso." });
        }

        // PUT api/usuarios/5/rol -> cambia el rol de alguien ya habilitado.
        [HttpPut("{id}/rol")]
        public IActionResult CambiarRol(int id, [FromBody] CambiarRolRequest request)
        {
            if (!_usuariosDao.CambiarRol(id, request.RolPortal))
                return Conflict(new { mensaje = "No se pudo cambiar el rol: verificá que la cuenta esté habilitada y que quede al menos un SuperAdmin." });
            return Ok(new { mensaje = "Rol actualizado." });
        }

        // POST api/usuarios/5/deshabilitar -> revoca el acceso y corta la sesión.
        [HttpPost("{id}/deshabilitar")]
        public IActionResult Deshabilitar(int id)
        {
            if (!_usuariosDao.Deshabilitar(id))
                return Conflict(new { mensaje = "No se puede deshabilitar: debe quedar al menos un SuperAdmin habilitado." });
            return Ok(new { mensaje = "Acceso revocado." });
        }
    }
}
