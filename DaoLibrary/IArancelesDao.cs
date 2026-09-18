using EntityLibrary;

namespace DaoLibrary
{
    public interface IArancelesDao
    {
        // Monto del arancel vigente de un género a una fecha dada (el de vigente_desde más
        // reciente que ya empezó). Null si todavía no se cargó ninguno para ese género.
        decimal? ObtenerMontoVigente(string genero, DateTime fecha);

        IReadOnlyList<ArancelHistorialItem> ObtenerHistorial();

        ArancelResumen ObtenerResumen();

        void ProgramarArancel(string genero, decimal monto, DateTime vigenteDesde);
    }
}
