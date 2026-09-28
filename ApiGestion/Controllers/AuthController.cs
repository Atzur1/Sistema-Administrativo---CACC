using Microsoft.AspNetCore.Mvc;
using ApiGestion.Models;
using DaoLibrary;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.RateLimiting;

namespace ApiGestion.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AuthDao _authDao;
        private readonly IConfiguration _config;
        private readonly ILogger<AuthController> _logger;

        // Inyectamos el AuthDao y la configuración (para leer la clave JWT)
        public AuthController(AuthDao authDao, IConfiguration config, ILogger<AuthController> logger)
        {
            _authDao = authDao;
            _config = config;
            _logger = logger;
        }

        [HttpPost("login")]
        [EnableRateLimiting("login")]
        public IActionResult Login([FromBody] LoginRequest request)
        {
            try
            {
                // Consultamos directamente a la base de datos usando nuestra capa DAO
                var usuarioEncontrado = _authDao.ValidarLogin(request.Usuario, request.Contrasena);

                if (usuarioEncontrado != null)
                {
                    // Generamos el token JWT con el rol adentro
                    string token = GenerarToken(usuarioEncontrado.IdUsuario, usuarioEncontrado.Email, usuarioEncontrado.IdRol);

                    return Ok(new {
                        mensaje = "¡Bienvenido al Portal Administrativo del CACC!",
                        email = usuarioEncontrado.Email,
                        rol = usuarioEncontrado.IdRol,
                        token = token
                    });
                }

                // Si devuelve null, las credenciales no coinciden con la BD
                return Unauthorized(new { mensaje = "Usuario o contraseña incorrectos." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error durante la autenticación");
                return StatusCode(500, new { mensaje = "Error interno en el servidor." });
            }
        }

        private string GenerarToken(int idUsuario, string email, int idRol)
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.Email, email),
                new Claim(ClaimTypes.Role, idRol.ToString()),
                // Identificador de negocio usado por endpoints protegidos (ej. quién registró un cobro)
                new Claim("idUsuario", idUsuario.ToString())
            };

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
