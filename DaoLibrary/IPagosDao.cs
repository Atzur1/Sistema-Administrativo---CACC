using EntityLibrary;

namespace DaoLibrary
{
    // Lecturas y operaciones de PAGOS que no necesitan una transacción del llamador. Las que sí (registrar,
    // cobrar y anular pagos) se hacen con IPaymentTransactionDao, que abre la transacción en esta capa.
    public interface IPagosDao
    {
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

        // Tabla "Pagos registrados": filtros y página resueltos en la base.
        PagosRegistradosPagina ObtenerPagosRegistrados(FiltroPagosRegistrados filtro);

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
    }
}
