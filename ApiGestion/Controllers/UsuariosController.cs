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

        // GET api/usuarios/deshabilitados -> gente que ya tuvo acceso y se le revocó.
        [HttpGet("deshabilitados")]
        public IActionResult ListarDeshabilitados() => Ok(_usuariosDao.ListarDeshabilitados());

        // PUT api/usuarios/5 -> corrige nombre/apellido/DNI/mail de un candidato antes
        // de habilitarlo, por si hubo un error de carga.
        [HttpPut("{id}")]
        public IActionResult EditarCandidato(int id, [FromBody] EditarCandidatoRequest request)
        {
            var resultado = _usuariosDao.EditarDatosCandidato(id, request.Nombre, request.Apellido, request.Dni, request.Email);
            if (resultado == EditarCandidatoResultado.Ok) return Ok(new { mensaje = "Datos corregidos." });

            var mensaje = resultado switch
            {
                EditarCandidatoResultado.DniInvalido => MensajeDniInvalido,
                EditarCandidatoResultado.DniEnUso => MensajeDniEnUso,
                _ => "No se encontró a ese candidato sin acceso todavía."
            };
            return resultado == EditarCandidatoResultado.NoEncontrado
                ? BadRequest(new { mensaje })
                : UnprocessableEntity(new { mensaje });
        }

        private const string MensajeDniInvalido = "El DNI no es válido: tiene que tener entre 6 y 9 números.";
        private const string MensajeDniEnUso = "Ese DNI ya lo usa otra cuenta con acceso al portal.";

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
                    HabilitarResultado.DniInvalido => "Antes de habilitarla hay que cargar un DNI válido (con el lápiz): es el usuario con el que va a ingresar.",
                    HabilitarResultado.DniEnUso => MensajeDniEnUso,
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

        // POST api/usuarios/5/resetear-contrasena -> el SuperAdmin le manda a una cuenta habilitada el enlace para
        // elegir una contraseña nueva. El enlace va SIEMPRE al correo de la propia cuenta (nunca a quien lo pide):
        // el SuperAdmin no ve ni fija la contraseña de nadie. Según la situación de la cuenta es el correo de
        // "Reestablece tu contraseña" (ya tenía una) o el de "Crea tu contraseña" (todavía no la había creado,
        // por ejemplo porque el enlace de activación venció).
        [HttpPost("{id}/resetear-contrasena")]
        public async Task<IActionResult> ResetearContrasena(int id, CancellationToken cancellationToken)
        {
            const string noEnCondiciones = "Solo se puede enviar el enlace a una cuenta con acceso habilitado y activa.";
            var estado = _accountAccessDao.ObtenerEstadoAcceso(id);
            if (estado is null) return BadRequest(new { mensaje = noEnCondiciones });

            try
            {
                var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
                var vence = DateTime.UtcNow.AddMinutes(30);

                if (estado.ActivacionPendiente)
                {
                    _accountAccessDao.CreateActivationToken(id, tokenHash, vence);
                    await _emailSender.SendActivationLink(estado.Email, token, cancellationToken);
                    return Ok(new { mensaje = $"Se reenvió a {estado.Email} el correo para crear su contraseña." });
                }

                var email = _accountAccessDao.CreateResetTokenForUser(id, tokenHash, vence);
                if (email is null) return BadRequest(new { mensaje = noEnCondiciones });
                await _emailSender.SendPasswordResetLink(email, token, cancellationToken);
                return Ok(new { mensaje = $"Se envió a {email} el enlace para reestablecer su contraseña." });
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "No se pudo enviar el enlace de contraseña a la cuenta {IdUsuario}.", id);
                return StatusCode(StatusCodes.Status502BadGateway, new { mensaje = "No se pudo enviar el correo. Reintentá en unos minutos." });
            }
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

        // POST api/usuarios/5/reactivar -> le devuelve el acceso a alguien ya
        // deshabilitado, con el mismo rol y la misma contraseña. Sin mail.
        [HttpPost("{id}/reactivar")]
        public IActionResult Reactivar(int id)
        {
            if (!_usuariosDao.Reactivar(id))
                return BadRequest(new { mensaje = "No se pudo reactivar: verificá que la persona esté entre las deshabilitadas y que su DNI no lo use otra cuenta con acceso." });
            return Ok(new { mensaje = "Acceso restablecido." });
        }
    }
}
