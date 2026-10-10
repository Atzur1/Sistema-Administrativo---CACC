using DaoLibrary;
using EntityLibrary;

namespace ApiGestion.Services;

// Genera sola la cuota del mes en curso de cada jugador, sin que nadie tenga que cargar nada.
// Cada jugador paga el arancel que le corresponde por jerarquía (el de su categoría si tiene uno
// vigente; si no, el de su género), así que si nadie programó un arancel nuevo se sigue cobrando
// el vigente, y si se programó uno, el mes en que entra en vigencia ya toma el cambio.
//
// Corre al iniciar la API y cada pocas horas. Es seguro repetirlo: GenerarCuotasPendientesDelMes
// solo inserta para quien todavía no tiene la cuota de ese mes. Genera solo el mes en curso,
// nunca meses atrasados, para no inventar deuda retroactiva si la API estuvo apagada.
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
            GenerarMesActual(RelojNegocio.Ahora);
        }
        while (await SiguienteTurnoAsync(timer, stoppingToken));
    }

    // Un error (por ejemplo, la base no disponible un momento) no tira la API: se registra y se
    // reintenta en el próximo turno.
    internal void GenerarMesActual(DateTime hoy)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var pagosDao = scope.ServiceProvider.GetRequiredService<IPagosDao>();
            pagosDao.GenerarCuotasPendientesDelMes(null, null, hoy.Month, hoy.Year);
            _logger.LogInformation("Cuotas del mes {Mes:00}/{Anio} verificadas.", hoy.Month, hoy.Year);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudieron generar las cuotas del mes {Mes:00}/{Anio}; se reintenta en el próximo turno.", hoy.Month, hoy.Year);
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
