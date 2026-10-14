namespace DaoLibrary;

using EntityLibrary;

// The ARANCELES and PAGOS operations available inside a transaction opened by ITariffTransactionDao. Every
// call runs on the same connection and transaction, which the caller never sees. Method names match
// ArancelesDao and PagosDao.
//
// genero / idCategoria narrow which players are reached (one of the two; both null = everyone).
// desde / hasta narrow the periods by fecha_vencimiento (hasta is exclusive; null = no limit).
// montoBase, when given, keeps only the fees issued for that amount, so the fees of one tariff are told
// apart from those of another tariff for the same destination.
public interface ITariffTransaction
{
    // A tariff is per gender OR per category: exactly one of the two is given.
    void ProgramarArancel(string? genero, int? idCategoria, decimal monto, DateTime vigenteDesde);

    Arancel? ObtenerPorId(int idArancel);

    // vigente_desde of the tariff the same destination already has in that month, or null.
    // Only one tariff per month and destination is allowed.
    DateTime? ObtenerVigenteDesdeEnMes(string? genero, int? idCategoria, int anio, int mes);

    // vigente_desde of the first tariff of the same destination after "desde", or null.
    DateTime? ObtenerSiguienteVigenteDesde(string? genero, int? idCategoria, DateTime desde);

    void EliminarArancel(int idArancel);

    // Issues the month's fee for whoever does not have it yet (see IPagosDao). Never overwrites existing ones.
    void GenerarCuotasPendientesDelMes(string? genero, int? idCategoria, int mes, int anio);

    // How many fees (player + period) already have a payment (a row with estado = 1).
    int ContarCuotasConPagos(string? genero, int? idCategoria, DateTime desde, DateTime? hasta, decimal? montoBase);

    // Deletes the pending fees that have no payment at all and returns how many were deleted.
    int EliminarCuotasPendientesSinPagos(string? genero, int? idCategoria, DateTime desde, DateTime? hasta, decimal? montoBase);
}
