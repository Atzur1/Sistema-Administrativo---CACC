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

        // Lee las filas con UPDLOCK/HOLDLOCK: una segunda sesión que quiera cobrar las mismas cuotas
        // espera hasta el commit/rollback de esta y después las ve con su estado real.
        IReadOnlyList<Pago> ObtenerPagosPorId(SqlConnection conexion, SqlTransaction transaccion, IEnumerable<int> idsPago);

        // HU-025: el cobro valida que el jugador exista dentro de la misma transacción.
        bool ExisteJugador(SqlConnection conexion, SqlTransaction transaccion, int idJugador);

        // HU-025: marca como abonadas las cuotas pendientes del jugador con método, fecha de pago,
        // hora de registro (reloj de SQL Server) y operador. Si alguna ya no está pendiente o no le
        // pertenece, lanza InvalidOperationException para que la transacción se revierta completa.
        // Devuelve la fecha/hora de registro que quedó grabada.
        DateTime MarcarPagosComoAbonados(SqlConnection conexion, SqlTransaction transaccion, int idJugador, IEnumerable<int> idsPago, DateTime fechaPago, string metodoPago, int idUsuarioRegistro);
        void ActualizarMontoCobroConDescuento(SqlConnection conexion, SqlTransaction transaccion, int idPago, int idDescuento, decimal montoFinal);

        // Emite la cuota de UN jugador para ese período con el arancel que le corresponde, si todavía no
        // tiene ninguna. false si no se emitió (no existe, ya la tiene, no estaba en el club ese mes, o
        // no hay arancel vigente para él).
        bool EmitirCuotaDeJugador(SqlConnection conexion, SqlTransaction transaccion, int idJugador, int mes, int anio);

        // ---- Anulación de pagos cargados por error ----

        // La fila de PAGOS con bloqueo (UPDLOCK/HOLDLOCK), con concepto, operador y hora de registro; null si no existe.
        Pago? ObtenerPagoParaAnular(SqlConnection conexion, SqlTransaction transaccion, int idPago);

        // Todas las filas (pendiente y abonos) de la misma cuota o inscripción que ese pago, con bloqueo:
        // mismo jugador y concepto, y para cuotas el mismo mes de fecha_vencimiento.
        IReadOnlyList<Pago> ObtenerFilasDeLaMismaDeuda(SqlConnection conexion, SqlTransaction transaccion, Pago pago);

        // Deja el registro permanente de la anulación (PAGOS_ANULADOS) y borra el abono de PAGOS.
        void RegistrarAnulacionYEliminarAbono(SqlConnection conexion, SqlTransaction transaccion, Pago abono, string motivo, int idUsuarioAnulacion);

        // ---- Lecturas simples, sin transacción (mismo estilo que AuthDao) ----

        // idCategoria: null trae todas las categorías (comportamiento previo, sin cambios). Con un
        // valor, HU-020 acota el padrón de morosos a esa división únicamente.
        IReadOnlyList<PendienteJugador> ObtenerPendientesAgrupados(int? idCategoria = null);

        // HU-029: padrón de jugadores con lo que debe cada uno. Con onlyDebtors la propia consulta
        // devuelve solo a quien debe algo, así el filtro nunca depende del cliente.
        IReadOnlyList<PlayerAccount> GetPlayerAccounts(bool onlyDebtors);

        // Deuda agrupada por categoría/división para un período: un mes puntual (mes != null)
        // o el año completo (mes == null). Se filtra por fecha_vencimiento de la cuota (el
        // período que cubre, no cuándo se cargó), mismo criterio que el resto de HU-020.
        IReadOnlyList<CategoriaDeuda> ObtenerDeudaPorCategoria(int anio, int? mes = null);

        IReadOnlyList<PagoReciente> ObtenerUltimosPagos(int top);

        IReadOnlyList<PagoReciente> ObtenerTodosLosPagos();

        ResumenPagos ObtenerResumen();

        ResumenPagosHoy ObtenerResumenHoy();

        HistorialPagosResultado ObtenerHistorialPagos(int idJugador, int page, int pageSize);

        // Detalle de deuda: cada cuota pendiente del jugador, con sus abonos parciales (si tiene)
        // y el saldo que le sigue faltando.
        IReadOnlyList<CuotaPendienteDetalle> ObtenerDeudaDetalle(int idJugador);

        // HU-025: todas las filas de concepto 'Cuota' del jugador con período cargado (pendientes y
        // abonadas), con el saldo ya ajustado por beneficio. PagosService las agrupa por período.
        IReadOnlyList<CuotaMovimiento> ObtenerMovimientosCuotas(int idJugador);

        // HU-024: datos del jugador y las filas de sus cuotas; null si el jugador no existe.
        PlayerStatementAccount? GetPlayerStatementAccount(int playerId);

        // Genera la cuota pendiente (PAGOS con Estado = false) del mes para cada jugador que todavía
        // no tiene ninguna fila de cuota para ese período, con el arancel que le corresponde
        // (el de su categoría si tiene uno vigente; si no, el de su género). genero / idCategoria
        // acotan a quién alcanza (un arancel de un género nunca toca a los jugadores del otro);
        // ambos null = todos los jugadores (generación mensual automática). Se puede llamar
        // repetidas veces sin duplicar: solo inserta para quien no tenga ya una fila ese período.
        void GenerarCuotasPendientesDelMes(string? genero, int? idCategoria, int mes, int anio);
        void GenerarCuotasPendientesDelMes(SqlConnection conexion, SqlTransaction transaccion, string? genero, int? idCategoria, int mes, int anio);
    }
}
