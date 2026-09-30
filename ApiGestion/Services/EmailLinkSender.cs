using System.Net;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

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

        var imagePath = Path.Combine(AppContext.BaseDirectory, "Assets", "cacc-email-header.jpg");
        if (!File.Exists(imagePath))
            throw new FileNotFoundException("No se encontró el encabezado institucional del correo.", imagePath);

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = CreateBody(baseUrl, token, heading, body, action),
            TextBody = CreatePlainTextBody(baseUrl, token, heading, body, action)
        };
        var headerImage = bodyBuilder.LinkedResources.Add(imagePath);
        headerImage.ContentId = "cacc-email-header";
        headerImage.ContentDisposition = new ContentDisposition(ContentDisposition.Inline);

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(from));
        message.To.Add(MailboxAddress.Parse(email));
        message.Subject = subject;
        message.Body = bodyBuilder.ToMessageBody();

        var port = configuration.GetValue("Email:SmtpPort", 587);
        var secureOption = configuration.GetValue("Email:EnableSsl", true) ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
        var username = configuration["Email:Username"];

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(host, port, secureOption, cancellationToken);
            if (!string.IsNullOrWhiteSpace(username))
                await client.AuthenticateAsync(username, configuration["Email:Password"] ?? string.Empty, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "No se pudo enviar un correo de acceso al Portal Administrativo.");
            throw;
        }
    }

    private static string CreateBody(string baseUrl, string token, string heading, string intro, string action)
    {
        var link = $"{baseUrl.TrimEnd('/')}/restablecer-contrasena?token={Uri.EscapeDataString(token)}";
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
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" bgcolor="#082414" style="background-color:#082414;background-image:radial-gradient(ellipse at 16% 12%,rgba(0,214,102,.42) 0%,rgba(0,214,102,0) 32%),linear-gradient(153deg,transparent 0%,transparent 25%,rgba(0,214,102,.03) 25.3%,rgba(0,214,102,.42) 25.6%,rgba(190,255,218,.76) 25.8%,rgba(0,214,102,.12) 26.1%,transparent 26.6%),linear-gradient(153deg,transparent 0%,transparent 62%,rgba(214,172,79,0) 62.1%,rgba(214,172,79,.48) 62.25%,rgba(255,238,183,.86) 62.4%,rgba(214,172,79,.06) 62.7%,transparent 63.1%),linear-gradient(135deg,#007f3e 0%,#0d4223 35%,#082414 60%,#030d07 85%,#000000 100%);">
                <tr><td align="center" style="padding:40px 12px;background-color:#082414;background-image:radial-gradient(ellipse at 16% 12%,rgba(0,214,102,.42) 0%,rgba(0,214,102,0) 32%),linear-gradient(153deg,transparent 0%,transparent 25%,rgba(0,214,102,.03) 25.3%,rgba(0,214,102,.42) 25.6%,rgba(190,255,218,.76) 25.8%,rgba(0,214,102,.12) 26.1%,transparent 26.6%),linear-gradient(153deg,transparent 0%,transparent 62%,rgba(214,172,79,0) 62.1%,rgba(214,172,79,.48) 62.25%,rgba(255,238,183,.86) 62.4%,rgba(214,172,79,.06) 62.7%,transparent 63.1%),linear-gradient(135deg,#007f3e 0%,#0d4223 35%,#082414 60%,#030d07 85%,#000000 100%);">
                  <!--[if mso]><table role="presentation" width="640" align="center"><tr><td><![endif]-->
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="width:100%;max-width:640px;">
                <tr><td align="center">
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" bgcolor="#edf5ef" style="width:100%;background-color:#edf5ef;background-image:radial-gradient(ellipse at 0% 0%,rgba(0,166,81,.19) 0%,rgba(0,166,81,0) 48%),radial-gradient(ellipse at 100% 100%,rgba(0,127,62,.13) 0%,rgba(0,127,62,0) 44%),linear-gradient(150deg,#f7fbf7 0%,#e8f3eb 100%);border:1px solid #cbd9ce;border-radius:10px;overflow:hidden;">
                    <tr><td style="padding:0;background-color:#082414;font-size:0;line-height:0;">
                      <img src="cid:cacc-email-header" width="640" alt="CACC · Club Atlético Social y Deportivo Camioneros" style="display:block;width:100%;max-width:640px;height:auto;border:0;">
                    </td></tr>
                    <tr><td bgcolor="#f3f8f4" style="height:4px;background-color:#b68a34;font-size:0;line-height:0;">&nbsp;</td></tr>
                    <tr><td bgcolor="#f0f7f1" style="padding:32px 40px 7px;background-color:#f0f7f1;background-image:radial-gradient(ellipse at 0% 0%,rgba(0,166,81,.17) 0%,rgba(0,166,81,0) 60%),linear-gradient(150deg,#f7fbf7 0%,#eaf4ed 100%);">
                      <div style="padding-bottom:9px;color:#a87921;font-size:11px;font-weight:700;letter-spacing:1.4px;text-transform:uppercase;">PORTAL ADMINISTRATIVO</div>
                      <h1 style="margin:0;color:#173d29;font-size:27px;line-height:1.3;font-weight:700;">{{WebUtility.HtmlEncode(heading)}}</h1>
                    </td></tr>
                    <tr><td bgcolor="#eef6ef" style="padding:9px 40px 0;background-color:#eef6ef;background-image:radial-gradient(ellipse at 100% 50%,rgba(0,127,62,.10) 0%,rgba(0,127,62,0) 58%),linear-gradient(100deg,#f2f8f3 0%,#eaf4ed 100%);color:#405148;font-size:15px;line-height:1.7;">{{WebUtility.HtmlEncode(intro)}}</td></tr>
                    <tr><td align="left" bgcolor="#eef6ef" style="padding:25px 40px 22px;background-color:#eef6ef;background-image:radial-gradient(ellipse at 0% 50%,rgba(0,166,81,.12) 0%,rgba(0,166,81,0) 60%),linear-gradient(100deg,#f2f8f3 0%,#eaf4ed 100%);">
                      <table role="presentation" cellspacing="0" cellpadding="0" border="0"><tr><td align="center" bgcolor="#007f3e" style="background-color:#007f3e;background-image:linear-gradient(110deg,#00a651 0%,#007f3e 55%,#005a2b 100%);border-radius:6px;">
                        <a href="{{safeLink}}" style="display:inline-block;padding:15px 25px;border:1px solid #007f3e;border-radius:6px;color:#ffffff;text-decoration:none;font-size:15px;font-weight:700;">{{WebUtility.HtmlEncode(action)}}</a>
                      </td></tr></table>
                    </td></tr>
                    <tr><td bgcolor="#eaf4ed" style="padding:0 40px 28px;background-color:#eaf4ed;background-image:radial-gradient(ellipse at 100% 100%,rgba(0,127,62,.14) 0%,rgba(0,127,62,0) 58%),linear-gradient(100deg,#eef6ef 0%,#e5f0e8 100%);color:#4f6255;font-size:13px;line-height:1.65;">
                      Este enlace es válido durante <strong style="color:#34443a;">30 minutos</strong> y puede utilizarse una sola vez. Si vence, solicitá uno nuevo desde el portal.
                    </td></tr>
                    <tr><td bgcolor="#e6f0e8" style="padding:17px 40px;background-color:#e6f0e8;border-top:1px solid #d6e3d9;color:#536459;font-size:12px;line-height:1.6;">
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
