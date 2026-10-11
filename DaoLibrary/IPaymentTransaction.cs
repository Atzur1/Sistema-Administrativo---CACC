namespace DaoLibrary;

using EntityLibrary;

// The PAGOS operations available inside a transaction opened by IPaymentTransactionDao. Every call runs on
// the same connection and transaction, which the caller never sees. Method names match PagosDao.
public interface IPaymentTransaction
{
    // HU-025: a payment checks that the player exists inside the same transaction.
    bool ExisteJugador(int idJugador);

    // A PAID row of the player for that month/year (blocks charging a period twice). WITH (UPDLOCK) so two
    // concurrent requests cannot both pass the check.
    Pago? ObtenerPagoAbonadoDeJugadorEnPeriodo(int idJugador, int mes, int anio);

    // The PENDING fee (Estado = false) of the player for that month/year, if any, to record a full or partial
    // payment against it. WITH (UPDLOCK), like the one above.
    Pago? ObtenerPagoPendienteDeJugadorEnPeriodo(int idJugador, int mes, int anio);

    // The active Becados y Descuentos benefit (JUGADORES_DESCUENTOS) covering the month of that due date, used
    // to adjust a fee that was already issued BEFORE the benefit was granted.
    DescuentoAplicable? ObtenerDescuentoAplicableEnPeriodo(int idJugador, DateTime fechaVencimiento);

    // Issues ONE player's fee for that period with the tariff that applies, if there is none yet. False when
    // nothing was issued (no such player, already issued, not in the club that month, or no tariff in force).
    bool EmitirCuotaDeJugador(int idJugador, int mes, int anio);

    // PAGOS.PK_id_pago has no IDENTITY: the id is computed inside this same transaction.
    int InsertarPago(Pago pago);

    // Lowers the balance of a pending fee after a partial payment (it stays Estado = false).
    void ActualizarSaldoPendiente(int idPago, decimal nuevoMonto);

    // Deletes the pending fee once a payment covers it in full.
    void EliminarPago(int idPago);

    // Reads with UPDLOCK/HOLDLOCK: a second session charging the same fees waits for this commit/rollback
    // and then sees their real state.
    IReadOnlyList<Pago> ObtenerPagosPorId(IEnumerable<int> idsPago);

    void ActualizarMontoCobroConDescuento(int idPago, int idDescuento, decimal montoFinal);

    // HU-025: marks the player's pending fees as paid with method, payment date, registration time (SQL Server
    // clock) and operator. Throws InvalidOperationException if one is no longer pending or belongs to someone
    // else, so the whole transaction rolls back. Returns the registration time that was stored.
    DateTime MarcarPagosComoAbonados(int idJugador, IEnumerable<int> idsPago, DateTime fechaPago, string metodoPago, int idUsuarioRegistro);

    // The PAGOS row, locked (UPDLOCK/HOLDLOCK), with concept, operator and registration time; null if missing.
    Pago? ObtenerPagoParaAnular(int idPago);

    // Every row (pending and payments) of the same fee or enrollment as that payment, locked: same player and
    // concept and, for fees, the same fecha_vencimiento month.
    IReadOnlyList<Pago> ObtenerFilasDeLaMismaDeuda(Pago pago);

    // Writes the permanent void record (PAGOS_ANULADOS) and deletes the payment from PAGOS.
    void RegistrarAnulacionYEliminarAbono(Pago abono, string motivo, int idUsuarioAnulacion);
}
