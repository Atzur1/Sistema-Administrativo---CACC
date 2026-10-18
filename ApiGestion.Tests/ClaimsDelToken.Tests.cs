namespace ApiGestion.Tests;

using System.Security.Claims;
using ApiGestion.Services;
using EntityLibrary;

// El token es compartido con el Portal Deportivo. El Administrador (rol 2) solo usa el Administrativo: su token no
// puede abrirle nada del Deportivo.
public class ClaimsDelTokenTests
{
    private static Usuario Cuenta(int rolPortal, int? idRolDeportivo = null, string? nombreRolDeportivo = null, params int[] categorias) => new()
    {
        IdUsuario = 7, Email = "cuenta@cacc.test", TokenVersion = 3, IdRol = rolPortal, AccesoPortal = rolPortal != 0,
        RolPortal = rolPortal == 0 ? null : rolPortal, IdRolDeportivo = idRolDeportivo, NombreRolDeportivo = nombreRolDeportivo,
        IdsCategorias = categorias.ToList()
    };

    private static List<string> Valores(List<Claim> claims, string tipo) => claims.Where(c => c.Type == tipo).Select(c => c.Value).ToList();

    [Fact]
    public void ElAdministrador_NoLlevaNadaDelDeportivo_AunqueSuCuentaTengaUnRolCargado()
    {
        var claims = ClaimsDelToken.Construir(Cuenta(rolPortal: 2, idRolDeportivo: 2, nombreRolDeportivo: "Administrativo", categorias: 3));

        Assert.Equal(new[] { "2" }, Valores(claims, ClaimTypes.Role));
        Assert.Empty(Valores(claims, "id_rol"));
        Assert.Empty(Valores(claims, "id_categoria"));
        Assert.Equal("2", Valores(claims, "rol_portal").Single());
    }

    [Fact]
    public void ElSuperAdmin_LlevaSuRolDeLosDosPortales()
    {
        var claims = ClaimsDelToken.Construir(Cuenta(rolPortal: 1, idRolDeportivo: 1, nombreRolDeportivo: "Administrador (Tesorero)"));

        Assert.Equal(new[] { "1", "Administrador (Tesorero)" }, Valores(claims, ClaimTypes.Role));
        Assert.Equal("1", Valores(claims, "id_rol").Single());
    }

    [Fact]
    public void QuienSoloTieneRolDelDeportivo_NoRecibeRolDelAdministrativo()
    {
        var claims = ClaimsDelToken.Construir(Cuenta(rolPortal: 0, idRolDeportivo: 4, nombreRolDeportivo: "Director Técnico", categorias: new[] { 3, 5 }));

        Assert.Equal(new[] { "Director Técnico" }, Valores(claims, ClaimTypes.Role));
        Assert.Equal("0", Valores(claims, "rol_portal").Single());
        Assert.Equal(new[] { "3", "5" }, Valores(claims, "id_categoria"));
    }

    [Fact]
    public void TodoTokenLlevaLaIdentidadYLaVersionDeSesion()
    {
        var claims = ClaimsDelToken.Construir(Cuenta(rolPortal: 2));

        Assert.Equal("7", Valores(claims, "idUsuario").Single());
        Assert.Equal("3", Valores(claims, "tokenVersion").Single());
        Assert.Equal("cuenta@cacc.test", Valores(claims, ClaimTypes.Email).Single());
    }
}
