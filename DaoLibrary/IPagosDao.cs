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

        // Busca la cuota PENDIENTE (Estado = false) del jugador para ese mes/año, si existe, para
        // registrar un abono (total o parcial) contra ella. WITH (UPDLOCK) como la de arriba.
        Pago? ObtenerPagoPendienteDeJugadorEnPeriodo(SqlConnection conexion, SqlTransaction transaccion, int idJugador, int mes, int anio);

        // Busca si el jugador tiene un beneficio activo de Becados y Descuentos (JUGADORES_DESCUENTOS)
        // cuya vigencia cubra el mes de esa fecha de vencimiento. Se usa para ajustar el saldo real
        // a cobrar de una cuota que ya estaba cargada ANTES de asignarle el beneficio.
        DescuentoAplicable? ObtenerDescuentoAplicableEnPeriodo(SqlConnection conexion, SqlTransaction transaccion, int idJugador, DateTime fechaVencimiento);

        // Reduce el saldo de una cuota pendiente tras un abono parcial (sigue con Estado = false).
        void ActualizarSaldoPendiente(SqlConnection conexion, SqlTransaction transaccion, int idPago, decimal nuevoMonto);

        // Borra la cuota pendiente cuando un abono la termina de cubrir (ya no queda saldo).
        void EliminarPago(SqlConnection conexion, SqlTransaction transaccion, int idPago);

        // PAGOS.PK_id_pago no tiene IDENTITY: el id se calcula a mano dentro de la propia transacción.
        int InsertarPago(SqlConnection conexion, SqlTransaction transaccion, Pago pago);

        IReadOnlyList<Pago> ObtenerPagosPorId(SqlConnection conexion, SqlTransaction transaccion, IEnumerable<int> idsPago);

        void MarcarPagosComoAbonados(SqlConnection conexion, SqlTransaction transaccion, IEnumerable<int> idsPago, DateTime fechaPago, string metodoPago);

        // ---- Lecturas simples, sin transacción (mismo estilo que AuthDao) ----

        IReadOnlyList<PendienteJugador> ObtenerPendientesAgrupados();

        // Roster of players with what each one owes (HU-029). With onlyDebtors the query itself
        // returns just the players that owe something, so the filter never depends on the client.
        IReadOnlyList<PlayerAccount> GetPlayerAccounts(bool onlyDebtors);

        IReadOnlyList<PagoReciente> ObtenerUltimosPagos(int top);

        ResumenPagos ObtenerResumen();

        HistorialPagosResultado ObtenerHistorialPagos(int idJugador, int page, int pageSize);

        // Detalle de deuda: cada cuota pendiente del jugador, con sus abonos parciales (si tiene)
        // y el saldo que le sigue faltando.
        IReadOnlyList<CuotaPendienteDetalle> ObtenerDeudaDetalle(int idJugador);

        // Genera la cuota pendiente (PAGOS con Estado = false) de cada jugador de ESE género que
        // todavía no tiene ninguna fila para ese período, usando el arancel de ese género/mes y
        // el descuento activo si tiene uno. Cargar un arancel de un género nunca toca a los
        // jugadores del otro género. Se puede llamar repetidas veces sin duplicar: solo inserta
        // para quien no tenga ya una fila ese período.
        void GenerarCuotasPendientesDelMes(string genero, int mes, int anio);
    }
}
