using EntityLibrary;

namespace DaoLibrary;

public interface IReportesDao
{
    int Contar(string tipo, int? idCategoria, int top, int anio, int mes);
    void Registrar(ExportacionReporte exportacion);
    HistorialExportacionesPagina Historial(int pagina, int tamanioPagina);
}
