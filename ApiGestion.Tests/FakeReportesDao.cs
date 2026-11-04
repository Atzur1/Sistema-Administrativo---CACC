using DaoLibrary;
using EntityLibrary;

namespace ApiGestion.Tests;

internal sealed class FakeReportesDao : IReportesDao
{
    public int Cantidad { get; set; }
    public bool FallarRegistro { get; set; }
    public List<(string, int?, int, int, int)> Conteos { get; } = new();
    public List<ExportacionReporte> Exportaciones { get; } = new();

    public int Contar(string tipo, int? categoria, int top, int anio, int mes)
    {
        Conteos.Add((tipo, categoria, top, anio, mes));
        return Cantidad;
    }

    public void Registrar(ExportacionReporte exportacion)
    {
        if (FallarRegistro) throw new InvalidOperationException("fixture falla historial");
        Exportaciones.Add(exportacion);
    }

    public HistorialExportacionesPagina Historial(int pagina, int tamanioPagina) => new()
    {
        Items = Exportaciones, Total = Exportaciones.Count, Pagina = pagina, TamanioPagina = tamanioPagina
    };
}
