using EntityLibrary;

namespace ServiceLibrary
{
    // HU-106: el cambio (género O categoría, tipo, valor y vigencia) más lo que el SuperAdmin aprobó en la simulación.
    public class ProgramarArancelRequest : CambioArancel
    {
        // Huella de la simulación aprobada: si el impacto recalculado al grabar es otro, no se graba.
        public string Huella { get; set; } = string.Empty;

        // Cantidad de jugadores que el SuperAdmin escribió para confirmar (obligatoria si alcanza más de una categoría).
        public int? JugadoresConfirmados { get; set; }
    }

    public class ProgramarArancelResultado
    {
        // El impacto que se grabó (el aprobado, recalculado dentro de la transacción).
        public ImpactoArancel? Impacto { get; set; }

        // Cuotas pendientes del mes en curso que se volvieron a emitir con el monto nuevo.
        public int CuotasReemitidas { get; set; }

        // Cuotas del mes en curso que ya tenían algún pago y por eso se mantuvieron sin cambios.
        public int CuotasConPagosConservadas { get; set; }

        // Si las cuotas con este monto todavía no se emitieron: el día 10 en que las genera el generador mensual.
        public DateTime? SeEmiteElDia { get; set; }
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

        // HU-106: cuántos jugadores y cuánto dinero alcanza el cambio, sin grabar nada.
        ImpactoArancel SimularArancel(CambioArancel cambio);

        // Graba el cambio solo si el impacto, recalculado en la misma transacción, es el de la simulación aprobada
        // (si no, ImpactoDesactualizadoException con el impacto actual).
        ProgramarArancelResultado ProgramarArancel(ProgramarArancelRequest request);

        CancelarArancelResultado CancelarArancel(int idArancel);
    }
}
