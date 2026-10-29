namespace ApiGestion.Tests;

using ApiGestion.Services;
using EntityLibrary;

// HU-007: a dónde puede entrar cada cuenta lo decide el backend y viaja en la respuesta del login. El frontend navega
// con eso (directo al Deportivo, directo al Administrativo o la elección de portales) y no deduce permisos por su cuenta.
public class PortalesDeLaCuentaTests
{
    private static Usuario Cuenta(int rolPortal, int? idRolDeportivo = null) => new()
    {
        IdUsuario = 7, IdRol = rolPortal, AccesoPortal = rolPortal != 0, RolPortal = rolPortal == 0 ? null : rolPortal,
        IdRolDeportivo = idRolDeportivo
    };

    [Theory]
    [InlineData(4)] // Director Técnico
    [InlineData(6)] // Preparador Físico
    [InlineData(3)] // Médico
    public void QuienSoloTieneRolDelDeportivo_EntraSoloAlDeportivo(int idRolDeportivo)
    {
        Assert.Equal(new[] { PortalesDeLaCuenta.Deportivo }, PortalesDeLaCuenta.De(Cuenta(rolPortal: 0, idRolDeportivo)));
    }

    [Fact]
    public void ElSuperAdmin_EligeEntreLosTresPortales()
    {
        Assert.Equal(
            new[] { PortalesDeLaCuenta.Administrativo, PortalesDeLaCuenta.Deportivo, PortalesDeLaCuenta.Personas },
            PortalesDeLaCuenta.De(Cuenta(rolPortal: 1, idRolDeportivo: 1)));
    }

    // Mismo aislamiento que ClaimsDelToken: el Administrador no recibe nada del Deportivo aunque su cuenta tenga un rol cargado.
    [Fact]
    public void ElAdministrador_EntraSoloAlAdministrativo_AunqueTengaRolDelDeportivo()
    {
        Assert.Equal(new[] { PortalesDeLaCuenta.Administrativo }, PortalesDeLaCuenta.De(Cuenta(rolPortal: 2, idRolDeportivo: 2)));
    }

    [Fact]
    public void UnaCuentaSinAccesoAdministrativoNiRolDelDeportivo_NoTienePortales()
    {
        Assert.Empty(PortalesDeLaCuenta.De(Cuenta(rolPortal: 0)));
    }
}
