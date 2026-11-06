namespace ApiGestion.Tests;

using ServiceLibrary;

// Contraseñas nuevas del portal: 10+ caracteres, letras y números, sin datos personales y no comunes.
public class PoliticaContraseniaTests
{
    private static readonly DatosTitular Camila = new("Camila Andrea", "Pérez Gómez", "30.111.222", "camila.perez@cacc.test");

    [Theory]
    [InlineData("Tribuna-Norte-77")]
    [InlineData("verde y blanco 1931")]
    [InlineData("k7kkDGVEDiSk7")]
    public void UnaContraseniaQueCumpleTodo_EsAceptada(string contrasenia)
    {
        Assert.Empty(PoliticaContrasenia.Validar(contrasenia, Camila));
    }

    [Fact]
    public void MenosDe10Caracteres_SeRechaza()
    {
        Assert.Contains(PoliticaContrasenia.Validar("Abc12345x", Camila), p => p.Contains("10 caracteres"));
    }

    [Theory]
    [InlineData("solamenteletras")]
    [InlineData("12345678901234")]
    public void SinLetrasYNumeros_SeRechaza(string contrasenia)
    {
        Assert.Contains(PoliticaContrasenia.Validar(contrasenia, Camila), p => p.Contains("letras y números"));
    }

    [Theory]
    [InlineData("Clave30111222")]      // el DNI, aunque esté cargado con puntos
    [InlineData("MiClave-Perez-2026")] // el apellido, sin el acento
    [InlineData("andrea12345")]        // el segundo nombre
    [InlineData("camilaperez99")]      // el usuario del correo
    public void ConDatosPersonales_SeRechaza(string contrasenia)
    {
        Assert.Contains(PoliticaContrasenia.Validar(contrasenia, Camila), p => p.Contains("DNI"));
    }

    [Theory]
    [InlineData("Camioneros2026!")]
    [InlineData("password123")]
    [InlineData("1q2w3e4r5t")]
    public void UnaContraseniaComun_SeRechaza(string contrasenia)
    {
        Assert.Contains(PoliticaContrasenia.Validar(contrasenia, Camila), p => p.Contains("común"));
    }

    // HU-087: el nombre del club no puede ir en ninguna parte de la contraseña, con cualquier separador.
    [Theory]
    [InlineData("Camioneros-1931")]
    [InlineData("ClubCamioneros-99")]
    [InlineData("Camioneros2027")]
    [InlineData("C.A.C.C.2026")]
    [InlineData("Mi-cacc-Norte-77")]
    public void ConElNombreDelClub_SeRechaza(string contrasenia)
    {
        Assert.Contains(PoliticaContrasenia.Validar(contrasenia, Camila), p => p.Contains("nombre del club"));
    }

    // HU-087: apellido compuesto con guion, cada tramo cuenta como dato personal.
    [Theory]
    [InlineData("Perez-norte-77")]
    [InlineData("Gomez-norte-77")]
    public void ConUnTramoDelApellidoCompuestoConGuion_SeRechaza(string contrasenia)
    {
        var titular = new DatosTitular("Camila", "Pérez-Gómez", "30111222", null);

        Assert.Contains(PoliticaContrasenia.Validar(contrasenia, titular), p => p.Contains("DNI"));
    }

    // HU-087: las palabras más usadas caen aunque cambie el número del final.
    [Theory]
    [InlineData("Qwerty12345")]
    [InlineData("Argentina2027")]
    [InlineData("Password2027")]
    [InlineData("Bienvenido99")]
    public void UnaPalabraComunConOtrosNumerosAlFinal_SeRechaza(string contrasenia)
    {
        Assert.Contains(PoliticaContrasenia.Validar(contrasenia, Camila), p => p.Contains("común"));
    }

    // HU-087 (QA 30/10): la sigla "cacc" solo cuenta cuando está sola o con puntos; no cuando es la unión de dos palabras.
    [Theory]
    [InlineData("Vaca-Ccuatro-7")]
    [InlineData("Vaca Ccuatro 77")]
    [InlineData("Argentina-campeon-2022")]
    public void UnaContraseniaQueSoloJuntaLetrasDeOtrasPalabras_NoSeRechazaPorElClub(string contrasenia)
    {
        Assert.Empty(PoliticaContrasenia.Validar(contrasenia, Camila));
    }

    [Theory]
    [InlineData("Camioneros-1931")]
    [InlineData("C.A.C.C.2026")]
    [InlineData("Qwerty12345")]
    [InlineData("ClubCACC-77")]
    public void LasContraseniasQueQAPidioRechazar_SeRechazan(string contrasenia)
    {
        Assert.NotEmpty(PoliticaContrasenia.Validar(contrasenia, Camila));
    }

    [Fact]
    public void ElApellidoCompuestoConGuion_SeRechazaPorSusTramos()
    {
        var titular = new DatosTitular("Camila", "Pérez-Gómez", "30111222", null);

        Assert.NotEmpty(PoliticaContrasenia.Validar("Gomez-norte-77", titular));
    }

    [Fact]
    public void UnaPalabraComunDentroDeUnaFraseLarga_NoSeRechaza()
    {
        Assert.Empty(PoliticaContrasenia.Validar("Argentina-campeon-2022", Camila));
    }

    [Fact]
    public void PartesMuyCortasDelNombreNoSeControlan()
    {
        // "Li" como apellido no puede impedir cualquier contraseña con esas dos letras seguidas.
        Assert.Empty(PoliticaContrasenia.Validar("Olivares-Norte-77", new DatosTitular("Ana", "Li", null, null)));
    }
}
