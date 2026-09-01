using Microsoft.AspNetCore.Mvc;
using ApiGestion.Models;
using DaoLibrary;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace ApiGestion.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AuthDao _authDao;
        private readonly IConfiguration _config;

        // Inyectamos el AuthDao y la configuración (para leer la clave JWT)
        public AuthController(AuthDao authDao, IConfiguration config)
        {
            _authDao = authDao;
            _config = config;
        }

        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest request)
        {
            try
            {
                // Consultamos directamente a la base de datos usando nuestra capa DAO
                var usuarioEncontrado = _authDao.ValidarLogin(request.Usuario, request.Contrasena);

                if (usuarioEncontrado != null)
                {
                    // Generamos el token JWT con el rol adentro
                    string token = GenerarToken(usuarioEncontrado.Email, usuarioEncontrado.IdRol);

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
                return StatusCode(500, new { mensaje = "Error interno en el servidor", error = ex.Message });
            }
        }

        private string GenerarToken(string email, int idRol)
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.Email, email),
                new Claim(ClaimTypes.Role, idRol.ToString())
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddHours(8), // el token dura 8 horas
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}