using ApiGestion.Database;

namespace ApiGestion.Tests;

public class MigracionesTests
{
    [Fact]
    public void OrdenaYSeleccionaSoloPendientes()
    {
        var primera = Migracion.Crear("V20260923_01__primera.sql", "SELECT 1;");
        var segunda = Migracion.Crear("V20260923_02__segunda.sql", "SELECT 2;");
        var historial = new Dictionary<string, string> { [primera.Nombre] = primera.Hash };
        Assert.Equal(new[] { segunda }, PlanMigraciones.Pendientes([segunda, primera], historial));
    }

    [Fact]
    public void RechazaCambioEnScriptAplicado()
    {
        var original = Migracion.Crear("V20260923_01__primera.sql", "SELECT 1;");
        var editada = Migracion.Crear(original.Nombre, "SELECT 2;");
        Assert.Throws<InvalidOperationException>(() => PlanMigraciones.Pendientes([editada],
            new Dictionary<string, string> { [original.Nombre] = original.Hash }));
    }

    [Fact]
    public void ToleraDiferenciasDeFinDeLineaEntreEquipos()
    {
        Assert.Equal(Migracion.Crear("V20260923_01__primera.sql", "SELECT 1;\r\n").Hash,
            Migracion.Crear("V20260923_01__primera.sql", "SELECT 1;\n").Hash);
    }

    [Fact]
    public void RechazaVersionDuplicada()
    {
        Assert.Throws<InvalidOperationException>(() => PlanMigraciones.Pendientes([
            Migracion.Crear("V20260923_01__uno.sql", "SELECT 1;"),
            Migracion.Crear("V20260923_01__dos.sql", "SELECT 2;")], new Dictionary<string, string>()));
    }

    [Fact]
    public void RechazaScriptAplicadoAusenteYVersionInsertadaEnElPasado()
    {
        var primera = Migracion.Crear("V20260923_01__primera.sql", "SELECT 1;");
        var segunda = Migracion.Crear("V20260923_02__segunda.sql", "SELECT 2;");
        var historial = new Dictionary<string, string> { [segunda.Nombre] = segunda.Hash };
        Assert.Throws<InvalidOperationException>(() => PlanMigraciones.Pendientes([], historial));
        Assert.Throws<InvalidOperationException>(() => PlanMigraciones.Pendientes([primera, segunda], historial));
    }

    [Theory]
    [InlineData("SELECT 1;\nGO\nSELECT 2;")]
    [InlineData("SELECT 1;\nGO 2")]
    [InlineData("BEGIN TRANSACTION; SELECT 1;")]
    [InlineData("COMMIT;")]
    [InlineData("ROLLBACK TRANSACTION;")]
    [InlineData("USE OtraBase;")]
    [InlineData("SET NOEXEC ON;")]
    [InlineData("SET PARSEONLY ON;")]
    [InlineData("SELECT $(VariableSqlCmd);")]
    [InlineData(":r otro.sql")]
    [InlineData("")]
    public void RechazaFormatoNoSoportado(string sql)
    {
        Assert.Throws<InvalidOperationException>(() => Migracion.Crear("V20260923_01__primera.sql", sql));
    }

    [Theory]
    [InlineData("seed.sql")]
    [InlineData("V20260923_1__cambio.sql")]
    public void RechazaNombreInvalido(string nombre)
    {
        Assert.Throws<InvalidOperationException>(() => Migracion.Crear(nombre, "SELECT 1;"));
    }
}
