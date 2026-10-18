using System.Security.Claims;
using EntityLibrary;

namespace ApiGestion.Services;

// Claims del token compartido por el Portal Administrativo y el Portal Deportivo (misma clave, emisor y audiencia).
//
//  - Administrativo: ClaimTypes.Role = rol_portal ("1" SuperAdmin, "2" Administrador), solo si la cuenta tiene acceso.
//    "rol_portal" ("0" si no tiene acceso) y "tokenVersion" son los que valida esta API en cada pedido.
//  - Deportivo: ClaimTypes.Role = nombre del rol de ROLES, "id_rol" y "id_categoria" (uno por categoría del staff).
//
// El Administrador (rol_portal 2) trabaja SOLO en el Portal Administrativo (cobranzas, deudores, becas y descuentos):
// su token nunca lleva nada del Deportivo, aunque su cuenta tenga un rol de ROLES cargado. Así el aislamiento no
// depende de cómo autorice el otro sistema.
public static class ClaimsDelToken
{
    public const int RolAdministrador = 2;

    public static List<Claim> Construir(Usuario usuario)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, usuario.Email),
            new(ClaimTypes.NameIdentifier, usuario.IdUsuario.ToString()),
            new("idUsuario", usuario.IdUsuario.ToString()),
            new("tokenVersion", usuario.TokenVersion.ToString()),
            new("rol_portal", usuario.IdRol.ToString())
        };
        if (usuario.IdRol != 0) claims.Add(new Claim(ClaimTypes.Role, usuario.IdRol.ToString()));

        if (usuario.IdRol == RolAdministrador) return claims;

        if (!string.IsNullOrWhiteSpace(usuario.NombreRolDeportivo)) claims.Add(new Claim(ClaimTypes.Role, usuario.NombreRolDeportivo));
        if (usuario.IdRolDeportivo is int idRolDeportivo) claims.Add(new Claim("id_rol", idRolDeportivo.ToString()));
        foreach (int idCategoria in usuario.IdsCategorias) claims.Add(new Claim("id_categoria", idCategoria.ToString()));
        return claims;
    }
}
