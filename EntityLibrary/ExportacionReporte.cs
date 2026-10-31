namespace EntityLibrary;

// Metadatos únicamente. Nunca contiene el archivo ni las filas del reporte.
public sealed class ExportacionReporte
{
    public long Id { get; set; }
    public DateTime FechaHora { get; set; }
    public int UsuarioId { get; set; }
    public string Persona { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string Formato { get; set; } = "";
    public string Filtros { get; set; } = "";
    public int CantidadFilas { get; set; }
}

public sealed class HistorialExportacionesPagina
{
    public List<ExportacionReporte> Items { get; set; } = new();
    public int Total { get; set; }
    public int Pagina { get; set; }
    public int TamanioPagina { get; set; }
}
