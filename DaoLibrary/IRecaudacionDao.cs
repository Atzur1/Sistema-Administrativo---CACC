using EntityLibrary;

namespace DaoLibrary;

public interface IRecaudacionDao
{
    // Totales y TODOS los cobros del período (exportaciones PDF y CSV).
    RecaudacionPeriodo ObtenerDelPeriodo(int anio, int mes, int? idCategoria);

    // HU-107: solo los totales (tarjetas y donut); Detalle queda vacío.
    RecaudacionPeriodo ObtenerTotalesDelPeriodo(int anio, int mes, int? idCategoria);

    // HU-107: una página de la tabla, ordenada y paginada en la base.
    PagosDelPeriodoPagina ObtenerPagosDelPeriodo(FiltroPagosDelPeriodo filtro);
}
