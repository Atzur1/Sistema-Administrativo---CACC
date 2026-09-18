using EntityLibrary;

namespace ServiceLibrary
{
    public class ProgramarArancelRequest
    {
        public string Genero { get; set; } = string.Empty;
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
