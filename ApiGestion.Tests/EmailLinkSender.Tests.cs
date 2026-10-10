namespace ApiGestion.Tests;

using System.Net;
using ApiGestion.Services;

// Los dos correos de acceso (primer ingreso y recuperación) tienen que distinguirse a simple vista y
// llevar a pantallas distintas: antes los dos terminaban en la misma pantalla "Restablecer contraseña".
public class EmailLinkSenderTests
{
    private const string BaseUrl = "https://portal.cacc.example";
    private const string Token = "abc-DEF_123";

    // El HTML codifica los acentos como entidades (&#233;); se decodifica para poder leer el texto.
    private static string Html(TipoCorreoAcceso tipo) => WebUtility.HtmlDecode(EmailLinkSender.CreateBody(BaseUrl, Token, tipo));

    [Fact]
    public void FirstAccessEmail_AsksToCreateThePasswordAndLinksToTheCreateScreen()
    {
        string html = Html(CorreosAcceso.CreaContrasena);

        Assert.Contains("Crea tu contraseña", html);
        Assert.Contains("Primer ingreso", html);
        Assert.Contains("Crear mi contraseña", html);
        Assert.Contains($"{BaseUrl}/crear-contrasena?token={Token}", html);
        Assert.DoesNotContain("restablecer-contrasena", html);
        Assert.DoesNotContain("Reestablece", html);
    }

    [Fact]
    public void ForgotPasswordEmail_AsksToResetThePasswordAndLinksToTheResetScreen()
    {
        string html = Html(CorreosAcceso.ReestableceContrasena);

        Assert.Contains("Reestablece tu contraseña", html);
        Assert.Contains("Recuperar acceso", html);
        Assert.Contains("Reestablecer mi contraseña", html);
        Assert.Contains($"{BaseUrl}/restablecer-contrasena?token={Token}", html);
        Assert.DoesNotContain("crear-contrasena", html);
        Assert.DoesNotContain("Crea tu contraseña", html);
    }

    [Fact]
    public void TheTwoEmails_HaveDifferentSubjectsAndOwnAdviceForAnExpiredLink()
    {
        TipoCorreoAcceso crea = CorreosAcceso.CreaContrasena;
        TipoCorreoAcceso reestablece = CorreosAcceso.ReestableceContrasena;

        Assert.NotEqual(crea.Asunto, reestablece.Asunto);
        Assert.StartsWith("Crea tu contraseña", crea.Asunto);
        Assert.StartsWith("Reestablece tu contraseña", reestablece.Asunto);

        // Quien recibe el primer ingreso no puede pedir un enlace nuevo solo: le toca al administrador.
        Assert.Contains("administrador", crea.NotaVencimiento);
        Assert.Contains("¿Olvidaste tu contraseña?", reestablece.NotaVencimiento);
        Assert.NotEqual(crea.NotaIgnorar, reestablece.NotaIgnorar);
    }

    [Fact]
    public void PlainTextVersion_AlsoDiffersAndCarriesTheRightLink()
    {
        string crea = EmailLinkSender.CreatePlainTextBody(BaseUrl, Token, CorreosAcceso.CreaContrasena);
        string reestablece = EmailLinkSender.CreatePlainTextBody(BaseUrl, Token, CorreosAcceso.ReestableceContrasena);

        Assert.StartsWith("CREA TU CONTRASEÑA", crea);
        Assert.Contains($"{BaseUrl}/crear-contrasena?token={Token}", crea);
        Assert.StartsWith("REESTABLECE TU CONTRASEÑA", reestablece);
        Assert.Contains($"{BaseUrl}/restablecer-contrasena?token={Token}", reestablece);
    }

    // Quien recibe el correo tiene que reconocer que es para él: se lo saluda por su nombre.
    [Fact]
    public void BothEmails_GreetThePersonByName()
    {
        string crea = WebUtility.HtmlDecode(EmailLinkSender.CreateBody(BaseUrl, Token, CorreosAcceso.CreaContrasena, "Laura"));
        string reestablece = WebUtility.HtmlDecode(EmailLinkSender.CreateBody(BaseUrl, Token, CorreosAcceso.ReestableceContrasena, "Laura"));
        string texto = EmailLinkSender.CreatePlainTextBody(BaseUrl, Token, CorreosAcceso.CreaContrasena, "Laura");

        Assert.Contains("Hola, Laura:", crea);
        Assert.Contains("Hola, Laura:", reestablece);
        Assert.Contains("Hola, Laura:", texto);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithoutAName_TheGreetingStaysGeneric(string? nombre)
    {
        Assert.Equal("Hola:", EmailLinkSender.Saludo(nombre));
    }

    [Fact]
    public void TheName_IsHtmlEncoded()
    {
        string html = EmailLinkSender.CreateBody(BaseUrl, Token, CorreosAcceso.CreaContrasena, "<b>Ana</b>");

        Assert.DoesNotContain("<b>Ana</b>", html);
        Assert.Contains("&lt;b&gt;Ana&lt;/b&gt;", html);
    }

    [Fact]
    public void Link_EscapesTheTokenAndToleratesATrailingSlashInTheBaseUrl()
    {
        string link = EmailLinkSender.BuildLink(BaseUrl + "/", "a b&c=d", CorreosAcceso.CreaContrasena);

        Assert.Equal($"{BaseUrl}/crear-contrasena?token=a%20b%26c%3Dd", link);
    }
}
