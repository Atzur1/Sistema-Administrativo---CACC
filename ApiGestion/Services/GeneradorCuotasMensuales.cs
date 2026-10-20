using DaoLibrary;
using EntityLibrary;

namespace ApiGestion.Services;

// Genera sola la cuota mensual de cada jugador, sin que nadie tenga que cargar nada. La cuota de cada
// mes se genera el día 10 (CalendarioCuotas): antes del 10 se asegura la del mes anterior y desde el 10
// la del mes en curso.
// Cada jugador paga el arancel que le corresponde por jerarquía (el de su categoría si tiene uno
// vigente; si no, el de su género), así que si nadie programó un arancel nuevo se sigue cobrando
// el vigente, y si se programó uno, el mes en que entra en vigencia ya toma el cambio.
//
// Corre al iniciar la API y cada pocas horas. Es seguro repetirlo: GenerarCuotasPendientesDelMes
// solo inserta para quien todavía no tiene la cuota de ese mes (por ejemplo, quien la pagó por
// adelantado). Nunca genera meses más viejos: si la API estuvo apagada, los meses que quedaron sin
// cuota los avisa el Resumen General y los genera el SuperAdmin (CuotasFaltantesService).
public sealed class GeneradorCuotasMensuales : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GeneradorCuotasMensuales> _logger;

    public GeneradorCuotasMensuales(IServiceScopeFactory scopeFactory, ILogger<GeneradorCuotasMensuales> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Intervalo);
        do
        {
            GenerarCuotasAlDia(RelojNegocio.Hoy);
        }
        while (await SiguienteTurnoAsync(timer, stoppingToken));
    }

    // Un error (por ejemplo, la base no disponible un momento) no tira la API: se registra y se
    // reintenta en el próximo turno.
    internal void GenerarCuotasAlDia(DateTime hoy)
    {
        var periodo = CalendarioCuotas.UltimoPeriodoEmitible(hoy);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var pagosDao = scope.ServiceProvider.GetRequiredService<IPagosDao>();
            pagosDao.GenerarCuotasPendientesDelMes(null, null, periodo.Month, periodo.Year);
            _logger.LogInformation("Cuotas del mes {Mes:00}/{Anio} verificadas.", periodo.Month, periodo.Year);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudieron generar las cuotas del mes {Mes:00}/{Anio}; se reintenta en el próximo turno.", periodo.Month, periodo.Year);
        }

        GenerarInscripcionesProgramadas();
    }

    // Inscripción que empezó a regir después de que el jugador entró: se le genera el día en que rige (ver
    // PlayerDAO.GenerarInscripcionesProgramadas). Va separada de las cuotas: un error en una no frena a la otra.
    internal void GenerarInscripcionesProgramadas()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            int generadas = scope.ServiceProvider.GetRequiredService<PlayerDAO>().GenerarInscripcionesProgramadas();
            if (generadas > 0) _logger.LogInformation("Se generaron {Cantidad} inscripciones que empezaron a regir.", generadas);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudieron generar las inscripciones programadas; se reintenta en el próximo turno.");
        }
    }

    private static async Task<bool> SiguienteTurnoAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
