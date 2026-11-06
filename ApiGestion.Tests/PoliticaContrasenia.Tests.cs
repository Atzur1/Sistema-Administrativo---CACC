namespace ApiGestion.Tests;

using ServiceLibrary;

// HU-087: contraseñas nuevas del portal (activación y recuperación). 10+ caracteres, al menos una letra y un número,
// sin el DNI, el nombre, el apellido ni el usuario del correo de la persona, no común y sin el nombre del club. Los datos
// personales se comparan sin mayúsculas, acentos, puntos ni otros símbolos.
public class PoliticaContraseniaTests
{
    private static readonly DatosTitular Camila = new("Camila Andrea", "Pérez Gómez", "30.111.222", "camila.perez@cacc.test");

    // El ejemplo de la HU: el nombre y el usuario del correo no coinciden (José / juan.perez).
    private static readonly DatosTitular Jose = new("José", "Pérez", "40123456", "juan.perez@dominio.com");

    private static IReadOnlyList<string> Validar(string contrasenia, DatosTitular? titular = null) =>
        PoliticaContrasenia.Validar(contrasenia, titular ?? Camila);

    // ===== 15. Contraseña válida =====

    [Theory]
    [InlineData("Tribuna-Norte-77")]
    [InlineData("verde y blanco 1931")]
    [InlineData("k7kkDGVEDiSk7")]
    public void UnaContraseniaQueCumpleTodo_EsAceptada(string contrasenia)
    {
        Assert.Empty(Validar(contrasenia));
    }

    // ===== 1 y 2. Longitud =====

    [Fact]
    public void MenosDe10Caracteres_SeRechaza()
    {
        Assert.Equal(9, "Tribuna-7".Length);
        Assert.Equal(new[] { "Tiene que tener al menos 10 caracteres." }, Validar("Tribuna-7"));
    }

    [Fact]
    public void Exactamente10Caracteres_SeAcepta()
    {
        Assert.Equal(10, "Tribuna-7k".Length);
        Assert.Empty(Validar("Tribuna-7k"));
    }

    [Fact]
    public void MasDe10Caracteres_SeAcepta()
    {
        Assert.Empty(Validar("Tribuna-Norte-de-la-cancha-7"));
    }

    // ===== 3, 4 y 5. Letras y números =====

    [Fact]
    public void SoloLetras_PideUnNumero()
    {
        var problemas = Validar("abcdefghij");

        Assert.Contains("Tiene que contener al menos un número.", problemas);
        Assert.DoesNotContain("Tiene que contener al menos una letra.", problemas);
    }

    [Fact]
    public void SoloNumeros_PideUnaLetra()
    {
        Assert.Contains("Tiene que contener al menos una letra.", Validar("8642097531"));
    }

    [Fact]
    public void LetrasYNumeros_PasaEsaRegla()
    {
        var problemas = Validar("abcdefgh1X");

        Assert.DoesNotContain("Tiene que contener al menos una letra.", problemas);
        Assert.DoesNotContain("Tiene que contener al menos un número.", problemas);
    }

    // ===== 6. DNI =====

    [Theory]
    [InlineData("Clave30111222")]     // cargado con puntos, escrito sin
    [InlineData("Clave-30.111.222")]  // escrito con puntos
    public void ConElDni_SeRechaza(string contrasenia)
    {
        Assert.Contains("No puede contener tu DNI.", Validar(contrasenia));
    }

    // ===== 7, 8 y 9. Nombre =====

    [Theory]
    [InlineData("andrea12345")]    // el segundo nombre
    [InlineData("CAMILA-norte-7")] // otra mayúscula
    public void ConElNombre_SeRechaza(string contrasenia)
    {
        Assert.Contains("No puede contener tu nombre.", Validar(contrasenia));
    }

    [Theory]
    [InlineData("Jose-norte-77")] // el titular es "José": sin el acento también cae
    [InlineData("JOSÉ-norte-77")]
    public void ConElNombreSinAcentoOConMayusculas_SeRechaza(string contrasenia)
    {
        Assert.Contains("No puede contener tu nombre.", Validar(contrasenia, Jose));
    }

    // ===== 10. Apellido =====

    [Theory]
    [InlineData("MiClave-Perez-2026")] // sin el acento
    [InlineData("gomez-norte-77")]     // el segundo apellido
    public void ConElApellido_SeRechaza(string contrasenia)
    {
        Assert.Contains("No puede contener tu apellido.", Validar(contrasenia));
    }

    // HU-087 (rechazo de QA del 30/10): la sigla no se busca uniendo dos palabras distintas. "Vaca-Ccuatro-7" junta
    // "vaca" y "ccuatro", y eso no es el nombre del club.
    [Theory]
    [InlineData("Vaca-Ccuatro-7")]
    [InlineData("Alpaca Cceres 2031")]
    public void LaSiglaFormadaAlUnirDosPalabras_NoSeRechaza(string contrasenia)
    {
        Assert.Empty(PoliticaContrasenia.Validar(contrasenia, Camila));
    }

    // HU-087: apellido compuesto con guion, cada tramo cuenta como dato personal.
    [Theory]
    [InlineData("Perez-norte-77")]
    [InlineData("Gomez-norte-77")]
    public void ConUnTramoDelApellidoCompuestoConGuion_SeRechaza(string contrasenia)
    {
        var titular = new DatosTitular("Camila", "Pérez-Gómez", "30111222", null);

        Assert.Contains("No puede contener tu apellido.", Validar(contrasenia, titular));
    }

    // ===== 11 y 12. Usuario del correo =====

    [Theory]
    [InlineData("juanperez-77")]   // sin el punto del correo
    [InlineData("Juan.Perez-77")]  // con el punto y otras mayúsculas
    public void ConElUsuarioDelCorreo_SeRechaza(string contrasenia)
    {
        Assert.Contains("No puede contener el usuario de tu correo.", Validar(contrasenia, Jose));
    }

    [Fact]
    public void ElDominioDelCorreoNoSeControla()
    {
        Assert.Empty(Validar("dominio-norte-77", Jose));
    }

    // ===== 13. Contraseñas comunes =====

    [Theory]
    [InlineData("Camioneros2026!")]
    [InlineData("password123")]
    [InlineData("1234567890")]
    [InlineData("qwerty12345")]
    [InlineData("admin12345")]
    [InlineData("1q2w3e4r5t")]
    [InlineData("Password2027")] // una palabra común con otros números al final
    [InlineData("Bienvenido99")]
    public void UnaContraseniaComun_SeRechaza(string contrasenia)
    {
        Assert.Contains("Es una contraseña demasiado común. Elegí otra.", Validar(contrasenia));
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
        Assert.Empty(Validar("Argentina-campeon-2022"));
    }

    // ===== 14. Nombre del club =====

    [Theory]
    [InlineData("Camioneros2026!")]
    [InlineData("Camioneros-1931")]
    [InlineData("ClubCamioneros-99")]
    [InlineData("C.A.C.C.2026")]
    [InlineData("Mi-cacc-Norte-77")]
    [InlineData("Camioneros2027")]
    [InlineData("Cacc2026-Norte")]
    [InlineData("C-A-C-C-Norte-77")]
    [InlineData("Camio-neros-77")]
    public void ConElNombreDelClub_SeRechaza(string contrasenia)
    {
        Assert.Contains("No puede contener el nombre del club.", Validar(contrasenia));
    }

    // ===== Varios problemas a la vez: se informan todos, para corregir de una sola vez =====

    [Fact]
    public void VariosProblemas_SeInformanTodos()
    {
        var problemas = Validar("perez", Jose);

        Assert.Contains("Tiene que tener al menos 10 caracteres.", problemas);
        Assert.Contains("Tiene que contener al menos un número.", problemas);
        Assert.Contains("No puede contener tu apellido.", problemas);
    }

    // ===== Nombres cortos: no hay falsos rechazos por dos letras =====

    [Fact]
    public void PartesMuyCortasDelNombreNoSeControlan()
    {
        // "Li" como apellido no puede impedir cualquier contraseña con esas dos letras seguidas.
        Assert.Empty(Validar("Olivares-Norte-77", new DatosTitular("Ana", "Li", null, null)));
    }

    // ===== Normalización: solo para comparar =====

    [Theory]
    [InlineData("José", "jose")]
    [InlineData("PÉREZ", "perez")]
    [InlineData("juan.perez", "juanperez")]
    [InlineData("40.123.456", "40123456")]
    [InlineData("Muñoz", "munoz")]
    public void Normalizar_IgnoraMayusculasAcentosYPuntos(string texto, string esperado)
    {
        Assert.Equal(esperado, PoliticaContrasenia.Normalizar(texto));
    }

    [Fact]
    public void SinDatosDelTitular_SoloAplicaLasReglasGenerales()
    {
        Assert.Empty(Validar("Tribuna-Norte-77", new DatosTitular(null, null, null, null)));
    }
}
