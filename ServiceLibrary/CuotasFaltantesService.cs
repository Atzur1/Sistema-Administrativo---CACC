using DaoLibrary;
using DaoLibrary.Exceptions;
using EntityLibrary;

namespace ServiceLibrary
{
    // Aviso de cuotas faltantes (Resumen General, solo SuperAdmin). El generador mensual nunca crea meses
    // viejos por su cuenta: si la API estuvo apagada el día 10 de algún mes, ese mes queda sin cuotas y acá se
    // avisa. Generarlas es una decisión del SuperAdmin, mes por mes, y nunca automática.
    //
    // Se revisan los últimos 12 meses cerrados: los anteriores al último cuya cuota ya corresponde (ese lo
    // asegura el generador mensual, que corre cada pocas horas).
    public class CuotasFaltantesService : ICuotasFaltantesService
    {
        public const int MesesRevisados = 12;

        private readonly ICuotasFaltantesDao _dao;
        private readonly Func<DateTime> _hoy;

        // reloj: solo para las pruebas; en la aplicación se usa la fecha real.
        public CuotasFaltantesService(ICuotasFaltantesDao dao, Func<DateTime>? reloj = null)
        {
            _dao = dao;
            _hoy = reloj ?? (() => RelojNegocio.Hoy);
        }

        public IReadOnlyList<MesConCuotasFaltantes> ObtenerMesesConFaltantes()
        {
            var (desde, hasta) = Ventana();
            return _dao.ObtenerMesesConFaltantes(desde, hasta);
        }

        public IReadOnlyList<CuotaFaltante> ObtenerFaltantesDelMes(int anio, int mes)
            => _dao.ObtenerFaltantesDelMes(ValidarMes(anio, mes));

        public int GenerarFaltantesDelMes(int anio, int mes)
            => _dao.GenerarFaltantesDelMes(ValidarMes(anio, mes));

        private (DateTime Desde, DateTime Hasta) Ventana()
        {
            DateTime hasta = CalendarioCuotas.UltimoPeriodoEmitible(_hoy()).AddMonths(-1);
            return (hasta.AddMonths(-(MesesRevisados - 1)), hasta);
        }

        // Solo los meses revisados: ni el mes en curso (lo genera el generador mensual el 10) ni meses futuros,
        // ni meses de hace más de un año.
        private DateTime ValidarMes(int anio, int mes)
        {
            if (mes < 1 || mes > 12 || anio < 2000 || anio > 9999)
            {
                throw new CuotasFaltantesInvalidasException("El mes indicado no es válido.");
            }

            var periodo = new DateTime(anio, mes, 1);
            var (desde, hasta) = Ventana();
            if (periodo < desde || periodo > hasta)
            {
                throw new CuotasFaltantesInvalidasException(
                    $"Solo se pueden generar cuotas faltantes de los últimos {MesesRevisados} meses cerrados " +
                    $"(de {desde:MM/yyyy} a {hasta:MM/yyyy}). Las del mes en curso las genera el sistema el día {CalendarioCuotas.DiaDeCorte}.");
            }
            return periodo;
        }
    }
}
