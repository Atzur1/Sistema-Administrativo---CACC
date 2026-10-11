namespace ApiGestion.Tests;

using ApiGestion.Services;
using DaoLibrary;
using EntityLibrary;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

// Regla del club: la cuota de cada mes se genera el día 10 y vence el 10 del mes siguiente, quedando vencida
// ese mismo día. Ej.: la de octubre se genera el 10/10 y está vencida desde el 10/11.
public class CalendarioCuotasTests
{
    [Theory]
    [InlineData(2026, 10, "2026-11-10")]
    [InlineData(2026, 12, "2027-01-10")]
    [InlineData(2028, 1, "2028-02-10")]
    public void FechaVencimiento_EsElDia10DelMesSiguiente(int anio, int mes, string esperado)
    {
        Assert.Equal(DateTime.Parse(esperado), CalendarioCuotas.FechaVencimiento(new DateTime(anio, mes, 1)));
    }

    [Theory]
    [InlineData(2026, 11, 9, false)]
    [InlineData(2026, 11, 10, true)]
    [InlineData(2026, 12, 1, true)]
    [InlineData(2026, 10, 31, false)]
    public void LaCuotaDeOctubre_VenceElMismo10DeNoviembre(int anio, int mes, int dia, bool vencida)
    {
        Assert.Equal(vencida, CalendarioCuotas.EstaVencida(new DateTime(2026, 10, 1), new DateTime(anio, mes, dia)));
    }

    [Theory]
    [InlineData(2026, 10, 9, 2026, 9)]
    [InlineData(2026, 10, 10, 2026, 10)]
    [InlineData(2026, 10, 31, 2026, 10)]
    [InlineData(2027, 1, 5, 2026, 12)]
    public void UltimoPeriodoEmitible_AntesDel10EsElMesAnterior(int anio, int mes, int dia, int anioEsperado, int mesEsperado)
    {
        Assert.Equal(new DateTime(anioEsperado, mesEsperado, 1), CalendarioCuotas.UltimoPeriodoEmitible(new DateTime(anio, mes, dia)));
    }

    // El generador nunca adelanta la cuota del mes: antes del 10 solo asegura la del mes anterior.
    [Theory]
    [InlineData(5, 9)]
    [InlineData(10, 10)]
    public void Generador_EmiteElUltimoPeriodoEmitible(int dia, int mesEsperado)
    {
        var pagos = new PagosDaoQueRegistraGeneraciones();
        var servicios = new ServiceCollection().AddScoped<IPagosDao>(_ => pagos.Proxy).BuildServiceProvider();
        var generador = new GeneradorCuotasMensuales(
            servicios.GetRequiredService<IServiceScopeFactory>(), NullLogger<GeneradorCuotasMensuales>.Instance);

        generador.GenerarCuotasAlDia(new DateTime(2026, 10, dia));

        Assert.Equal((mesEsperado, 2026), Assert.Single(pagos.Generados));
    }

    // IPagosDao tiene muchos métodos: un DispatchProxy registra solo el que interesa y rechaza el resto.
    private sealed class PagosDaoQueRegistraGeneraciones
    {
        public List<(int Mes, int Anio)> Generados { get; } = new();
        public IPagosDao Proxy { get; }

        public PagosDaoQueRegistraGeneraciones()
        {
            Proxy = System.Reflection.DispatchProxy.Create<IPagosDao, Interceptor>();
            ((Interceptor)(object)Proxy).Destino = this;
        }

        public class Interceptor : System.Reflection.DispatchProxy
        {
            public PagosDaoQueRegistraGeneraciones? Destino { get; set; }

            protected override object? Invoke(System.Reflection.MethodInfo? metodo, object?[]? args)
            {
                if (metodo?.Name == nameof(IPagosDao.GenerarCuotasPendientesDelMes))
                {
                    Destino!.Generados.Add(((int)args![2]!, (int)args[3]!));
                    return null;
                }
                throw new NotSupportedException(metodo?.Name);
            }
        }
    }
}
