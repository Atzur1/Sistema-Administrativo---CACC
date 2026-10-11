using EntityLibrary;

namespace DaoLibrary;

public interface IRecaudacionDao
{
    RecaudacionPeriodo ObtenerDelPeriodo(int anio, int mes, int? idCategoria);
}
