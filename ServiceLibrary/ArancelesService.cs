using DaoLibrary;
using DaoLibrary.Exceptions;
using EntityLibrary;

namespace ServiceLibrary
{
    public class ArancelesService : IArancelesService
    {
        private static readonly string[] GenerosValidos = { "Masculino", "Femenino" };

        private readonly IArancelesDao _arancelesDao;
        private readonly ICuotasPorArancelDao _cuotasDao;
        private readonly ISqlTransactionRunner _transactionRunner;
        private readonly ICategoriasDao _categoriasDao;
        private readonly Func<DateTime> _hoy;

        // reloj: solo para las pruebas; en la aplicación se usa la fecha real.
        public ArancelesService(IArancelesDao arancelesDao, ICuotasPorArancelDao cuotasDao, ISqlTransactionRunner transactionRunner, ICategoriasDao categoriasDao, Func<DateTime>? reloj = null)
        {
            _arancelesDao = arancelesDao;
            _cuotasDao = cuotasDao;
            _transactionRunner = transactionRunner;
            _categoriasDao = categoriasDao;
            _hoy = reloj ?? (() => RelojNegocio.Hoy);
        }

        public IReadOnlyList<ArancelHistorialItem> ObtenerHistorial() => _arancelesDao.ObtenerHistorial();

        public ArancelResumen ObtenerResumen() => _arancelesDao.ObtenerResumen();

        public ProgramarArancelResultado ProgramarArancel(ProgramarArancelRequest request)
        {
            // Género O categoría, nunca los dos ni ninguno: si la categoría tiene arancel propio,
            // al sistema no le importa el género de sus jugadores.
            bool tieneGenero = !string.IsNullOrWhiteSpace(request.Genero);
            if (tieneGenero == request.IdCategoria.HasValue)
            {
                throw new ArancelInvalidoException("El arancel debe ser por género o por categoría (uno solo).");
            }

            if (tieneGenero && !GenerosValidos.Contains(request.Genero))
            {
                throw new ArancelInvalidoException(
                    $"Género inválido: '{request.Genero}'. Valores permitidos: {string.Join(", ", GenerosValidos)}.");
            }

            string? nombreCategoria = null;
            if (request.IdCategoria.HasValue)
            {
                nombreCategoria = _categoriasDao.ObtenerTodas().FirstOrDefault(c => c.IdCategoria == request.IdCategoria.Value)?.Nombre;
                if (nombreCategoria == null)
                {
                    throw new ArancelInvalidoException($"No existe la categoría con id {request.IdCategoria.Value}.");
                }
            }

            if (request.Monto <= 0)
            {
                throw new ArancelInvalidoException("El monto debe ser mayor a cero.");
            }

            string? genero = tieneGenero ? request.Genero : null;
            DateTime vigenteDesde = request.VigenteDesde.Date;
            DateTime mesDelArancel = PrimerDiaDelMes(vigenteDesde);
            DateTime mesActual = PrimerDiaDelMes(_hoy());

            return _transactionRunner.EjecutarEnTransaccion((conexion, transaccion) =>
            {
                // Un solo arancel por mes y destino: dos en el mismo mes se pisarían (la cuota del mes
                // toma el último) y dejarían cuotas emitidas con un monto que ya no figura vigente.
                // Para cambiar el de un mes hay que cancelar el existente y programar el nuevo.
                DateTime? existente = _arancelesDao.ObtenerVigenteDesdeEnMes(
                    conexion, transaccion, genero, request.IdCategoria, vigenteDesde.Year, vigenteDesde.Month);
                if (existente.HasValue)
                {
                    throw new ArancelInvalidoException(
                        $"{Destino(genero, nombreCategoria)} ya tiene un arancel que rige desde el {existente.Value:dd/MM/yyyy}, " +
                        $"en {NombreDelMes(mesDelArancel)}. Cancelalo primero para poder cargar uno nuevo para ese mes.");
                }

                _arancelesDao.ProgramarArancel(conexion, transaccion, genero, request.IdCategoria, request.Monto, vigenteDesde);

                var resultado = new ProgramarArancelResultado();

                // Mes futuro: no se emite nada ahora. GeneradorCuotasMensuales crea las cuotas del mes
                // cuando ese mes empieza, con el arancel que corresponda a cada jugador.
                if (mesDelArancel > mesActual)
                {
                    return resultado;
                }

                // Mes en curso: las cuotas ya emitidas (con el arancel anterior) se vuelven a emitir con el
                // monto nuevo, salvo las que ya recibieron algún pago, que no se tocan.
                // Mes pasado: solo se emiten las cuotas que falten; las existentes quedan como están.
                if (mesDelArancel == mesActual)
                {
                    DateTime mesSiguiente = mesDelArancel.AddMonths(1);
                    resultado.CuotasConPagosConservadas = _cuotasDao.ContarCuotasConPagos(
                        conexion, transaccion, genero, request.IdCategoria, mesDelArancel, mesSiguiente, null);
                    resultado.CuotasReemitidas = _cuotasDao.EliminarCuotasPendientesSinPagos(
                        conexion, transaccion, genero, request.IdCategoria, mesDelArancel, mesSiguiente, null);
                }

                // Vuelve a emitir las que se borraron (y las que faltaban) con el arancel que le corresponde
                // a cada jugador: el de su categoría si lo tiene vigente, si no el de su género.
                _cuotasDao.GenerarCuotasPendientesDelMes(conexion, transaccion, genero, request.IdCategoria, vigenteDesde.Month, vigenteDesde.Year);
                return resultado;
            });
        }

        public CancelarArancelResultado CancelarArancel(int idArancel)
        {
            DateTime mesActual = PrimerDiaDelMes(_hoy());

            return _transactionRunner.EjecutarEnTransaccion((conexion, transaccion) =>
            {
                Arancel arancel = _arancelesDao.ObtenerPorId(conexion, transaccion, idArancel)
                    ?? throw new ArancelInvalidoException("El arancel no existe o ya fue cancelado.");

                DateTime mesDelArancel = PrimerDiaDelMes(arancel.VigenteDesde);
                if (mesDelArancel < mesActual)
                {
                    throw new ArancelInvalidoException(
                        "Los aranceles de meses anteriores son parte del historial y no se pueden cancelar: " +
                        "sus cuotas ya se emitieron en períodos cerrados.");
                }

                // Períodos que cubre este arancel: desde su mes hasta el mes del arancel siguiente del mismo
                // destino (sin incluirlo), o sin límite si nadie lo reemplazó.
                DateTime? siguiente = _arancelesDao.ObtenerSiguienteVigenteDesde(
                    conexion, transaccion, arancel.Genero, arancel.IdCategoria, arancel.VigenteDesde);
                DateTime? hasta = siguiente.HasValue ? PrimerDiaDelMes(siguiente.Value) : null;

                // Las cuotas emitidas con este arancel son las del mismo destino, en esos períodos, por su monto.
                int conPagos = _cuotasDao.ContarCuotasConPagos(
                    conexion, transaccion, arancel.Genero, arancel.IdCategoria, mesDelArancel, hasta, arancel.Monto);
                if (conPagos > 0)
                {
                    throw new ArancelInvalidoException(
                        $"No se puede cancelar este arancel: {conPagos} cuota{(conPagos == 1 ? "" : "s")} emitida{(conPagos == 1 ? "" : "s")} " +
                        $"con él ya tiene{(conPagos == 1 ? "" : "n")} pagos registrados. Un arancel con cuotas cobradas ya no se puede quitar.");
                }

                int eliminadas = _cuotasDao.EliminarCuotasPendientesSinPagos(
                    conexion, transaccion, arancel.Genero, arancel.IdCategoria, mesDelArancel, hasta, arancel.Monto);
                _arancelesDao.EliminarArancel(conexion, transaccion, idArancel);

                return new CancelarArancelResultado { CuotasEliminadas = eliminadas };
            });
        }

        private static DateTime PrimerDiaDelMes(DateTime fecha) => new DateTime(fecha.Year, fecha.Month, 1);

        private static string Destino(string? genero, string? nombreCategoria) =>
            genero != null ? $"El género {genero}" : $"La categoría {nombreCategoria}";

        private static readonly string[] Meses =
        {
            "enero", "febrero", "marzo", "abril", "mayo", "junio",
            "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"
        };

        private static string NombreDelMes(DateTime mes) => $"{Meses[mes.Month - 1]} de {mes.Year}";
    }
}
