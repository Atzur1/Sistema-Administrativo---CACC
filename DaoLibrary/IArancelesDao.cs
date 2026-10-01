using EntityLibrary;
using Microsoft.Data.SqlClient;

namespace DaoLibrary
{
    public interface IArancelesDao
    {
        // Monto del arancel por género vigente a una fecha dada (el de vigente_desde más
        // reciente que ya empezó). Null si todavía no se cargó ninguno para ese género.
        decimal? ObtenerMontoVigente(string genero, DateTime fecha);

        // Monto que le corresponde a un jugador de ese género y categoría a una fecha dada:
        // el arancel de su categoría si tiene uno vigente (el género no importa) y, si no, el
        // de su género. Null si no hay ninguno de los dos.
        decimal? ObtenerMontoVigente(string genero, int idCategoria, DateTime fecha);

        IReadOnlyList<ArancelHistorialItem> ObtenerHistorial();

        ArancelResumen ObtenerResumen();

        // Un arancel es por género O por categoría: se pasa exactamente uno de los dos.
        void ProgramarArancel(string? genero, int? idCategoria, decimal monto, DateTime vigenteDesde);
        void ProgramarArancel(SqlConnection conexion, SqlTransaction transaccion, string? genero, int? idCategoria, decimal monto, DateTime vigenteDesde);
    }
}
