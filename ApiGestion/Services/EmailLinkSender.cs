using System.Net;
using System.Net.Mail;

namespace ApiGestion.Services;

public sealed class EmailLinkSender(
    IConfiguration configuration,
    ILogger<EmailLinkSender> logger,
    IHostEnvironment environment)
{
    public async Task SendPasswordResetLink(string email, string token, CancellationToken cancellationToken)
    {
        var host = configuration["Email:SmtpHost"];
        var from = configuration["Email:From"];
        var baseUrl = configuration["Email:PublicBaseUrl"];
        // SECURITY TODO: remove this local test path before production; production access links must use SMTP.
        if (environment.IsDevelopment() && configuration.GetValue<bool>("Email:LogResetLink"))
        {
            ValidateBaseUrl(baseUrl);
            var link = $"{baseUrl!.TrimEnd('/')}/restablecer-contrasena?token={Uri.EscapeDataString(token)}";
            logger.LogWarning("Enlace temporal de acceso para {Email}: {ResetLink}", email, link);
            return;
        }
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("Falta configurar Email:SmtpHost, Email:From o Email:PublicBaseUrl.");
        ValidateBaseUrl(baseUrl);

        using var message = new MailMessage(from, email)
        {
            Subject = "Restablecé tu contraseña del Portal Administrativo CACC",
            IsBodyHtml = true,
            Body = CreateBody(baseUrl, token)
        };
        using var client = new SmtpClient(host, configuration.GetValue("Email:SmtpPort", 587))
        {
            EnableSsl = configuration.GetValue("Email:EnableSsl", true)
        };
        var username = configuration["Email:Username"];
        if (!string.IsNullOrWhiteSpace(username)) client.Credentials = new NetworkCredential(username, configuration["Email:Password"]);
        cancellationToken.ThrowIfCancellationRequested();
        try { await client.SendMailAsync(message, cancellationToken); }
        catch (Exception exception)
        {
            logger.LogError(exception, "No se pudo enviar un correo de acceso al Portal Administrativo.");
            throw;
        }
    }

    private static string CreateBody(string baseUrl, string token)
    {
        var path = "/restablecer-contrasena";
        var link = $"{baseUrl.TrimEnd('/')}{path}?token={Uri.EscapeDataString(token)}";
        return $"<p>Usá este enlace para crear o restablecer la contraseña del superadministrador. Tiene una validez de 30 minutos y solo puede utilizarse una vez.</p><p><a href=\"{WebUtility.HtmlEncode(link)}\">Continuar</a></p><p>Si no solicitaste esta acción, ignorá este correo.</p>";
    }

    private static void ValidateBaseUrl(string? baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var publicUri) ||
            (publicUri.Scheme != Uri.UriSchemeHttps && !publicUri.IsLoopback))
            throw new InvalidOperationException("Email:PublicBaseUrl debe usar HTTPS; solo se permite HTTP para localhost.");
    }
}
