namespace ApiGestion.Tests;

using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ApiGestion.Controllers;
using ApiGestion.Models;
using DaoLibrary;
using EntityLibrary;

// HU-020: CategoriasController es un passthrough de ICategoriasDao.ObtenerTodas — el
// catálogo completo de categorías del club, independiente de quién debe o no (la consulta
// SQL en sí se validó aparte contra la base real). Acá solo se cubre el contrato del
// endpoint: que expone el resultado del DAO como DTO y que exige autenticación.
public class CategoriasControllerTests
{
    private static (CategoriasController controller, FakeCategoriasDao dao) CreateController()
    {
        FakeCategoriasDao dao = new();
        return (new CategoriasController(dao), dao);
    }

    [Fact]
    public void ObtenerTodas_ReturnsOkWithEveryCategoryAsADto()
    {
        (CategoriasController controller, FakeCategoriasDao dao) = CreateController();
        dao.Result = new List<Categoria>
        {
            new() { IdCategoria = 1, Nombre = "AFA 20067" },
            new() { IdCategoria = 2, Nombre = "AFA 20112" },
        };

        IActionResult result = controller.ObtenerTodas();

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result);
        List<CategoryResponseDTO> categories = Assert.IsType<List<CategoryResponseDTO>>(ok.Value);
        Assert.Equal(new[] { 1, 2 }, categories.Select(c => c.IdCategoria));
        Assert.Equal(new[] { "AFA 20067", "AFA 20112" }, categories.Select(c => c.Nombre));
    }

    [Fact]
    public void ObtenerTodas_WithNoCategories_ReturnsOkWithAnEmptyList()
    {
        (CategoriasController controller, _) = CreateController();

        IActionResult result = controller.ObtenerTodas();

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result);
        Assert.Empty(Assert.IsType<List<CategoryResponseDTO>>(ok.Value));
    }

    [Fact]
    public void Controller_RequiresAnAuthenticatedUser()
    {
        Assert.NotNull(typeof(CategoriasController).GetCustomAttribute<AuthorizeAttribute>());
    }

    [Fact]
    public void ObtenerTodas_IsAReadOnlyGetEndpoint()
    {
        MethodInfo method = typeof(CategoriasController).GetMethod(nameof(CategoriasController.ObtenerTodas))!;

        Assert.Single(method.GetCustomAttributes<HttpGetAttribute>());
        Assert.Empty(method.GetCustomAttributes<HttpPostAttribute>());
        Assert.Empty(method.GetCustomAttributes<HttpPutAttribute>());
        Assert.Empty(method.GetCustomAttributes<HttpDeleteAttribute>());
    }

    // ---- Fakes ----

    private class FakeCategoriasDao : ICategoriasDao
    {
        public IReadOnlyList<Categoria> Result { get; set; } = new List<Categoria>();

        public IReadOnlyList<Categoria> ObtenerTodas() => Result;
    }
}
