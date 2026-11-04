using EntityLibrary;

namespace ApiGestion.Services;

// HU-007: a qué portales puede entrar la cuenta que acaba de iniciar sesión. Viaja en la respuesta del login y el frontend
// navega con eso: con un solo portal entra directo, con varios elige (HU-005). Es la misma regla de ClaimsDelToken:
//  - SuperAdmin (rol_portal 1): Administrativo, Deportivo y Personas (el alta de personas, que vive en el Deportivo).
//  - Administrador (rol_portal 2): solo el Administrativo; su token no lleva nada del Deportivo.
//  - Sin acceso al Administrativo y con un rol de ROLES (Director Técnico, Preparador Físico, Médico…): solo el Deportivo.
// Esto decide la navegación. Lo que se puede hacer en cada portal lo sigue autorizando cada API en cada pedido.
public static class PortalesDeLaCuenta
{
    public const string Administrativo = "administrativo";
    public const string Deportivo = "deportivo";
    public const string Personas = "personas";

    public static IReadOnlyList<string> De(Usuario usuario) => usuario.IdRol switch
    {
        1 => new[] { Administrativo, Deportivo, Personas },
        ClaimsDelToken.RolAdministrador => new[] { Administrativo },
        0 when usuario.IdRolDeportivo is not null => new[] { Deportivo },
        _ => Array.Empty<string>()
    };
}
