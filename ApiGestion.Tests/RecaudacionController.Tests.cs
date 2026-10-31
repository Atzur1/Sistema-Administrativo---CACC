namespace ApiGestion.Tests;

using System.Text.Json;
using ApiGestion.Controllers;
using ApiGestion.Models;
using DaoLibrary;
using EntityLibrary;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

// HU-107: tabla "Pagos del período" paginada en el servidor y tarjetas del período completo.
public class RecaudacionControllerTests
{
    private readonly FakeRecaudacionDao _dao = new();

    private RecaudacionController Controller() => new(_dao, NullLogger<RecaudacionController>.Instance);

    [Fact]
    public void Pagos_SinParametros_UsaLosDefaultsDeHu088_FechaDescendente()
    {
        var resultado = Controller().ObtenerPagosDelPeriodo(2026, 10);

        Assert.IsType<OkObjectResult>(resultado);
        var filtro = Assert.Single(_dao.Filtros);
        Assert.Equal(1, filtro.Pagina);
        Assert.Equal(10, filtro.TamanioPagina);
        Assert.Equal(OrdenPagosDelPeriodo.Fecha, filtro.OrdenarPor);
        Assert.True(filtro.Descendente);
    }

    [Theory]
    [InlineData("fecha", "asc", OrdenPagosDelPeriodo.Fecha, false)]
    [InlineData("fecha", "desc", OrdenPagosDelPeriodo.Fecha, true)]
    [InlineData("monto", "asc", OrdenPagosDelPeriodo.Monto, false)]
    [InlineData("MONTO", "DESC", OrdenPagosDelPeriodo.Monto, true)]
    public void Pagos_TraduceElOrdenAlEnum(string sortBy, string sortDirection, OrdenPagosDelPeriodo orden, bool descendente)
    {
        Controller().ObtenerPagosDelPeriodo(2026, 10, idCategoria: 5, page: 3, pageSize: 25, sortBy: sortBy, sortDirection: sortDirection);

        var filtro = Assert.Single(_dao.Filtros);
        Assert.Equal(orden, filtro.OrdenarPor);
        Assert.Equal(descendente, filtro.Descendente);
        Assert.Equal(3, filtro.Pagina);
        Assert.Equal(25, filtro.TamanioPagina);
        Assert.Equal(5, filtro.IdCategoria);
        Assert.Equal((2026, 10), (filtro.Anio, filtro.Mes));
    }

    [Theory]
    [InlineData(int.MaxValue, 100, "fecha", "desc")]
    [InlineData(0, 10, "fecha", "desc")]
    [InlineData(-1, 10, "fecha", "desc")]
    [InlineData(1, 0, "fecha", "desc")]
    [InlineData(1, 101, "fecha", "desc")]
    [InlineData(1, 10, "PK_id_pago", "desc")]
    [InlineData(1, 10, "fecha_pago; DROP TABLE PAGOS--", "desc")]
    [InlineData(1, 10, "fecha", "arriba")]
    [InlineData(1, 10, "fecha", "desc, PK_id_pago")]
    public void Pagos_ParametrosInvalidos_Responden400_SinConsultar(int page, int pageSize, string sortBy, string sortDirection)
    {
        var resultado = Controller().ObtenerPagosDelPeriodo(2026, 10, null, page, pageSize, sortBy, sortDirection);

        Assert.IsType<BadRequestObjectResult>(resultado);
        Assert.Empty(_dao.Filtros);
    }

    [Theory]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    [InlineData(1999, 5)]
    public void PeriodoInvalido_Responde400_EnLosDosEndpoints(int anio, int mes)
    {
        Assert.IsType<BadRequestObjectResult>(Controller().ObtenerPagosDelPeriodo(anio, mes));
        Assert.IsType<BadRequestObjectResult>(Controller().ObtenerDelPeriodo(anio, mes));
        Assert.Empty(_dao.Filtros);
        Assert.Equal(0, _dao.LlamadasTotales);
    }

    [Fact]
    public void Pagos_DevuelveLaPaginaConElTotalDelPeriodo()
    {
        _dao.Pagina = new PagosDelPeriodoPagina
        {
            Total = 126, Pagina = 2, TamanioPagina = 10,
            Items = Enumerable.Range(1, 10).Select(i => new PagoDelPeriodo { IdPago = i, Monto = 1000, FechaPago = new DateTime(2026, 10, i) }).ToList()
        };

        var ok = Assert.IsType<OkObjectResult>(Controller().ObtenerPagosDelPeriodo(2026, 10, page: 2));
        var dto = Assert.IsType<PeriodPaymentsPageResponseDTO>(ok.Value);

        Assert.Equal(126, dto.Total);
        Assert.Equal(13, dto.TotalPaginas);
        Assert.Equal(2, dto.Pagina);
        Assert.Equal(10, dto.Items.Count);
    }

    // Las tarjetas y el donut no bajan filas: piden solo los totales, que no dependen de la página.
    [Fact]
    public void Totales_NoTraenElDetalleNiDependenDeLaPagina()
    {
        _dao.Totales = new RecaudacionPeriodo { Total = 315000, Efectivo = 115000, Transferencia = 200000, Pagos = 126, PagosEfectivo = 50, PagosTransferencia = 76 };

        var ok = Assert.IsType<OkObjectResult>(Controller().ObtenerDelPeriodo(2026, 10));
        var dto = Assert.IsType<RecaudacionResponseDTO>(ok.Value);

        Assert.Equal(1, _dao.LlamadasTotales);
        Assert.Equal(0, _dao.LlamadasDetalle);
        Assert.Equal(126, dto.Pagos);
        Assert.Equal(315000, dto.Total);
        Assert.DoesNotContain("detalle", JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    // La hora de registro ya está en hora del club: viaja con el corrimiento de la zona del club, así que la
    // pantalla puede mostrar esos dígitos tal cual, sin depender de la zona del navegador.
    [Fact]
    public void LaFechaViajaConDiaYHoraDelClubSinAmbiguedad()
    {
        var conHora = PeriodPaymentDTO.FromEntity(new PagoDelPeriodo { FechaPago = new DateTime(2026, 10, 31), FechaHoraRegistro = new DateTime(2026, 10, 31, 14, 35, 12) });
        var sinHora = PeriodPaymentDTO.FromEntity(new PagoDelPeriodo { FechaPago = new DateTime(2026, 10, 2) });

        string json = JsonSerializer.Serialize(new[] { conHora, sinHora }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"fechaPago\":\"2026-10-31\"", json);
        Assert.Contains("\"fechaHoraRegistro\":\"2026-10-31T14:35:12-03:00\"", json);
        Assert.Contains("\"fechaPago\":\"2026-10-02\",\"fechaHoraRegistro\":null", json);
    }

    // El ORDER BY sale de una lista cerrada y siempre termina en el id del pago (orden estable entre páginas).
    [Theory]
    [InlineData(OrdenPagosDelPeriodo.Fecha, true, "COALESCE(p.fecha_hora_registro, CAST(p.fecha_pago AS datetime2)) DESC, p.PK_id_pago DESC")]
    [InlineData(OrdenPagosDelPeriodo.Fecha, false, "COALESCE(p.fecha_hora_registro, CAST(p.fecha_pago AS datetime2)) ASC, p.PK_id_pago ASC")]
    [InlineData(OrdenPagosDelPeriodo.Monto, true, "p.monto_final DESC, p.PK_id_pago DESC")]
    [InlineData(OrdenPagosDelPeriodo.Monto, false, "p.monto_final ASC, p.PK_id_pago ASC")]
    public void Orden_EsUnaListaCerradaConDesempatePorId(OrdenPagosDelPeriodo orden, bool descendente, string esperado)
    {
        Assert.Equal(esperado, RecaudacionDao.Orden(orden, descendente));
    }

    [Fact]
    public void Orden_DesconocidoNoArmaSql()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RecaudacionDao.Orden((OrdenPagosDelPeriodo)99, true));
    }

    private class FakeRecaudacionDao : IRecaudacionDao
    {
        public List<FiltroPagosDelPeriodo> Filtros { get; } = new();
        public int LlamadasTotales { get; private set; }
        public int LlamadasDetalle { get; private set; }
        public RecaudacionPeriodo Totales { get; set; } = new();
        public PagosDelPeriodoPagina Pagina { get; set; } = new() { TamanioPagina = 10, Pagina = 1 };

        public RecaudacionPeriodo ObtenerDelPeriodo(int anio, int mes, int? idCategoria)
        {
            LlamadasDetalle++;
            return Totales;
        }

        public RecaudacionPeriodo ObtenerTotalesDelPeriodo(int anio, int mes, int? idCategoria)
        {
            LlamadasTotales++;
            return Totales;
        }

        public PagosDelPeriodoPagina ObtenerPagosDelPeriodo(FiltroPagosDelPeriodo filtro)
        {
            Filtros.Add(filtro);
            return Pagina;
        }
    }
}
