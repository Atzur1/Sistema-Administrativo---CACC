using ApiGestion.Reports;
using DaoLibrary;
using EntityLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServiceLibrary;

namespace ApiGestion.Controllers
{
    // HU-021 (reporte de deudores, PDF y CSV) y las exportaciones CSV de la
    // pantalla "Reportes" (una caja por pantalla de Gestión). Todos devuelven el
    // archivo como stream de bytes con Content-Disposition: attachment, así el
    // navegador arranca la descarga solo, sin recargar la página.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ReportesController : ControllerBase
    {
        private readonly IPagosService _pagosService;
        private readonly IArancelesService _arancelesService;
        private readonly DiscountDao _discountDao;
        private readonly ICategoriasDao _categoriasDao;

        public ReportesController(
            IPagosService pagosService,
            IArancelesService arancelesService,
            DiscountDao discountDao,
            ICategoriasDao categoriasDao)
        {
            _pagosService = pagosService;
            _arancelesService = arancelesService;
            _discountDao = discountDao;
            _categoriasDao = categoriasDao;
        }

        // GET api/reportes/deudores/pdf?idCategoria=5 -> HU-021: planilla imprimible
        [HttpGet("deudores/pdf")]
        public IActionResult ExportarDeudoresPdf([FromQuery] int? idCategoria = null)
        {
            var deudores = _pagosService.ObtenerPendientes(idCategoria);
            byte[] pdf = DeudoresReportBuilder.BuildPdf(deudores, CategoriaLabel(idCategoria), PeriodoLabel());
            return File(pdf, "application/pdf", $"reporte-deudores_{Timestamp()}.pdf");
        }

        // GET api/reportes/deudores/csv?idCategoria=5 -> HU-021: misma planilla en CSV
        [HttpGet("deudores/csv")]
        public IActionResult ExportarDeudoresCsv([FromQuery] int? idCategoria = null)
        {
            var deudores = _pagosService.ObtenerPendientes(idCategoria);
            byte[] csv = DeudoresReportBuilder.BuildCsv(deudores, CategoriaLabel(idCategoria), PeriodoLabel());
            return File(csv, "text/csv", $"reporte-deudores_{Timestamp()}.csv");
        }

        // GET api/reportes/pagos-recientes/csv -> caja "Cuotas y Pagos" en Reportes
        [HttpGet("pagos-recientes/csv")]
        public IActionResult ExportarPagosRecientesCsv([FromQuery] int top = 500)
        {
            var pagos = _pagosService.ObtenerUltimosPagos(top);
            byte[] csv = PagosRecientesReportBuilder.BuildCsv(pagos);
            return File(csv, "text/csv", $"reporte-pagos_{Timestamp()}.csv");
        }

        [HttpGet("pagos-recientes/pdf")]
        public IActionResult ExportarPagosRecientesPdf([FromQuery] int top = 500)
        {
            var pagos = _pagosService.ObtenerUltimosPagos(top);
            byte[] pdf = PagosRecientesReportBuilder.BuildPdf(pagos);
            return File(pdf, "application/pdf", $"reporte-pagos_{Timestamp()}.pdf");
        }

        // GET api/reportes/becados/csv -> caja "Becados y Descuentos" en Reportes
        [HttpGet("becados/csv")]
        public IActionResult ExportarBecadosCsv()
        {
            List<Discount> beneficios = _discountDao.GetAllDiscounts();
            byte[] csv = BecadosReportBuilder.BuildCsv(beneficios);
            return File(csv, "text/csv", $"reporte-becados_{Timestamp()}.csv");
        }

        [HttpGet("becados/pdf")]
        public IActionResult ExportarBecadosPdf()
        {
            List<Discount> beneficios = _discountDao.GetAllDiscounts();
            byte[] pdf = BecadosReportBuilder.BuildPdf(beneficios);
            return File(pdf, "application/pdf", $"reporte-becados_{Timestamp()}.pdf");
        }

        // GET api/reportes/aranceles/csv -> caja "Actualización de Aranceles" en Reportes
        [HttpGet("aranceles/csv")]
        public IActionResult ExportarArancelesCsv()
        {
            var historial = _arancelesService.ObtenerHistorial();
            byte[] csv = ArancelesReportBuilder.BuildCsv(historial);
            return File(csv, "text/csv", $"reporte-aranceles_{Timestamp()}.csv");
        }

        [HttpGet("aranceles/pdf")]
        public IActionResult ExportarArancelesPdf()
        {
            var historial = _arancelesService.ObtenerHistorial();
            byte[] pdf = ArancelesReportBuilder.BuildPdf(historial);
            return File(pdf, "application/pdf", $"reporte-aranceles_{Timestamp()}.pdf");
        }

        private static string Timestamp() => DateTime.Now.ToString("yyyyMMdd_HHmm");

        // QA (24/09, HU-021): con una categoría filtrada sin deudores hoy, el nombre
        // salía como "Categoría #N" porque se leía de la propia fila del padrón (que
        // queda vacía en ese caso). Se busca en el catálogo de categorías en vez de
        // inferirlo de los deudores, así el nombre siempre es el real.
        private string CategoriaLabel(int? idCategoria)
        {
            if (idCategoria == null)
            {
                return "Todas";
            }
            return _categoriasDao.ObtenerTodas()
                .FirstOrDefault(c => c.IdCategoria == idCategoria)?.Nombre
                ?? $"Categoría #{idCategoria}";
        }

        // HU-021 (QA, 24/09): "Período consultado" faltaba en el encabezado. El
        // padrón de deudores no está acotado a un período (es la deuda pendiente de
        // HOY, no de un mes puntual), así que se informa como la temporada vigente
        // hasta la fecha de emisión — mismo criterio que sugirió QA en la revisión.
        private static string PeriodoLabel() => $"Temporada {DateTime.Now.Year}, hasta {DateTime.Now:dd/MM/yyyy}";
    }
}
