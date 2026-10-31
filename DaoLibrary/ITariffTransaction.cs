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
//
// The reads (ILecturaImpactoArancel) are the same ones the HU-106 simulation runs outside a transaction, so the
// impact approved by the user is recalculated here, with the write lock held, right before saving.
public interface ITariffTransaction : ILecturaImpactoArancel
{
    // A tariff is per gender OR per category: exactly one of the two is given. impacto (HU-106): what the user
    // approved; it is stored with the tariff so the audit event records it. Null when there is nothing to record.
    void ProgramarArancel(string? genero, int? idCategoria, decimal monto, DateTime vigenteDesde, ImpactoArancel? impacto = null);

    Arancel? ObtenerPorId(int idArancel);

    void EliminarArancel(int idArancel);

    // Issues the month's fee for whoever does not have it yet (see IPagosDao). Never overwrites existing ones.
    void GenerarCuotasPendientesDelMes(string? genero, int? idCategoria, int mes, int anio);

    // Deletes the pending fees that have no payment at all and returns how many were deleted.
    int EliminarCuotasPendientesSinPagos(string? genero, int? idCategoria, DateTime desde, DateTime? hasta, decimal? montoBase);
}
