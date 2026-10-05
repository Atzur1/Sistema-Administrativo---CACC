namespace ApiGestion.Tests;

using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ApiGestion.Controllers;
using DaoLibrary;
using EntityLibrary;
using ServiceLibrary;

// HU-021 (reporte de deudores, QA 24/09): tres cosas puntuales que QA marcó como faltantes
// -"Período consultado" en el encabezado, el nombre de categoría resuelto contra el
// catálogo (no contra el padrón de deudores, que puede venir vacío) y el passthrough del
// filtro idCategoria hacia el service. El orden de las filas vive en SQL (PagosDao) y se
// valida aparte; acá solo se cubre el contrato de este controller.
public class ReportesControllerTests
{
    // El PDF de deudores pasa por QuestPDF, que exige declarar la licencia al arrancar
    // (Program.cs lo hace para la app real; el proceso de test necesita lo mismo).
    static ReportesControllerTests()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
    }

    private static (ReportesController controller, FakePagosService pagosService, FakeCategoriasDao categoriasDao) CreateController()
    {
        FakePagosService pagosService = new();
        FakeCategoriasDao categoriasDao = new();
        var controller = new ReportesController(
            pagosService,
            new FakeArancelesService(),
            new DiscountDao(""),
            categoriasDao);
        return (controller, pagosService, categoriasDao);
    }

    private static PendienteJugador Deudor(int idCategoria, string categoria) => new()
    {
        IdJugador = 1,
        NombreCompleto = "PRUEBA, SANTIAGO",
        Dni = "99000001",
        IdCategoria = idCategoria,
        Categoria = categoria,
        MontoTotal = 92_000m,
        CantidadCuotas = 1,
    };

    private static string DecodeCsv(FileContentResult file) => Encoding.UTF8.GetString(file.FileContents).TrimStart('﻿');

    [Fact]
    public void ExportarDeudoresCsv_AsksTheServiceForTheGivenCategoryFilter()
    {
        (ReportesController controller, FakePagosService pagosService, _) = CreateController();

        controller.ExportarDeudoresCsv(idCategoria: 5);

        Assert.Equal(new int?[] { 5 }, pagosService.PendientesCalls);
    }

    [Fact]
    public void ExportarDeudoresCsv_IncludesThePeriodoConsultadoHeader()
    {
        (ReportesController controller, _, _) = CreateController();

        var result = Assert.IsType<FileContentResult>(controller.ExportarDeudoresCsv());

        Assert.Contains("Período consultado", DecodeCsv(result));
    }

    [Fact]
    public void ExportarDeudoresCsv_WithoutACategoryFilter_LabelsItAsTodas()
    {
        (ReportesController controller, _, _) = CreateController();

        var result = Assert.IsType<FileContentResult>(controller.ExportarDeudoresCsv(idCategoria: null));

        Assert.Contains("Todas", DecodeCsv(result));
    }

    [Fact]
    public void ExportarDeudoresCsv_ResolvesTheCategoryNameFromTheCatalog_EvenWithNoDebtorsToday()
    {
        // QA (24/09): filtrar una categoría sin deudores hoy hacía que el nombre saliera
        // como "Categoría #N" porque antes se leía de la propia fila del padrón (vacía en
        // ese caso). Ahora se busca en el catálogo de categorías, así que resuelve igual.
        (ReportesController controller, FakePagosService pagosService, FakeCategoriasDao categoriasDao) = CreateController();
        pagosService.PendientesResult = new List<PendienteJugador>(); // sin deudores hoy
        categoriasDao.Result = new List<Categoria> { new() { IdCategoria = 9, Nombre = "AFA 20112" } };

        var result = Assert.IsType<FileContentResult>(controller.ExportarDeudoresCsv(idCategoria: 9));

        Assert.Contains("AFA 20112", DecodeCsv(result));
        Assert.DoesNotContain("Categoría #9", DecodeCsv(result));
    }

    [Fact]
    public void ExportarDeudoresCsv_WithACategoryNotInTheCatalog_FallsBackToAPlaceholderLabel()
    {
        (ReportesController controller, _, FakeCategoriasDao categoriasDao) = CreateController();
        categoriasDao.Result = new List<Categoria>();

        var result = Assert.IsType<FileContentResult>(controller.ExportarDeudoresCsv(idCategoria: 42));

        Assert.Contains("Categoría #42", DecodeCsv(result));
    }

    [Fact]
    public void ExportarDeudoresPdf_AsksTheServiceForTheGivenCategoryFilter()
    {
        (ReportesController controller, FakePagosService pagosService, _) = CreateController();

        var result = controller.ExportarDeudoresPdf(idCategoria: 5);

        Assert.Equal(new int?[] { 5 }, pagosService.PendientesCalls);
        Assert.IsType<FileContentResult>(result);
    }

    [Fact]
    public void Controller_RequiresAnAuthenticatedUser()
    {
        Assert.NotNull(typeof(ReportesController).GetCustomAttribute<AuthorizeAttribute>());
    }

    // ---- Fakes ----

    private class FakePagosService : IPagosService
    {
        public IReadOnlyList<PendienteJugador> PendientesResult { get; set; } = new List<PendienteJugador> { Deudor(1, "AFA 20067") };
        public List<int?> PendientesCalls { get; } = new();

        public IReadOnlyList<PendienteJugador> ObtenerPendientes(int? idCategoria = null)
        {
            PendientesCalls.Add(idCategoria);
            return PendientesResult;
        }

        public RegistrarPagoResultado RegistrarPago(RegistrarPagoRequest request) => throw new NotSupportedException();
        public CobrarPagosPendientesResultado CobrarPagosPendientes(CobrarPagosPendientesRequest request) => throw new NotSupportedException();
        public IReadOnlyList<CategoriaDeuda> ObtenerDeudaPorCategoria(int anio, int? mes = null) => throw new NotSupportedException();
        public IReadOnlyList<PlayerAccount> GetPlayerAccounts(bool onlyDebtors) => throw new NotSupportedException();
        public IReadOnlyList<CuotaPendienteDetalle> ObtenerDeudaDetalle(int idJugador) => throw new NotSupportedException();
        public IReadOnlyList<CuotaJugador> ObtenerCuotasJugador(int idJugador) => throw new NotSupportedException();
        public PlayerStatement? GetPlayerStatement(int playerId) => throw new NotSupportedException();
        public IReadOnlyList<PagoReciente> ObtenerUltimosPagos(int top) => throw new NotSupportedException();
        public IReadOnlyList<PagoReciente> ObtenerTodosLosPagos() => throw new NotSupportedException();
        public ResumenPagos ObtenerResumen() => throw new NotSupportedException();
        public ResumenPagosHoy ObtenerResumenHoy() => throw new NotSupportedException();
    }

    private class FakeCategoriasDao : ICategoriasDao
    {
        public IReadOnlyList<Categoria> Result { get; set; } = new List<Categoria> { new() { IdCategoria = 1, Nombre = "AFA 20067" } };

        public IReadOnlyList<Categoria> ObtenerTodas() => Result;
    }

    private class FakeArancelesService : IArancelesService
    {
        public IReadOnlyList<ArancelHistorialItem> ObtenerHistorial() => new List<ArancelHistorialItem>();
        public ArancelResumen ObtenerResumen() => throw new NotSupportedException();
        public ProgramarArancelResultado ProgramarArancel(ProgramarArancelRequest request) => throw new NotSupportedException();
        public CancelarArancelResultado CancelarArancel(int idArancel) => throw new NotSupportedException();
    }
}
