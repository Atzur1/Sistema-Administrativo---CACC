namespace DaoLibrary;

using EntityLibrary;

// HU-106: lo que se lee para calcular el impacto de un cambio de arancel. Lo implementan ArancelesDao (la
// simulación, sin transacción) e ITariffTransaction (la confirmación, dentro de la transacción que después graba),
// así las dos corren exactamente las mismas consultas. Ninguna escribe.
//
// genero / idCategoria: el destino del arancel, uno de los dos (como en ITariffTransaction).
public interface ILecturaImpactoArancel
{
    // Arancel del destino que rige en esa fecha (el de vigente_desde más reciente que ya empezó), o null. Es la base
    // de un aumento por porcentaje o por monto fijo (HU-069).
    decimal? ObtenerMontoVigenteDelDestino(string? genero, int? idCategoria, DateTime fecha);

    // vigente_desde of the tariff the same destination already has in that month, or null.
    // Only one tariff per month and destination is allowed.
    DateTime? ObtenerVigenteDesdeEnMes(string? genero, int? idCategoria, int anio, int mes);

    // vigente_desde of the first tariff of the same destination after "desde", or null.
    DateTime? ObtenerSiguienteVigenteDesde(string? genero, int? idCategoria, DateTime desde);

    // La cuota del mes de vigenteDesde con y sin el arancel nuevo, para cada jugador del alcance que está en el club
    // ese mes: misma jerarquía (SqlCuotaMensual.ArancelDelJugador) y misma elegibilidad (JugadorEnElClub) que el
    // generador de cuotas. El arancel nuevo no se inserta: se suma a ARANCELES dentro de la consulta.
    ImpactoArancelConsulta ConsultarImpacto(string? genero, int? idCategoria, decimal montoNuevo, DateTime vigenteDesde);

    // How many fees (player + period) already have a payment (a row with estado = 1).
    int ContarCuotasConPagos(string? genero, int? idCategoria, DateTime desde, DateTime? hasta, decimal? montoBase);

    // Cuotas pendientes sin ningún pago: las que EliminarCuotasPendientesSinPagos quitaría (mismo filtro).
    int ContarCuotasPendientesSinPagos(string? genero, int? idCategoria, DateTime desde, DateTime? hasta);
}
