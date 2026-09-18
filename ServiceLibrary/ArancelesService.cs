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

        public ArancelesService(IArancelesDao arancelesDao, IPagosDao pagosDao)
        {
            _arancelesDao = arancelesDao;
            _pagosDao = pagosDao;
        }

        public IReadOnlyList<ArancelHistorialItem> ObtenerHistorial() => _arancelesDao.ObtenerHistorial();

        public ArancelResumen ObtenerResumen() => _arancelesDao.ObtenerResumen();

        public void ProgramarArancel(ProgramarArancelRequest request)
        {
            if (!GenerosValidos.Contains(request.Genero))
            {
                throw new ArancelInvalidoException(
                    $"Género inválido: '{request.Genero}'. Valores permitidos: {string.Join(", ", GenerosValidos)}.");
            }

            if (request.Monto <= 0)
            {
                throw new ArancelInvalidoException("El monto debe ser mayor a cero.");
            }

            _arancelesDao.ProgramarArancel(request.Genero, request.Monto, request.VigenteDesde);

            // Manual: se genera la cuota justo del mes de este arancel (nada de meses intermedios
            // ni nada atado a la fecha de hoy), y SOLO para los jugadores del género de este
            // arancel — cargar un Masculino nunca debe generar ni tocar cuotas Femenino. Si ya
            // existiera una fila para ese jugador+período, GenerarCuotasPendientesDelMes la deja
            // como está (no duplica ni pisa nada).
            _pagosDao.GenerarCuotasPendientesDelMes(request.Genero, request.VigenteDesde.Month, request.VigenteDesde.Year);
        }
    }
}
