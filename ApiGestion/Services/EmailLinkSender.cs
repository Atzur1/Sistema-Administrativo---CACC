using System.Net;
using System.Net.Mail;

namespace ApiGestion.Services;

// Manda los dos correos con enlace de un solo uso del sistema: recuperación de
// contraseña del SuperAdmin y activación de una cuenta recién habilitada desde
// Usuarios y Permisos. Ambos comparten el mismo token (TOKEN_ACCESO_CUENTA) y
// la misma pantalla de destino (/restablecer-contrasena); solo cambia el texto.
public sealed class EmailLinkSender(
    IConfiguration configuration,
    ILogger<EmailLinkSender> logger,
    IHostEnvironment environment)
{
    public Task SendPasswordResetLink(string email, string token, CancellationToken cancellationToken) => SendLink(
        email, token, cancellationToken,
        subject: "Restablecé tu contraseña del Portal Administrativo CACC",
        heading: "Restablecé tu contraseña",
        body: "Recibimos una solicitud para crear o cambiar la contraseña del superadministrador del Portal Administrativo.",
        action: "Restablecer contraseña");

    // Enlace de activación para una cuenta recién habilitada por un SuperAdmin
    // (Usuarios y Permisos). Mismo mecanismo de token que la recuperación de
    // contraseña, distinto texto para que quede claro que es un alta, no un reset.
    public Task SendActivationLink(string email, string token, CancellationToken cancellationToken) => SendLink(
        email, token, cancellationToken,
        subject: "Activá tu acceso al Portal Administrativo CACC",
        heading: "Activá tu acceso",
        body: "Un administrador habilitó tu acceso al Portal Administrativo CACC. Para comenzar, creá una contraseña segura.",
        action: "Crear mi contraseña");

    private async Task SendLink(
        string email,
        string token,
        CancellationToken cancellationToken,
        string subject,
        string heading,
        string body,
        string action)
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
            Subject = subject,
            IsBodyHtml = true,
            Body = CreateBody(baseUrl, token, heading, body, action),
            BodyEncoding = System.Text.Encoding.UTF8,
            SubjectEncoding = System.Text.Encoding.UTF8
        };
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
            CreatePlainTextBody(baseUrl, token, heading, body, action),
            System.Text.Encoding.UTF8,
            "text/plain"));
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

    private static string CreateBody(string baseUrl, string token, string heading, string intro, string action)
    {
        var path = "/restablecer-contrasena";
        var link = $"{baseUrl.TrimEnd('/')}{path}?token={Uri.EscapeDataString(token)}";
        var safeLink = WebUtility.HtmlEncode(link);
        return $$"""
            <!doctype html>
            <html lang="es">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>{{WebUtility.HtmlEncode(heading)}}</title>
            </head>
            <body style="margin:0;padding:0;background-color:#f3f5f3;font-family:Arial,Helvetica,sans-serif;color:#202820;">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="background-color:#f3f5f3;padding:36px 12px;">
                <tr><td align="center">
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="max-width:560px;background-color:#ffffff;border:1px solid #e3e9e4;border-radius:12px;overflow:hidden;">
                    <tr><td style="height:8px;background-color:#08783e;font-size:0;line-height:0;">&nbsp;</td></tr>
                    <tr><td style="padding:30px 36px 8px;text-align:center;">
                      <div style="display:inline-block;padding:9px 16px;border:2px solid #08783e;border-radius:8px;color:#08783e;font-size:20px;font-weight:700;letter-spacing:2px;">CACC</div>
                      <div style="padding-top:10px;color:#68736b;font-size:11px;font-weight:700;letter-spacing:1.2px;">CLUB ATLÉTICO SOCIAL Y DEPORTIVO CAMIONEROS</div>
                    </td></tr>
                    <tr><td style="padding:24px 36px 4px;">
                      <h1 style="margin:0;color:#183b28;font-size:25px;line-height:1.3;font-weight:700;">{{WebUtility.HtmlEncode(heading)}}</h1>
                    </td></tr>
                    <tr><td style="padding:12px 36px 0;color:#4d5850;font-size:15px;line-height:1.7;">{{WebUtility.HtmlEncode(intro)}}</td></tr>
                    <tr><td align="center" style="padding:28px 36px;">
                      <a href="{{safeLink}}" style="display:inline-block;background-color:#08783e;border:1px solid #08783e;border-radius:7px;padding:14px 24px;color:#ffffff;text-decoration:none;font-size:15px;font-weight:700;">{{WebUtility.HtmlEncode(action)}}</a>
                    </td></tr>
                    <tr><td style="padding:0 36px 22px;color:#68736b;font-size:13px;line-height:1.65;">
                      Este enlace es válido durante <strong style="color:#34443a;">30 minutos</strong> y puede utilizarse una sola vez. Si vence, solicitá uno nuevo desde el portal.
                    </td></tr>
                    <tr><td style="padding:18px 36px;border-top:1px solid #edf0ed;color:#68736b;font-size:12px;line-height:1.6;">
                      Si no solicitaste este correo, podés ignorarlo. Tu cuenta permanecerá segura.
                    </td></tr>
                  </table>
                  <div style="padding:18px 12px 0;color:#8a938c;font-size:11px;line-height:1.5;">Portal Administrativo · CACC</div>
                </td></tr>
              </table>
            </body>
            </html>
            """;
    }

    private static string CreatePlainTextBody(string baseUrl, string token, string heading, string intro, string action)
    {
        var link = $"{baseUrl.TrimEnd('/')}/restablecer-contrasena?token={Uri.EscapeDataString(token)}";
        return $"{heading}\n\n{intro}\n\n{action}: {link}\n\nEste enlace es válido durante 30 minutos y puede utilizarse una sola vez. Si no solicitaste este correo, podés ignorarlo.";
    }

    private static void ValidateBaseUrl(string? baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var publicUri) ||
            (publicUri.Scheme != Uri.UriSchemeHttps && !publicUri.IsLoopback))
            throw new InvalidOperationException("Email:PublicBaseUrl debe usar HTTPS; solo se permite HTTP para localhost.");
    }
}
