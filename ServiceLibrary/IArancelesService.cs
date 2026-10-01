using EntityLibrary;

namespace ServiceLibrary
{
    public class ProgramarArancelRequest
    {
        // El arancel es por género O por categoría: se informa exactamente uno de los dos.
        public string? Genero { get; set; }
        public int? IdCategoria { get; set; }
        public decimal Monto { get; set; }
        public DateTime VigenteDesde { get; set; }
    }

    public interface IArancelesService
    {
        IReadOnlyList<ArancelHistorialItem> ObtenerHistorial();

        ArancelResumen ObtenerResumen();

        void ProgramarArancel(ProgramarArancelRequest request);
    }
}
