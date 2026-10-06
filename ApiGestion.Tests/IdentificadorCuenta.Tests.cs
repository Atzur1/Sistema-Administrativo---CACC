namespace ApiGestion.Tests;

using DaoLibrary;

// El usuario del login es el DNI (y, mientras dura la transición, el correo). Esto fija qué se interpreta como qué.
public class IdentificadorCuentaTests
{
    [Theory]
    [InlineData("30111222", "30111222")]
    [InlineData("30.111.222", "30111222")]
    [InlineData(" 30 111 222 ", "30111222")]
    [InlineData("30-111-222", "30111222")]
    [InlineData("1234567", "1234567")]
    public void ElDniSeNormalizaASoloDigitos(string escrito, string esperado)
    {
        var identificador = IdentificadorCuenta.Interpretar(escrito);

        Assert.NotNull(identificador);
        Assert.False(identificador.Value.EsCorreo);
        Assert.Equal(esperado, identificador.Value.Valor);
    }

    [Theory]
    [InlineData("Camila@CACC.test", "camila@cacc.test")]
    [InlineData("  admin@cacc.com ", "admin@cacc.com")]
    public void UnTextoConArrobaEsUnCorreoYSeGuardaEnMinusculas(string escrito, string esperado)
    {
        var identificador = IdentificadorCuenta.Interpretar(escrito);

        Assert.NotNull(identificador);
        Assert.True(identificador.Value.EsCorreo);
        Assert.Equal(esperado, identificador.Value.Valor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]        // muy corto para ser un DNI
    [InlineData("1234567890")]   // demasiado largo
    [InlineData("30111A22")]     // una letra no es un DNI
    [InlineData("admin")]        // ni correo ni DNI
    [InlineData("30111222 OR 1=1")]
    public void LoQueNoEsNiDniNiCorreoNoBuscaNada(string? escrito)
    {
        Assert.Null(IdentificadorCuenta.Interpretar(escrito));
    }

    [Fact]
    public void UnDniNuncaSeConfundeConUnCorreo()
    {
        Assert.False(IdentificadorCuenta.Interpretar("30111222")!.Value.EsCorreo);
        Assert.True(IdentificadorCuenta.Interpretar("30111222@cacc.test")!.Value.EsCorreo);
    }
}
