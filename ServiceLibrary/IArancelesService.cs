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

    public class ProgramarArancelResultado
    {
        // Cuotas pendientes del mes en curso que se volvieron a emitir con el monto nuevo.
        public int CuotasReemitidas { get; set; }

        // Cuotas del mes en curso que ya tenían algún pago y por eso se mantuvieron sin cambios.
        public int CuotasConPagosConservadas { get; set; }
    }

    public class CancelarArancelResultado
    {
        // Cuotas pendientes sin pagos que se quitaron junto con el arancel.
        public int CuotasEliminadas { get; set; }
    }

    public interface IArancelesService
    {
        IReadOnlyList<ArancelHistorialItem> ObtenerHistorial();

        ArancelResumen ObtenerResumen();

        ProgramarArancelResultado ProgramarArancel(ProgramarArancelRequest request);

        CancelarArancelResultado CancelarArancel(int idArancel);
    }
}
