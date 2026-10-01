using DaoLibrary;
using DaoLibrary.Exceptions;
using EntityLibrary;

namespace ServiceLibrary
{
    public class ArancelesService : IArancelesService
    {
        private static readonly string[] GenerosValidos = { "Masculino", "Femenino" };

        private readonly IArancelesDao _arancelesDao;
        private readonly IPagosDao _pagosDao;
        private readonly ISqlTransactionRunner _transactionRunner;
        private readonly ICategoriasDao _categoriasDao;

        public ArancelesService(IArancelesDao arancelesDao, IPagosDao pagosDao, ISqlTransactionRunner transactionRunner, ICategoriasDao categoriasDao)
        {
            _arancelesDao = arancelesDao;
            _pagosDao = pagosDao;
            _transactionRunner = transactionRunner;
            _categoriasDao = categoriasDao;
        }

        public IReadOnlyList<ArancelHistorialItem> ObtenerHistorial() => _arancelesDao.ObtenerHistorial();

        public ArancelResumen ObtenerResumen() => _arancelesDao.ObtenerResumen();

        public void ProgramarArancel(ProgramarArancelRequest request)
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

            if (request.IdCategoria.HasValue && !_categoriasDao.ObtenerTodas().Any(c => c.IdCategoria == request.IdCategoria.Value))
            {
                throw new ArancelInvalidoException($"No existe la categoría con id {request.IdCategoria.Value}.");
            }

            if (request.Monto <= 0)
            {
                throw new ArancelInvalidoException("El monto debe ser mayor a cero.");
            }

            string? genero = tieneGenero ? request.Genero : null;

            // Se genera la cuota justo del mes de este arancel (nada de meses intermedios ni nada
            // atado a la fecha de hoy), y SOLO para quienes alcanza: los jugadores del género de
            // este arancel — cargar un Masculino nunca debe tocar cuotas Femenino —, o los de la
            // categoría. Los meses siguientes los genera solo GeneradorCuotasMensuales, repitiendo
            // el arancel vigente. Si ya existiera una fila para ese jugador+período,
            // GenerarCuotasPendientesDelMes la deja como está (no duplica ni pisa nada).
            _transactionRunner.EjecutarEnTransaccion((conexion, transaccion) =>
            {
                _arancelesDao.ProgramarArancel(conexion, transaccion, genero, request.IdCategoria, request.Monto, request.VigenteDesde);
                _pagosDao.GenerarCuotasPendientesDelMes(conexion, transaccion, genero, request.IdCategoria, request.VigenteDesde.Month, request.VigenteDesde.Year);
                return true;
            });
        }
    }
}
