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
            categoriasDao,
            new FakeRecaudacionDao(), new FakeReportesDao());
        controller.ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[] { new System.Security.Claims.Claim("idUsuario", "7"), new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "1") }, "test")) } };
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

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(57)]
    public void Conteo_ConsultaSinExportarNiRegistrar(int cantidad)
    {
        var (controller, pagos, _) = CreateController();
        var dao = new FakeReportesDao { Cantidad = cantidad };
        // Reutilizamos los fakes existentes pero no llamamos ninguna consulta de exportación.
        controller = new ReportesController(pagos, new FakeArancelesService(), new DiscountDao(""), new FakeCategoriasDao(), new FakeRecaudacionDao(), dao) { ControllerContext = controller.ControllerContext };
        var result = Assert.IsType<OkObjectResult>(controller.Conteo("deudores", 9));
        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        Assert.Contains($"\"cantidadFilas\":{cantidad}", json);
        Assert.Equal(("deudores", (int?)9, 500, 0, 0), Assert.Single(dao.Conteos));
        Assert.Empty(pagos.PendientesCalls);
        Assert.Empty(dao.Exportaciones);
    }

    [Fact]
    public void Exportacion_RegistraCantidadRealActorFiltrosFormatoYHora()
    {
        var (controller, pagos, categorias) = CreateController();
        var dao = new FakeReportesDao { Cantidad = 999 };
        controller = new ReportesController(pagos, new FakeArancelesService(), new DiscountDao(""), categorias, new FakeRecaudacionDao(), dao) { ControllerContext = controller.ControllerContext };
        var antes = RelojNegocio.Ahora;
        Assert.IsType<FileContentResult>(controller.ExportarDeudoresCsv(1));
        var registro = Assert.Single(dao.Exportaciones);
        Assert.Equal(1, registro.CantidadFilas); // Nunca reutiliza el preview 999.
        Assert.Equal(7, registro.UsuarioId);
        Assert.Equal("deudores", registro.Tipo);
        Assert.Equal("csv", registro.Formato);
        Assert.Contains("AFA 20067", registro.Filtros);
        Assert.InRange(registro.FechaHora, antes, RelojNegocio.Ahora);
    }

    [Fact]
    public void FallaAlPersistirHistorial_NoRespondeArchivoExitoso()
    {
        var (controller, pagos, categorias) = CreateController();
        var dao = new FakeReportesDao { FallarRegistro = true };
        controller = new ReportesController(pagos, new FakeArancelesService(), new DiscountDao(""), categorias, new FakeRecaudacionDao(), dao) { ControllerContext = controller.ControllerContext };
        Assert.Throws<InvalidOperationException>(() => controller.ExportarDeudoresCsv());
    }

    [Fact]
    public void CambioDeFiltros_ConsultaNuevaSeleccion()
    {
        var (controller, pagos, categorias) = CreateController();
        var dao = new FakeReportesDao();
        controller = new ReportesController(pagos, new FakeArancelesService(), new DiscountDao(""), categorias, new FakeRecaudacionDao(), dao) { ControllerContext = controller.ControllerContext };
        controller.Conteo("deudores", 1);
        controller.Conteo("deudores", 2);
        Assert.Equal(new int?[] { 1, 2 }, dao.Conteos.Select(c => c.Item2));
    }

    [Theory]
    [InlineData("aranceles")]
    [InlineData("recaudacion")]
    public void Conteo_AdministradorNoConsultaReportesExclusivos(string tipo)
    {
        var (controller, _, _) = CreateController();
        controller.HttpContext.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "2") }, "test"));
        Assert.IsType<ForbidResult>(controller.Conteo(tipo, anio: 2026, mes: 10));
    }

    [Theory]
    [InlineData("deudores")]
    [InlineData("pagos-recientes")]
    [InlineData("becados")]
    [InlineData("aranceles")]
    [InlineData("recaudacion")]
    public void Conteo_TodosLosReportes_UsanLaOperacionDeConteo(string tipo)
    {
        var (original, pagos, categorias) = CreateController();
        var dao = new FakeReportesDao();
        var controller = new ReportesController(pagos, new FakeArancelesService(), new DiscountDao(""), categorias, new FakeRecaudacionDao(), dao) { ControllerContext = original.ControllerContext };
        Assert.IsType<OkObjectResult>(controller.Conteo(tipo, 2, 1001, 2026, 10));
        Assert.Equal((tipo, (int?)2, 1000, 2026, 10), Assert.Single(dao.Conteos));
        Assert.Empty(dao.Exportaciones);
    }

    [Fact]
    public void Conteo_PeriodoInvalido_SeRechazaSinConsultar()
    {
        var (controller, _, _) = CreateController();
        Assert.IsType<BadRequestObjectResult>(controller.Conteo("recaudacion", anio: 2026, mes: 13));
    }

    [Fact]
    public void Exportacion_SinActor_NoRegistra()
    {
        var (controller, _, _) = CreateController();
        controller.HttpContext.User = new System.Security.Claims.ClaimsPrincipal();
        Assert.Throws<UnauthorizedAccessException>(() => controller.ExportarDeudoresCsv());
    }

    [Fact]
    public void Exportacion_ConsultaFallida_NoRegistra()
    {
        var (original, pagos, categorias) = CreateController();
        var dao = new FakeReportesDao();
        pagos.FallarConsulta = true;
        var controller = new ReportesController(pagos, new FakeArancelesService(), new DiscountDao(""), categorias, new FakeRecaudacionDao(), dao) { ControllerContext = original.ControllerContext };
        Assert.Throws<InvalidOperationException>(() => controller.ExportarDeudoresCsv());
        Assert.Empty(dao.Exportaciones);
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("csv")]
    public void Exportacion_Recaudacion_RegistraCantidadDelDetalle(string formato)
    {
        var (original, pagos, categorias) = CreateController();
        var dao = new FakeReportesDao();
        var controller = new ReportesController(pagos, new FakeArancelesService(), new DiscountDao(""), categorias, new FakeRecaudacionDao(), dao) { ControllerContext = original.ControllerContext };
        Assert.IsType<FileContentResult>(formato == "pdf" ? controller.ExportarRecaudacionPdf(2026, 10, 1) : controller.ExportarRecaudacionCsv(2026, 10, 1));
        var registro = Assert.Single(dao.Exportaciones);
        Assert.Equal("recaudacion", registro.Tipo);
        Assert.Equal(formato, registro.Formato);
        Assert.Contains("2026", registro.Filtros);
        Assert.Equal(0, registro.CantidadFilas);
    }

    [Fact]
    public void Exportacion_GeneracionFallida_NoRegistraHistorial()
    {
        var (original, pagos, categorias) = CreateController();
        var dao = new FakeReportesDao();
        // La consulta retorna un resultado inválido: el builder real falla al generar el CSV.
        pagos.PendientesResult = null!;
        var controller = new ReportesController(pagos, new FakeArancelesService(), new DiscountDao(""), categorias, new FakeRecaudacionDao(), dao)
        {
            ControllerContext = original.ControllerContext
        };
        Assert.ThrowsAny<Exception>(() => controller.ExportarDeudoresCsv());
        Assert.Empty(dao.Exportaciones);
    }

    // ---- Fakes ----

    private class FakePagosService : IPagosService
    {
        public AnularPagoResultado AnularPago(AnularPagoRequest request) => throw new NotSupportedException();
        public IReadOnlyList<PendienteJugador> PendientesResult { get; set; } = new List<PendienteJugador> { Deudor(1, "AFA 20067") };
        public List<int?> PendientesCalls { get; } = new();
        public bool FallarConsulta { get; set; }

        public IReadOnlyList<PendienteJugador> ObtenerPendientes(int? idCategoria = null)
        {
            if (FallarConsulta) throw new InvalidOperationException("fixture falla consulta");
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
        public PagosRegistradosPagina ObtenerPagosRegistrados(FiltroPagosRegistrados filtro) => throw new NotSupportedException();
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
        public ImpactoArancel SimularArancel(CambioArancel cambio) => throw new NotSupportedException();
        public ProgramarArancelResultado ProgramarArancel(ProgramarArancelRequest request) => throw new NotSupportedException();
        public CancelarArancelResultado CancelarArancel(int idArancel) => throw new NotSupportedException();
    }

    private class FakeRecaudacionDao : IRecaudacionDao
    {
        public RecaudacionPeriodo ObtenerDelPeriodo(int anio, int mes, int? idCategoria) => new RecaudacionPeriodo();
        public RecaudacionPeriodo ObtenerTotalesDelPeriodo(int anio, int mes, int? idCategoria) => new RecaudacionPeriodo();
        public PagosDelPeriodoPagina ObtenerPagosDelPeriodo(FiltroPagosDelPeriodo filtro) => new PagosDelPeriodoPagina { TamanioPagina = 10 };
    }
}
