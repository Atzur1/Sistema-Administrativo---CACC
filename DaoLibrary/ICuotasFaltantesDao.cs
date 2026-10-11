using EntityLibrary;

namespace DaoLibrary
{
    public interface ICuotasFaltantesDao
    {
        // Meses entre desde y hasta (primeros días, inclusive) con cuotas sin generar, en orden cronológico.
        IReadOnlyList<MesConCuotasFaltantes> ObtenerMesesConFaltantes(DateTime desde, DateTime hasta);

        // Las cuotas que faltan en ese mes: a quién y por cuánto.
        IReadOnlyList<CuotaFaltante> ObtenerFaltantesDelMes(DateTime periodo);

        // Emite las cuotas que faltan en ese mes (las mismas reglas que el generador mensual). Devuelve cuántas creó.
        int GenerarFaltantesDelMes(DateTime periodo);
    }
}
