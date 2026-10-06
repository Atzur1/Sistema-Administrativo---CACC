namespace ApiGestion.Tests;

using ApiGestion.Services;

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
        var throttle = new LoginThrottle(new RelojDePrueba());

        FallarVeces(throttle, "30111222", LoginThrottle.MaximoFallos - 1);
        Assert.False(throttle.Bloqueado("30111222"));

        throttle.RegistrarFallo("30111222");
        Assert.True(throttle.Bloqueado("30111222"));
    }

    [Fact]
    public void BloquearUnaCuentaNoAfectaALasDemas()
    {
        var throttle = new LoginThrottle(new RelojDePrueba());

        FallarVeces(throttle, "30111222", LoginThrottle.MaximoFallos);

        Assert.False(throttle.Bloqueado("41522398"));
    }

    [Fact]
    public void PasadaLaVentanaLaCuentaVuelveAPoderIntentar()
    {
        var reloj = new RelojDePrueba();
        var throttle = new LoginThrottle(reloj);
        FallarVeces(throttle, "30111222", LoginThrottle.MaximoFallos);

        reloj.Avanzar(LoginThrottle.Ventana - TimeSpan.FromSeconds(1));
        Assert.True(throttle.Bloqueado("30111222"));

        reloj.Avanzar(TimeSpan.FromSeconds(1));
        Assert.False(throttle.Bloqueado("30111222"));
    }

    [Fact]
    public void UnIngresoCorrectoReiniciaLaCuenta()
    {
        var throttle = new LoginThrottle(new RelojDePrueba());
        FallarVeces(throttle, "30111222", LoginThrottle.MaximoFallos - 1);

        throttle.Limpiar("30111222");
        FallarVeces(throttle, "30111222", LoginThrottle.MaximoFallos - 1);

        Assert.False(throttle.Bloqueado("30111222"));
    }

    [Fact]
    public void LosFallosViejosNoSeAcumulanConLosNuevos()
    {
        var reloj = new RelojDePrueba();
        var throttle = new LoginThrottle(reloj);
        FallarVeces(throttle, "30111222", LoginThrottle.MaximoFallos - 1);

        reloj.Avanzar(LoginThrottle.Ventana);
        throttle.RegistrarFallo("30111222");

        Assert.False(throttle.Bloqueado("30111222"));
    }
}
