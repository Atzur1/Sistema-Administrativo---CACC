using DaoLibrary;
using EntityLibrary;
using Microsoft.Data.SqlClient;
namespace ApiGestion.Tests;

[Collection(BaseDeDatosCollection.Nombre)]
public class ReportesDaoIntegrationTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static string Conexion => Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable)!;
    [IntegracionFact]
    public void TodosLosReportes_ConteoCoincideConSeleccionRealDelDatasetEstable()
    {
        var dao = new ReportesDao(Conexion);
        foreach (var tipo in new[] { "deudores", "pagos-recientes", "becados", "aranceles", "recaudacion" })
            output.WriteLine($"{tipo}: {dao.Contar(tipo, null, 500, 2026, 9)} filas");
        Assert.Equal(new PagosDao(Conexion).ObtenerPendientesAgrupados().Count, dao.Contar("deudores", null, 500, 0, 0));
        Assert.Equal(new PagosDao(Conexion).ObtenerUltimosPagos(500).Count, dao.Contar("pagos-recientes", null, 500, 0, 0));
        Assert.Equal(new DiscountDao(Conexion).GetAllDiscounts().Count, dao.Contar("becados", null, 500, 0, 0));
        Assert.Equal(new ArancelesDao(Conexion).ObtenerHistorial().Count, dao.Contar("aranceles", null, 500, 0, 0));
        Assert.Equal(new RecaudacionDao(Conexion).ObtenerDelPeriodo(2026, 9, null).Detalle.Count, dao.Contar("recaudacion", null, 500, 2026, 9));
    }
    [IntegracionFact]
    public void FixtureCeroUnaVariasFilas_Filtros_ConteoNoEscribeHistorial()
    {
        using var db = new SqlConnection(Conexion); db.Open();
        using var tx = db.BeginTransaction();
        try
        {
            int historial = ReportesDao.HistorialCore(db, tx, 1, 20).Total;
            int Contar(int mes, int? categoria = null) => ReportesDao.ContarCore(db, tx, "recaudacion", categoria, 500, 2098, mes);
            Assert.Equal(0, Contar(6));
            using (var cmd = new SqlCommand(@"INSERT INTO PAGOS (FK_id_jugador,monto_base,monto_final,fecha_pago,metodo_pago,fecha_vencimiento,estado,concepto,fecha_hora_registro)
    SELECT TOP(1) PK_id_jugador,100,100,'20980615','Efectivo','20980601',1,'Cuota','20980615 12:00' FROM JUGADORES;", db, tx)) Assert.Equal(1, cmd.ExecuteNonQuery());
            Assert.Equal(1, Contar(6));
            using (var cmd = new SqlCommand(@"INSERT INTO PAGOS (FK_id_jugador,monto_base,monto_final,fecha_pago,metodo_pago,fecha_vencimiento,estado,concepto,fecha_hora_registro)
    SELECT TOP(1) PK_id_jugador,200,200,'20980616','Transferencia','20980601',1,'Cuota','20980616 12:00' FROM JUGADORES;
    INSERT INTO PAGOS (FK_id_jugador,monto_base,monto_final,fecha_pago,metodo_pago,fecha_vencimiento,estado,concepto,fecha_hora_registro)
    SELECT TOP(1) PK_id_jugador,300,300,'20980617','Efectivo','20980601',1,'Cuota','20980617 12:00' FROM JUGADORES;", db, tx)) Assert.Equal(2, cmd.ExecuteNonQuery());
            Assert.Equal(3, Contar(6));
            Assert.Equal(0, Contar(7));
            Assert.Equal(0, Contar(6, int.MaxValue));
            Assert.Equal(3, RecaudacionDao.ObtenerDetalleCore(db, tx, 2098, 6, null).Count);
            Assert.Equal(historial, ReportesDao.HistorialCore(db, tx, 1, 20).Total);
        }
        finally { tx.Rollback(); }
    }
    [IntegracionFact]
    public void Historial_PersisteMetadatosResuelvePersonaYPaginaConDesempatePorId()
    {
        using var db = new SqlConnection(Conexion); db.Open(); using var tx = db.BeginTransaction();
        try
        {
            using var actor = new SqlCommand("SELECT TOP(1) u.PK_id_usuario,LTRIM(RTRIM(ISNULL(p.apellido,''))) + ', ' + LTRIM(RTRIM(ISNULL(p.nombre,''))) FROM USUARIOS u JOIN PERSONAS p ON p.PK_id_persona=u.FK_id_persona ORDER BY u.PK_id_usuario", db, tx);
            int id; string persona;
            using (var reader = actor.ExecuteReader()) { Assert.True(reader.Read()); id = reader.GetInt32(0); persona = reader.GetString(1); }
            var fecha = new DateTime(2099, 10, 31, 12, 30, 0);
            foreach (var formato in new[] { "pdf", "csv" }) ReportesDao.RegistrarCore(db, tx, new ExportacionReporte { UsuarioId = id, Persona = "suplantado", FechaHora = fecha, Tipo = "recaudacion", Formato = formato, Filtros = "Período: octubre 2099; Categoría: Todas", CantidadFilas = 3 });
            var primera = ReportesDao.HistorialCore(db, tx, 1, 1); var segunda = ReportesDao.HistorialCore(db, tx, 2, 1);
            var a = Assert.Single(primera.Items); var b = Assert.Single(segunda.Items);
            Assert.Equal("csv", a.Formato); Assert.Equal("pdf", b.Formato); Assert.True(a.Id > b.Id);
            Assert.Equal(fecha, a.FechaHora); Assert.Equal(persona, a.Persona); Assert.Equal(id, a.UsuarioId); Assert.Equal(3, a.CantidadFilas);
            Assert.Equal("recaudacion", a.Tipo); Assert.Equal("Período: octubre 2099; Categoría: Todas", a.Filtros);
            using var schema = new SqlCommand("SELECT name FROM sys.columns WHERE object_id=OBJECT_ID('dbo.EXPORTACIONES_REPORTES')", db, tx);
            var nombres = new List<string>(); using (var reader = schema.ExecuteReader()) while (reader.Read()) nombres.Add(reader.GetString(0));
            Assert.Equal(new[] { "id", "fecha_hora", "usuario_id", "persona", "tipo", "formato", "filtros", "cantidad_filas" }, nombres);
        }
        finally { tx.Rollback(); }
    }
    [IntegracionFact]
    public void ExportadoresReales_CincoReportesDosFormatos_PersistenHistorialAntesDeResponder()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        using var db = new SqlConnection(Conexion);
        db.Open();
        using var tx = db.BeginTransaction();
        try
        {
            using var actor = new SqlCommand("SELECT TOP(1) PK_id_usuario FROM USUARIOS ORDER BY PK_id_usuario", db, tx);
            int id = Convert.ToInt32(actor.ExecuteScalar());
            var historial = new HistorialEnTransaccion(db, tx);
            var controller = new ApiGestion.Controllers.ReportesController(
                new ServiceLibrary.PagosService(new PagosDao(Conexion), new PaymentTransactionDao(Conexion)),
                new ServiceLibrary.ArancelesService(new ArancelesDao(Conexion), new TariffTransactionDao(Conexion), new CategoriasDao(Conexion)),
                new DiscountDao(Conexion), new CategoriasDao(Conexion), new RecaudacionDao(Conexion), historial);
            controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
                {
                    User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[] {
                        new System.Security.Claims.Claim("idUsuario", id.ToString()),
                        new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "1")
                    }, "fixture"))
                }
            };
            int antes = historial.Historial(1, 100).Total;
            var exportaciones = new (string Tipo, string Formato, Func<Microsoft.AspNetCore.Mvc.IActionResult> Generar)[]
            {
                ("deudores", "csv", () => controller.ExportarDeudoresCsv()),
                ("deudores", "pdf", () => controller.ExportarDeudoresPdf()),
                ("pagos-recientes", "csv", () => controller.ExportarPagosRecientesCsv()),
                ("pagos-recientes", "pdf", () => controller.ExportarPagosRecientesPdf()),
                ("becados", "csv", () => controller.ExportarBecadosCsv()),
                ("becados", "pdf", () => controller.ExportarBecadosPdf()),
                ("aranceles", "csv", () => controller.ExportarArancelesCsv()),
                ("aranceles", "pdf", () => controller.ExportarArancelesPdf()),
                ("recaudacion", "csv", () => controller.ExportarRecaudacionCsv(2026, 9)),
                ("recaudacion", "pdf", () => controller.ExportarRecaudacionPdf(2026, 9)),
                ("recaudacion", "csv", () => controller.ExportarRecaudacionCsv(2098, 7)),
                ("recaudacion", "pdf", () => controller.ExportarRecaudacionPdf(2098, 7))
            };
            int generadas = 0;
            foreach (var exportacion in exportaciones)
            {
                int anio = generadas >= 10 ? 2098 : 2026;
                int mes = generadas >= 10 ? 7 : 9;
                int cantidad = historial.Contar(exportacion.Tipo, null, 500, anio, mes);
                var file = Assert.IsType<Microsoft.AspNetCore.Mvc.FileContentResult>(exportacion.Generar());
                Assert.NotEmpty(file.FileContents);
                if (exportacion.Formato == "pdf") Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(file.FileContents, 0, 4));
                var pagina = historial.Historial(1, 1);
                var registro = Assert.Single(pagina.Items);
                Assert.Equal(antes + ++generadas, pagina.Total);
                Assert.Equal(cantidad, registro.CantidadFilas);
                Assert.Equal(exportacion.Tipo, registro.Tipo);
                Assert.Equal(exportacion.Formato, registro.Formato);
                Assert.Equal(id, registro.UsuarioId);
                Assert.False(string.IsNullOrWhiteSpace(registro.Persona));
                output.WriteLine($"{exportacion.Tipo}/{exportacion.Formato}: preview={cantidad}, real={registro.CantidadFilas}, usuario={registro.UsuarioId}, persona={registro.Persona}, fecha={registro.FechaHora:O}, bytes={file.FileContents.Length}");
                var carpeta = Environment.GetEnvironmentVariable("CACC_QA_OUTPUT");
                if (!string.IsNullOrWhiteSpace(carpeta))
                {
                    Directory.CreateDirectory(carpeta);
                    File.WriteAllBytes(Path.Combine(carpeta, $"{exportacion.Tipo}-{anio}-{mes}.{exportacion.Formato}"), file.FileContents);
                }
            }
        }
        finally { tx.Rollback(); }
    }

    private sealed class HistorialEnTransaccion(SqlConnection db, SqlTransaction tx) : IReportesDao
    {
        public int Contar(string tipo, int? categoria, int top, int anio, int mes)
            => ReportesDao.ContarCore(db, tx, tipo, categoria, top, anio, mes);
        public void Registrar(ExportacionReporte exportacion) => ReportesDao.RegistrarCore(db, tx, exportacion);
        public HistorialExportacionesPagina Historial(int pagina, int tamanioPagina)
            => ReportesDao.HistorialCore(db, tx, pagina, tamanioPagina);
    }

}
