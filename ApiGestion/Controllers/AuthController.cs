using Microsoft.AspNetCore.Mvc;
using ApiGestion.Models;
using DaoLibrary;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ApiGestion.Services;
using Microsoft.AspNetCore.RateLimiting;
using System.Text;
using ServiceLibrary;

namespace ApiGestion.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AuthDao _authDao;
        private readonly IConfiguration _config;
        private readonly ILogger<AuthController> _logger;
        private readonly AccountAccessDao _accountAccessDao;
        private readonly EmailLinkSender _emailSender;
        private readonly LoginThrottle _loginThrottle;

        // Inyectamos el AuthDao y la configuración (para leer la clave JWT)
        public AuthController(AuthDao authDao, IConfiguration config, ILogger<AuthController> logger,
            AccountAccessDao accountAccessDao, EmailLinkSender emailSender, LoginThrottle loginThrottle)
        {
            _authDao = authDao;
            _config = config;
            _logger = logger;
            _accountAccessDao = accountAccessDao;
            _emailSender = emailSender;
            _loginThrottle = loginThrottle;
        }

        [HttpPost("password-reset/request")]
        [EnableRateLimiting("account-email")]
        public async Task<IActionResult> SolicitarRestablecimiento([FromBody] PasswordRecoveryRequestDTO request, CancellationToken cancellationToken)
        {
            const string response = "Si los datos corresponden a una cuenta activa, el enlace llegará al correo registrado en esa cuenta.";
            var (token, tokenHash) = TokenAccesoCuenta.Generar();
            try
            {
                // Cualquier cuenta con acceso al portal (SuperAdmin o Administrador) puede recuperar su contraseña.
                // La respuesta es siempre la misma, exista o no la cuenta, para no revelar qué DNI o correos están
                // registrados. El enlace va al correo que la cuenta tiene cargado, no a lo que se escribió acá.
                var account = _accountAccessDao.CreateResetToken(request.Usuario, tokenHash, DateTime.UtcNow.AddMinutes(30));
                if (account is not null)
                {
                    // El envío va aparte: si se esperara acá, responder tardaría más cuando la cuenta existe y esa
                    // diferencia de tiempo delataría qué correos están registrados.
                    _ = EnviarEnlaceRecuperacionAsync(account, token);
                }
            }
            catch (Exception exception) { _logger.LogError(exception, "Falló el procesamiento de recuperación de contraseña."); }
            return Ok(new { mensaje = response });
        }

        private async Task EnviarEnlaceRecuperacionAsync(DestinatarioCorreo destinatario, string token)
        {
            try
            {
                await _emailSender.SendPasswordResetLink(destinatario.Email, destinatario.Nombre, token, CancellationToken.None);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "No se pudo enviar el correo de recuperación de contraseña.");
            }
        }

        // Completa tanto un token de RECUPERACION (olvidé mi contraseña)
        // como uno de ACTIVACION (alta hecha desde Usuarios y Permisos): el tipo lo
        // resuelve el propio token, no hace falta que el cliente lo indique.
        [HttpPost("account-access/complete")]
        [EnableRateLimiting("account-email")]
        public IActionResult CompletarAcceso([FromBody] CompleteAccountAccessRequestDTO request)
        {
            var tokenHash = TokenAccesoCuenta.Hash(request.Token);
            var titular = _accountAccessDao.ObtenerTitularDelToken(tokenHash);
            if (titular is null)
                return BadRequest(new { mensaje = "El enlace no es válido o venció." });

            // Política de contraseñas nuevas (las que ya existen siguen funcionando).
            var problemas = PoliticaContrasenia.Validar(request.Password,
                new DatosTitular(titular.Value.Nombre, titular.Value.Apellido, titular.Value.Dni, titular.Value.Email));
            if (problemas.Count > 0)
                return BadRequest(new { mensaje = "La contraseña no cumple los requisitos: " + string.Join(" ", problemas), problemas });

            if (!_accountAccessDao.CompleteAccountAccess(tokenHash, PasswordHasher.HashPassword(request.Password)))
                return BadRequest(new { mensaje = "El enlace no es válido o venció." });
            return Ok(new { mensaje = "La contraseña se actualizó. Ya puede iniciar sesión." });
        }

        [HttpPost("login")]
        [EnableRateLimiting("login")]
        public IActionResult Login([FromBody] LoginDTO request)
        {
            try
            {
                // Límite por cuenta (además del de IP). Cuenta los fallos de cualquier identificador, exista o no,
                // para que bloquearse no revele qué DNI o correos están registrados.
                var identificador = IdentificadorCuenta.Interpretar(request.Username);
                var clave = identificador?.Valor;
                if (clave is not null && _loginThrottle.Bloqueado(clave))
                    return StatusCode(StatusCodes.Status429TooManyRequests,
                        new { mensaje = "Demasiados intentos fallidos. Esperá unos minutos antes de volver a intentar." });

                // Consultamos directamente a la base de datos usando nuestra capa DAO
                var usuarioEncontrado = _authDao.ValidarLogin(request.Username, request.Password);

                if (usuarioEncontrado != null)
                {
                    if (clave is not null) _loginThrottle.Limpiar(clave);

                    // Generamos el token JWT con el rol adentro
                    string token = GenerarToken(usuarioEncontrado);

                    return Ok(new LoginResponseDTO
                    {
                        Message = "¡Bienvenido al Portal Administrativo del CACC!",
                        Email = usuarioEncontrado.Email,
                        FirstName = usuarioEncontrado.Nombre,
                        LastName = usuarioEncontrado.Apellido,
                        Dni = usuarioEncontrado.Dni,
                        Role = usuarioEncontrado.IdRol,
                        Token = token
                    });
                }

                // Si devuelve null, las credenciales no coinciden con la BD
                if (clave is not null) _loginThrottle.RegistrarFallo(clave);
                return Unauthorized(new { mensaje = "Usuario o contraseña incorrectos." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error durante la autenticación");
                return StatusCode(500, new { mensaje = "Error interno en el servidor." });
            }
        }

        // POST api/auth/logout -> invalida el token actual (y cualquier otra sesión abierta de esa cuenta).
        // Sin esto, cerrar sesión solo borraba el token del navegador y seguía sirviendo hasta su vencimiento.
        [Microsoft.AspNetCore.Authorization.Authorize]
        [HttpPost("logout")]
        public IActionResult Logout()
        {
            if (!int.TryParse(User.FindFirst("idUsuario")?.Value, out var idUsuario))
                return Unauthorized();

            _authDao.InvalidarSesiones(idUsuario);
            return NoContent();
        }

        // Un solo token para los dos portales (ver ClaimsDelToken: el Administrador no lleva permisos del Deportivo).
        private string GenerarToken(EntityLibrary.Usuario usuario)
        {
            var claims = ClaimsDelToken.Construir(usuario);

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddHours(1),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
