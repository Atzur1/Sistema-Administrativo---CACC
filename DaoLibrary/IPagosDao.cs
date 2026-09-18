using EntityLibrary;
using Microsoft.Data.SqlClient;

namespace DaoLibrary
{
    public interface IPagosDao
    {
        // ---- Operaciones transaccionales: reciben la conexión/transacción abierta por ISqlTransactionRunner ----

        // Busca un pago ya ABONADO del jugador para ese mes/año (bloqueo de re-cobro / evitar doble carga
        // del mismo período). WITH (UPDLOCK) para que dos requests concurrentes no pasen ambos la validación.
        Pago? ObtenerPagoAbonadoDeJugadorEnPeriodo(SqlConnection conexion, SqlTransaction transaccion, int idJugador, int mes, int anio);

        // PAGOS.PK_id_pago no tiene IDENTITY: el id se calcula a mano dentro de la propia transacción.
        int InsertarPago(SqlConnection conexion, SqlTransaction transaccion, Pago pago);

        IReadOnlyList<Pago> ObtenerPagosPorId(SqlConnection conexion, SqlTransaction transaccion, IEnumerable<int> idsPago);

        void MarcarPagosComoAbonados(SqlConnection conexion, SqlTransaction transaccion, IEnumerable<int> idsPago, DateTime fechaPago, string metodoPago);

        // ---- Lecturas simples, sin transacción (mismo estilo que AuthDao) ----

        IReadOnlyList<PendienteJugador> ObtenerPendientesAgrupados();

        IReadOnlyList<PagoReciente> ObtenerUltimosPagos(int top);

        ResumenPagos ObtenerResumen();

        HistorialPagosResultado ObtenerHistorialPagos(int idJugador, int page, int pageSize);
    }
}
