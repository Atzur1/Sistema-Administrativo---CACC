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
            SubjectEncoding = System.Text.Encoding.UTF8,
            BodyEncoding = System.Text.Encoding.UTF8
        };
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
            CreatePlainTextBody(baseUrl, token, heading, body, action),
            System.Text.Encoding.UTF8,
            "text/plain"));
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
            CreateBody(baseUrl, token, heading, body, action),
            System.Text.Encoding.UTF8,
            "text/html"));
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
            <body style="margin:0;padding:0;background-color:#082414;font-family:Arial,Helvetica,sans-serif;color:#202820;">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" bgcolor="#082414" style="background-color:#082414;background-image:linear-gradient(135deg,#007f3e 0%,#0d4223 35%,#082414 60%,#030d07 85%,#000000 100%);">
                <tr><td align="center" style="padding:40px 12px;background-color:#082414;background-image:linear-gradient(135deg,#007f3e 0%,#0d4223 35%,#082414 60%,#030d07 85%,#000000 100%);">
                  <!--[if mso]><table role="presentation" width="640" align="center"><tr><td><![endif]-->
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="width:100%;max-width:640px;">
                <tr><td align="center">
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" bgcolor="#ffffff" style="width:100%;background-color:#ffffff;border:1px solid #dce5dd;border-radius:10px;overflow:hidden;">
                    <tr><td bgcolor="#007f3e" style="height:5px;background-color:#00b355;font-size:0;line-height:0;">&nbsp;</td></tr>
                    <tr><td bgcolor="#0d4223" style="padding:25px 40px;background-color:#0d4223;background-image:linear-gradient(110deg,#007f3e 0%,#0d4223 48%,#082414 100%);text-align:left;">
                      <table role="presentation" cellspacing="0" cellpadding="0" border="0"><tr>
                        <td valign="middle" style="padding:0 14px 0 0;">
                          <table role="presentation" cellspacing="0" cellpadding="0" border="0" style="border:2px solid #ffffff;border-radius:7px;"><tr><td style="padding:7px 10px;color:#ffffff;font-size:18px;font-weight:700;letter-spacing:2px;">CACC</td></tr></table>
                        </td>
                        <td valign="middle" style="color:#ffffff;font-size:12px;font-weight:700;line-height:1.5;letter-spacing:.5px;">CLUB ATLÉTICO SOCIAL Y<br>DEPORTIVO CAMIONEROS</td>
                      </tr></table>
                    </td></tr>
                    <tr><td style="padding:35px 40px 7px;">
                      <div style="padding-bottom:9px;color:#a87921;font-size:11px;font-weight:700;letter-spacing:1.4px;text-transform:uppercase;">PORTAL ADMINISTRATIVO</div>
                      <h1 style="margin:0;color:#173d29;font-size:27px;line-height:1.3;font-weight:700;">{{WebUtility.HtmlEncode(heading)}}</h1>
                    </td></tr>
                    <tr><td style="padding:9px 40px 0;color:#4d5850;font-size:15px;line-height:1.7;">{{WebUtility.HtmlEncode(intro)}}</td></tr>
                    <tr><td align="left" style="padding:25px 40px 22px;">
                      <table role="presentation" cellspacing="0" cellpadding="0" border="0"><tr><td align="center" bgcolor="#007f3e" style="background-color:#007f3e;background-image:linear-gradient(110deg,#00a651 0%,#007f3e 55%,#005a2b 100%);border-radius:6px;">
                        <a href="{{safeLink}}" style="display:inline-block;padding:15px 25px;border:1px solid #007f3e;border-radius:6px;color:#ffffff;text-decoration:none;font-size:15px;font-weight:700;">{{WebUtility.HtmlEncode(action)}}</a>
                      </td></tr></table>
                    </td></tr>
                    <tr><td style="padding:0 40px 28px;color:#68736b;font-size:13px;line-height:1.65;">
                      Este enlace es válido durante <strong style="color:#34443a;">30 minutos</strong> y puede utilizarse una sola vez. Si vence, solicitá uno nuevo desde el portal.
                    </td></tr>
                    <tr><td bgcolor="#f8faf8" style="padding:17px 40px;background-color:#f8faf8;border-top:1px solid #e8eee9;color:#68736b;font-size:12px;line-height:1.6;">
                      Si no solicitaste este correo, podés ignorarlo. Tu cuenta permanecerá segura.
                    </td></tr>
                  </table>
                  <div style="padding:15px 12px 0;color:#738077;font-size:11px;line-height:1.5;">Un servicio digital del Club Atlético Social y Deportivo Camioneros</div>
                </td></tr>
                  </table>
                  <!--[if mso]></td></tr></table><![endif]-->
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
