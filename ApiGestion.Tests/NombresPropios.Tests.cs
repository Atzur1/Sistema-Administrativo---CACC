namespace ApiGestion.Tests;

using EntityLibrary;

public class NombresPropiosTests
{
    [Theory]
    [InlineData("JUAN CARLOS", "Juan Carlos")]
    [InlineData("juan carlos", "Juan Carlos")]
    [InlineData("jUaN cArLoS", "Juan Carlos")]
    [InlineData("Juan Carlos", "Juan Carlos")]
    [InlineData("  PEREZ   GOMEZ  ", "Perez Gomez")]
    public void Deja_la_primera_letra_de_cada_palabra_en_mayuscula(string escrito, string esperado)
        => Assert.Equal(esperado, NombresPropios.Normalizar(escrito));

    [Theory]
    [InlineData("MARÍA DE LOS ÁNGELES", "María de los Ángeles")]
    [InlineData("josé del valle", "José del Valle")]
    [InlineData("ANA Y LUCÍA", "Ana y Lucía")]
    public void Las_particulas_van_en_minuscula_salvo_al_principio(string escrito, string esperado)
        => Assert.Equal(esperado, NombresPropios.Normalizar(escrito));

    [Theory]
    [InlineData("DE LA FUENTE", "De la Fuente")]
    [InlineData("DEL VALLE", "Del Valle")]
    public void Una_particula_al_principio_lleva_mayuscula(string escrito, string esperado)
        => Assert.Equal(esperado, NombresPropios.Normalizar(escrito));

    [Theory]
    [InlineData("PÉREZ-GÓMEZ", "Pérez-Gómez")]
    [InlineData("d'angelo", "D'Angelo")]
    [InlineData("MCDONALD", "McDonald")]
    [InlineData("PÉREZ-MCDONALD", "Pérez-McDonald")]
    [InlineData("mcdonald-mcgregor", "McDonald-McGregor")]
    [InlineData("o'mcgrath", "O'McGrath")]
    [InlineData("MCA", "McA")]
    [InlineData("ñandú NÚÑEZ", "Ñandú Núñez")]
    public void Respeta_guiones_apostrofos_acentos_y_la_enie(string escrito, string esperado)
        => Assert.Equal(esperado, NombresPropios.Normalizar(escrito));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Un_valor_vacio_queda_vacio(string? escrito)
        => Assert.Equal(string.Empty, NombresPropios.Normalizar(escrito));

    [Fact]
    public void Es_idempotente()
    {
        string una = NombresPropios.Normalizar("MARÍA DE LOS ÁNGELES MCDONALD-PÉREZ");
        Assert.Equal(una, NombresPropios.Normalizar(una));
    }
}
