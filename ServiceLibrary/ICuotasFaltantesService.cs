using EntityLibrary;

namespace ServiceLibrary
{
    public interface ICuotasFaltantesService
    {
        // Los últimos meses cerrados que quedaron con cuotas sin generar (vacío si no falta ninguna).
        IReadOnlyList<MesConCuotasFaltantes> ObtenerMesesConFaltantes();

        // Detalle de lo que se generaría en ese mes (para mostrarlo antes de confirmar).
        IReadOnlyList<CuotaFaltante> ObtenerFaltantesDelMes(int anio, int mes);

        // Genera las cuotas faltantes de ese mes. Devuelve cuántas creó.
        int GenerarFaltantesDelMes(int anio, int mes);
    }
}
