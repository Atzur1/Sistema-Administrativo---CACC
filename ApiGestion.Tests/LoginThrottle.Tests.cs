namespace ApiGestion.Tests;

using ApiGestion.Services;
using DaoLibrary;

// Límite de contraseñas equivocadas por cuenta, además del límite por IP del login.
public class LoginThrottleTests
{
    private sealed class RelojDePrueba : TimeProvider
    {
        private DateTimeOffset _ahora = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _ahora;
        public void Avanzar(TimeSpan tiempo) => _ahora += tiempo;
    }

    private static void FallarVeces(LoginThrottle throttle, string clave, int veces)
    {
        for (var i = 0; i < veces; i++) throttle.RegistrarFallo(clave);
    }

    [Fact]
    public void LaCuentaSeBloqueaAlLlegarAlMaximoDeFallos()
    {
        var throttle = new LoginThrottle(new RelojDePrueba(), new IntentosEnMemoria());

        FallarVeces(throttle, "30111222", LoginThrottle.MaximoFallos - 1);
        Assert.False(throttle.Bloqueado("30111222"));

        throttle.RegistrarFallo("30111222");
        Assert.True(throttle.Bloqueado("30111222"));
    }

    [Fact]
    public void BloquearUnaCuentaNoAfectaALasDemas()
    {
        var throttle = new LoginThrottle(new RelojDePrueba(), new IntentosEnMemoria());

        FallarVeces(throttle, "30111222", LoginThrottle.MaximoFallos);

        Assert.False(throttle.Bloqueado("41522398"));
    }

    [Fact]
    public void PasadaLaVentanaLaCuentaVuelveAPoderIntentar()
    {
        var reloj = new RelojDePrueba();
        var throttle = new LoginThrottle(reloj, new IntentosEnMemoria());
        FallarVeces(throttle, "30111222", LoginThrottle.MaximoFallos);

        reloj.Avanzar(LoginThrottle.Ventana - TimeSpan.FromSeconds(1));
        Assert.True(throttle.Bloqueado("30111222"));

        reloj.Avanzar(TimeSpan.FromSeconds(1));
        Assert.False(throttle.Bloqueado("30111222"));
    }

    // HU-086: la ventana corre desde el fallo que completa el máximo, no desde el primero.
    [Fact]
    public void ConFallosRepartidosEnLaVentana_ElDecimoBloqueaLos15MinutosCompletos()
    {
        var reloj = new RelojDePrueba();
        var throttle = new LoginThrottle(reloj, new IntentosEnMemoria());

        // 9 fallos en 14 minutos (uno cada ~1,5 minutos) y el décimo al minuto 14.
        for (var i = 0; i < LoginThrottle.MaximoFallos - 1; i++)
        {
            throttle.RegistrarFallo("30111222");
            reloj.Avanzar(TimeSpan.FromSeconds(100));
        }
        reloj.Avanzar(TimeSpan.FromMinutes(14) - TimeSpan.FromSeconds(100 * (LoginThrottle.MaximoFallos - 1)));
        throttle.RegistrarFallo("30111222");
        Assert.True(throttle.Bloqueado("30111222"));

        reloj.Avanzar(LoginThrottle.Ventana - TimeSpan.FromSeconds(1));
        Assert.True(throttle.Bloqueado("30111222"));

        reloj.Avanzar(TimeSpan.FromSeconds(1));
        Assert.False(throttle.Bloqueado("30111222"));
    }

    [Fact]
    public void UnFalloDespuesDeUnaVentanaSinFallosVuelveAContarDesdeUno()
    {
        var reloj = new RelojDePrueba();
        var throttle = new LoginThrottle(reloj, new IntentosEnMemoria());
        FallarVeces(throttle, "30111222", LoginThrottle.MaximoFallos - 1);

        reloj.Avanzar(LoginThrottle.Ventana);
        FallarVeces(throttle, "30111222", LoginThrottle.MaximoFallos - 1);

        Assert.False(throttle.Bloqueado("30111222"));
    }

    [Fact]
    public void UnIngresoCorrectoReiniciaLaCuenta()
    {
        var throttle = new LoginThrottle(new RelojDePrueba(), new IntentosEnMemoria());
        FallarVeces(throttle, "30111222", LoginThrottle.MaximoFallos - 1);

        throttle.Limpiar("30111222");
        FallarVeces(throttle, "30111222", LoginThrottle.MaximoFallos - 1);

        Assert.False(throttle.Bloqueado("30111222"));
    }

    [Fact]
    public void LosFallosViejosNoSeAcumulanConLosNuevos()
    {
        var reloj = new RelojDePrueba();
        var throttle = new LoginThrottle(reloj, new IntentosEnMemoria());
        FallarVeces(throttle, "30111222", LoginThrottle.MaximoFallos - 1);

        reloj.Avanzar(LoginThrottle.Ventana);
        throttle.RegistrarFallo("30111222");

        Assert.False(throttle.Bloqueado("30111222"));
    }

    [Fact]
    public void NoGuardaElIdentificadorEnClaro()
    {
        var store = new IntentosEnMemoria();
        new LoginThrottle(new RelojDePrueba(), store).RegistrarFallo("30111222");

        var clave = Assert.Single(store.Claves);
        Assert.DoesNotContain("30111222", clave);
        Assert.Equal(64, clave.Length);
    }

    // Mismo comportamiento que IntentosLoginDao, en memoria, para probar las reglas sin base.
    private sealed class IntentosEnMemoria : IIntentosLoginStore
    {
        private readonly Dictionary<string, (int Fallos, DateTimeOffset Desde)> _fallos = new();

        public IEnumerable<string> Claves => _fallos.Keys;

        public (int Fallos, DateTimeOffset Desde)? Obtener(string clave) => _fallos.TryGetValue(clave, out var e) ? e : null;

        public void RegistrarFallo(string clave, DateTimeOffset ahora, TimeSpan ventana) =>
            _fallos[clave] = _fallos.TryGetValue(clave, out var e) && ahora - e.Desde < ventana ? (e.Fallos + 1, ahora) : (1, ahora);

        public void Limpiar(string clave) => _fallos.Remove(clave);
    }
}

// La tabla INTENTOS_LOGIN contra una base real: los fallos sobreviven a una instancia nueva del límite
// (como un reinicio de la API) y vencen con la ventana.
[Collection(BaseDeDatosCollection.Nombre)]
public class IntentosLoginDaoIntegrationTests
{
    private sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }

    // HU-086 contra la base real: 9 fallos repartidos en 14 minutos y el décimo bloquea los 15 minutos completos.
    [IntegracionFact]
    public void ElDecimoFalloBloqueaLaVentanaCompleta_AunqueLosAnterioresEstenRepartidos()
    {
        var dao = new IntentosLoginDao(Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable)!);
        string identificador = "prueba-" + Guid.NewGuid();
        var inicio = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
        try
        {
            for (var i = 0; i < LoginThrottle.MaximoFallos - 1; i++)
                new LoginThrottle(new RelojFijo(inicio.AddSeconds(100 * i)), dao).RegistrarFallo(identificador);

            var decimo = inicio.AddMinutes(14);
            new LoginThrottle(new RelojFijo(decimo), dao).RegistrarFallo(identificador);

            // Al minuto 16 el primer fallo ya salió de la ventana, pero la cuenta sigue bloqueada.
            Assert.True(new LoginThrottle(new RelojFijo(inicio.AddMinutes(16)), dao).Bloqueado(identificador));
            Assert.True(new LoginThrottle(new RelojFijo(decimo + LoginThrottle.Ventana - TimeSpan.FromSeconds(1)), dao).Bloqueado(identificador));
            Assert.False(new LoginThrottle(new RelojFijo(decimo + LoginThrottle.Ventana), dao).Bloqueado(identificador));
        }
        finally
        {
            dao.Limpiar(LoginThrottle.Clave(identificador));
        }
    }

    [IntegracionFact]
    public void LosFallosSobrevivenAUnReinicioYVencenConLaVentana()
    {
        var dao = new IntentosLoginDao(Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable)!);
        string identificador = "prueba-" + Guid.NewGuid();
        var ahora = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
        try
        {
            var antes = new LoginThrottle(new RelojFijo(ahora), dao);
            for (var i = 0; i < LoginThrottle.MaximoFallos; i++) antes.RegistrarFallo(identificador);

            // Una instancia nueva (la API reiniciada) ve el bloqueo.
            Assert.True(new LoginThrottle(new RelojFijo(ahora.AddMinutes(1)), dao).Bloqueado(identificador));
            Assert.False(new LoginThrottle(new RelojFijo(ahora + LoginThrottle.Ventana), dao).Bloqueado(identificador));

            // Un fallo después de la ventana vuelve a contar desde 1.
            new LoginThrottle(new RelojFijo(ahora + LoginThrottle.Ventana), dao).RegistrarFallo(identificador);
            Assert.Equal(1, dao.Obtener(LoginThrottle.Clave(identificador))!.Value.Fallos);
        }
        finally
        {
            dao.Limpiar(LoginThrottle.Clave(identificador));
        }
    }
}
