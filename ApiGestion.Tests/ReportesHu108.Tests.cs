using System.Reflection;
using ApiGestion.Controllers;
using DaoLibrary;
using Microsoft.AspNetCore.Authorization;

namespace ApiGestion.Tests;

public class ReportesHu108Tests
{
    [Fact]
    public void Historial_RequiereSuperAdmin()
        => Assert.Equal("1", typeof(ReportesController).GetMethod("Historial")!.GetCustomAttribute<AuthorizeAttribute>()!.Roles);

    [Fact]
    public void Conteo_ExisteSinExportador()
        => Assert.NotNull(typeof(IReportesDao).GetMethod("Contar"));

    [Fact]
    public void Historial_SoloMetadatos()
    {
        var nombres = typeof(EntityLibrary.ExportacionReporte).GetProperties().Select(p => p.Name).ToArray();
        Assert.DoesNotContain("Contenido", nombres);
        Assert.Contains("CantidadFilas", nombres);
    }
}
