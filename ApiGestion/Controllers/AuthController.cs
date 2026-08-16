using Microsoft.AspNetCore.Mvc;
using ApiGestion.Models;

namespace ApiGestion.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest request)
        {
            // Validación temporal hasta que conectemos la Base de Datos real (HU-031) en la capa DAO
            if (request.Usuario == "super@admin.com" && request.Contrasena == "admin123")
            {
                // Si está todo bien, devolvemos un estado 200 (OK)
                return Ok(new { mensaje = "¡Bienvenido al Portal Administrativo del CACC!" });
            }
            
            // Si las credenciales fallan, devolvemos un estado 401 (No autorizado)
            return Unauthorized(new { mensaje = "Usuario o contraseña incorrectos." });
        }
    }
}