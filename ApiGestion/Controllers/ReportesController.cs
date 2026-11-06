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
    // Sin [Authorize] a nivel de clase: en ASP.NET Core, un [Authorize(Roles=...)]
    // de clase y otro de método se combinan con AND, no con OR. Poner Roles acá y
    // otro distinto en cada acción dejaría afuera al rol que no está en ambos.
    // Por eso cada acción declara explícitamente qué roles puede.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ReportesController : ControllerBase
    {
        private readonly IReportesDao _reportesDao;
        private readonly IPagosService _pagosService;
        private readonly IArancelesService _arancelesService;
        private readonly DiscountDao _discountDao;
        private readonly ICategoriasDao _categoriasDao;
        private readonly IRecaudacionDao _recaudacionDao;

        public ReportesController(
            IPagosService pagosService,
            IArancelesService arancelesService,
            DiscountDao discountDao,
            ICategoriasDao categoriasDao,
            IRecaudacionDao recaudacionDao,
            IReportesDao reportesDao)
        {
            _reportesDao = reportesDao;
            _pagosService = pagosService;
            _arancelesService = arancelesService;
            _discountDao = discountDao;
            _categoriasDao = categoriasDao;
            _recaudacionDao = recaudacionDao;
        }

        // GET api/reportes/deudores/pdf?idCategoria=5 -> HU-021: planilla imprimible
        [Authorize(Roles = "1,2")]
        [HttpGet("deudores/pdf")]
        public IActionResult ExportarDeudoresPdf([FromQuery] int? idCategoria = null)
        {
            var deudores = _pagosService.ObtenerPendientes(idCategoria);
            byte[] pdf = DeudoresReportBuilder.BuildPdf(deudores, CategoriaLabel(idCategoria), PeriodoLabel());
            Registrar("deudores", "pdf", FiltroCategoria(idCategoria), deudores.Count);
            return File(pdf, "application/pdf", $"reporte-deudores_{Timestamp()}.pdf");
        }

        // GET api/reportes/deudores/csv?idCategoria=5 -> HU-021: misma planilla en CSV
        [Authorize(Roles = "1,2")]
        [HttpGet("deudores/csv")]
        public IActionResult ExportarDeudoresCsv([FromQuery] int? idCategoria = null)
        {
            var deudores = _pagosService.ObtenerPendientes(idCategoria);
            byte[] csv = DeudoresReportBuilder.BuildCsv(deudores, CategoriaLabel(idCategoria), PeriodoLabel());
            Registrar("deudores", "csv", FiltroCategoria(idCategoria), deudores.Count);
            return File(csv, "text/csv", $"reporte-deudores_{Timestamp()}.csv");
        }

        // GET api/reportes/pagos-recientes/csv -> caja "Cuotas y Pagos" en Reportes
        [Authorize(Roles = "1,2")]
        [HttpGet("pagos-recientes/csv")]
        public IActionResult ExportarPagosRecientesCsv([FromQuery] int top = 500)
        {
            var pagos = _pagosService.ObtenerUltimosPagos(Math.Clamp(top, 1, 1000));
            byte[] csv = PagosRecientesReportBuilder.BuildCsv(pagos);
            Registrar("pagos-recientes", "csv", $"Últimos {Math.Clamp(top, 1, 1000)} pagos", pagos.Count);
            return File(csv, "text/csv", $"reporte-pagos_{Timestamp()}.csv");
        }

        [Authorize(Roles = "1,2")]
        [HttpGet("pagos-recientes/pdf")]
        public IActionResult ExportarPagosRecientesPdf([FromQuery] int top = 500)
        {
            var pagos = _pagosService.ObtenerUltimosPagos(Math.Clamp(top, 1, 1000));
            byte[] pdf = PagosRecientesReportBuilder.BuildPdf(pagos);
            Registrar("pagos-recientes", "pdf", $"Últimos {Math.Clamp(top, 1, 1000)} pagos", pagos.Count);
            return File(pdf, "application/pdf", $"reporte-pagos_{Timestamp()}.pdf");
        }

        // GET api/reportes/becados/csv -> caja "Becados y Descuentos" en Reportes
        [Authorize(Roles = "1,2")]
        [HttpGet("becados/csv")]
        public IActionResult ExportarBecadosCsv()
        {
            List<Discount> beneficios = _discountDao.GetAllDiscounts();
            byte[] csv = BecadosReportBuilder.BuildCsv(beneficios);
            Registrar("becados", "csv", "Sin filtros adicionales", beneficios.Count);
            return File(csv, "text/csv", $"reporte-becados_{Timestamp()}.csv");
        }

        [Authorize(Roles = "1,2")]
        [HttpGet("becados/pdf")]
        public IActionResult ExportarBecadosPdf()
        {
            List<Discount> beneficios = _discountDao.GetAllDiscounts();
            byte[] pdf = BecadosReportBuilder.BuildPdf(beneficios);
            Registrar("becados", "pdf", "Sin filtros adicionales", beneficios.Count);
            return File(pdf, "application/pdf", $"reporte-becados_{Timestamp()}.pdf");
        }

        // GET api/reportes/aranceles/csv -> caja "Actualización de Aranceles" en Reportes
        [Authorize(Roles = "1")]
        [HttpGet("aranceles/csv")]
        public IActionResult ExportarArancelesCsv()
        {
            var historial = _arancelesService.ObtenerHistorial();
            byte[] csv = ArancelesReportBuilder.BuildCsv(historial);
            Registrar("aranceles", "csv", "Sin filtros adicionales", historial.Count);
            return File(csv, "text/csv", $"reporte-aranceles_{Timestamp()}.csv");
        }

        [Authorize(Roles = "1")]
        [HttpGet("aranceles/pdf")]
        public IActionResult ExportarArancelesPdf()
        {
            var historial = _arancelesService.ObtenerHistorial();
            byte[] pdf = ArancelesReportBuilder.BuildPdf(historial);
            Registrar("aranceles", "pdf", "Sin filtros adicionales", historial.Count);
            return File(pdf, "application/pdf", $"reporte-aranceles_{Timestamp()}.pdf");
        }

        // GET api/reportes/recaudacion/pdf?anio=2026&mes=9&idCategoria=5 -> HU-018
        // Solo rol 1, igual que el panel de Recaudación Mensual que lo origina.
        [Authorize(Roles = "1")]
        [HttpGet("recaudacion/pdf")]
        public IActionResult ExportarRecaudacionPdf(
            [FromQuery] int anio, [FromQuery] int mes, [FromQuery] int? idCategoria = null)
        {
            if (!PeriodoValido(anio, mes, out string? error))
            {
                return BadRequest(new { exito = false, mensaje = error });
            }

            var datos = _recaudacionDao.ObtenerDelPeriodo(anio, mes, idCategoria);
            byte[] pdf = RecaudacionReportBuilder.BuildPdf(
                datos, PeriodoMesLabel(anio, mes), CategoriaLabel(idCategoria));
            Registrar("recaudacion", "pdf", $"Período: {PeriodoMesLabel(anio, mes)}; {FiltroCategoria(idCategoria)}", datos.Detalle.Count);
            return File(pdf, "application/pdf", $"reporte-recaudacion_{Timestamp()}.pdf");
        }

        // GET api/reportes/recaudacion/csv?anio=2026&mes=9&idCategoria=5 -> misma planilla en CSV
        [Authorize(Roles = "1")]
        [HttpGet("recaudacion/csv")]
        public IActionResult ExportarRecaudacionCsv(
            [FromQuery] int anio, [FromQuery] int mes, [FromQuery] int? idCategoria = null)
        {
            if (!PeriodoValido(anio, mes, out string? error))
            {
                return BadRequest(new { exito = false, mensaje = error });
            }

            var datos = _recaudacionDao.ObtenerDelPeriodo(anio, mes, idCategoria);
            byte[] csv = RecaudacionReportBuilder.BuildCsv(
                datos, PeriodoMesLabel(anio, mes), CategoriaLabel(idCategoria));
            Registrar("recaudacion", "csv", $"Período: {PeriodoMesLabel(anio, mes)}; {FiltroCategoria(idCategoria)}", datos.Detalle.Count);
            return File(csv, "text/csv", $"reporte-recaudacion_{Timestamp()}.csv");
        }

        [Authorize(Roles = "1,2")]
        [HttpGet("{tipo}/conteo")]
        public IActionResult Conteo(string tipo, [FromQuery] int? idCategoria = null,
            [FromQuery] int top = 500, [FromQuery] int anio = 0, [FromQuery] int mes = 0)
        {
            if (tipo is not ("deudores" or "pagos-recientes" or "becados" or "aranceles" or "recaudacion"))
                return NotFound();
            if (tipo is "aranceles" or "recaudacion" && !User.IsInRole("1")) return Forbid();
            if (tipo == "recaudacion" && !PeriodoValido(anio, mes, out var error))
                return BadRequest(new { exito = false, mensaje = error });
            return Ok(new { tipo, cantidadFilas = _reportesDao.Contar(tipo, idCategoria, Math.Clamp(top, 1, 1000), anio, mes) });
        }

        [Authorize(Roles = "1")]
        [HttpGet("historial")]
        public IActionResult Historial([FromQuery] int pagina = 1, [FromQuery] int tamanioPagina = 20)
        {
            if (pagina < 1 || pagina > 1000000 || tamanioPagina < 1 || tamanioPagina > 100)
                return BadRequest(new { exito = false, mensaje = "La página o el tamaño de página no es válido." });
            return Ok(_reportesDao.Historial(pagina, tamanioPagina));
        }

        private string FiltroCategoria(int? categoria) => $"Categoría: {CategoriaLabel(categoria)}";

        private void Registrar(string tipo, string formato, string filtros, int filas)
        {
            if (!int.TryParse(User.FindFirst("idUsuario")?.Value, out var usuarioId) || usuarioId <= 0)
                throw new UnauthorizedAccessException("Falta la identidad autenticada de quien exporta.");
            _reportesDao.Registrar(new ExportacionReporte {
                UsuarioId = usuarioId, FechaHora = RelojNegocio.Ahora, Tipo = tipo,
                Formato = formato, Filtros = filtros, CantidadFilas = filas
            });
        }

        private static bool PeriodoValido(int anio, int mes, out string? error)
        {
            if (mes < 1 || mes > 12)
            {
                error = "El mes debe estar entre 1 y 12.";
                return false;
            }
            if (anio < 2000 || anio > 2100)
            {
                error = "El año no es válido.";
                return false;
            }
            error = null;
            return true;
        }

        // "Septiembre 2026", con el nombre del mes en es-AR y la inicial en mayúscula.
        private static string PeriodoMesLabel(int anio, int mes)
        {
            var cultura = System.Globalization.CultureInfo.GetCultureInfo("es-AR");
            string nombre = cultura.DateTimeFormat.GetMonthName(mes);
            return $"{char.ToUpper(nombre[0], cultura)}{nombre[1..]} {anio}";
        }

        private static string Timestamp() => RelojNegocio.Ahora.ToString("yyyyMMdd_HHmm");

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
        private static string PeriodoLabel() => $"Temporada {RelojNegocio.Ahora.Year}, hasta {FormatoVisible.Fecha(RelojNegocio.Ahora)}";
    }
}
