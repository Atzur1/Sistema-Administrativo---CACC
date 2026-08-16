using Microsoft.AspNetCore.Mvc;
using ApiGestion.Models;
using DaoLibrary;

namespace ApiGestion.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AuthDao _authDao;

        // Inyectamos el AuthDao que configuramos en el Program.cs
        public AuthController(AuthDao authDao)
        {
            _authDao = authDao;
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
                    // Si la base de datos lo devuelve, el login es exitoso
                    return Ok(new { 
                        mensaje = "¡Bienvenido al Portal Administrativo del CACC!",
                        email = usuarioEncontrado.Email,
                        rol = usuarioEncontrado.IdRol 
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
    }
}