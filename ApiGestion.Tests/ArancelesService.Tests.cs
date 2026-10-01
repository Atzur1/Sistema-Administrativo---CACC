namespace ApiGestion.Tests;

using DaoLibrary;
using DaoLibrary.Exceptions;
using EntityLibrary;
using ServiceLibrary;

// El arancel es por género O por categoría (uno solo). Estas validaciones corren antes de tocar
// la base, por eso el servicio se arma sin DAO de aranceles/pagos ni runner transaccional.
public class ArancelesServiceTests
{
    private static ArancelesService CreateService() =>
        new ArancelesService(null!, null!, null!, new FakeCategoriasDao());

    private static ProgramarArancelRequest Request(string? genero, int? idCategoria, decimal monto = 50000) => new()
    {
        Genero = genero,
        IdCategoria = idCategoria,
        Monto = monto,
        VigenteDesde = new DateTime(2026, 10, 1)
    };

    [Fact]
    public void ProgramarArancel_WithGenderAndCategory_IsRefused()
    {
        var ex = Assert.Throws<ArancelInvalidoException>(() => CreateService().ProgramarArancel(Request("Masculino", 1)));
        Assert.Contains("género o por categoría", ex.Message);
    }

    [Fact]
    public void ProgramarArancel_WithNeitherGenderNorCategory_IsRefused()
    {
        Assert.Throws<ArancelInvalidoException>(() => CreateService().ProgramarArancel(Request(null, null)));
        Assert.Throws<ArancelInvalidoException>(() => CreateService().ProgramarArancel(Request("  ", null)));
    }

    [Fact]
    public void ProgramarArancel_WithAnInvalidGender_IsRefused()
    {
        Assert.Throws<ArancelInvalidoException>(() => CreateService().ProgramarArancel(Request("Otro", null)));
    }

    [Fact]
    public void ProgramarArancel_WithAnUnknownCategory_IsRefused()
    {
        var ex = Assert.Throws<ArancelInvalidoException>(() => CreateService().ProgramarArancel(Request(null, 999)));
        Assert.Contains("999", ex.Message);
    }

    [Fact]
    public void ProgramarArancel_WithANonPositiveAmount_IsRefused()
    {
        Assert.Throws<ArancelInvalidoException>(() => CreateService().ProgramarArancel(Request("Masculino", null, 0)));
        Assert.Throws<ArancelInvalidoException>(() => CreateService().ProgramarArancel(Request(null, 1, -5)));
    }

    private class FakeCategoriasDao : ICategoriasDao
    {
        public IReadOnlyList<Categoria> ObtenerTodas() => new List<Categoria>
        {
            new() { IdCategoria = 1, Nombre = "Sub 17" },
            new() { IdCategoria = 2, Nombre = "Sub 15" }
        };
    }
}
