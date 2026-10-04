using System.Net;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace ApiGestion.Services;

// Los dos tipos de correo con enlace de un solo uso del Portal. Comparten el mecanismo (token de
// TOKEN_ACCESO_CUENTA, 30 minutos) pero NO el mensaje ni la pantalla de destino: cada uno lleva su
// propio asunto, título, texto y ruta, para que quien lo recibe sepa de un vistazo si está creando
// su contraseña por primera vez o reestableciendo una que olvidó.
internal sealed record TipoCorreoAcceso(
    string Asunto,
    string Etiqueta,
    string Titulo,
    string Intro,
    string Accion,
    string Ruta,
    string NotaVencimiento,
    string NotaIgnorar);

internal static class CorreosAcceso
{
    // Primer ingreso: un SuperAdmin habilitó la cuenta desde Usuarios y Permisos.
    public static readonly TipoCorreoAcceso CreaContrasena = new(
        Asunto: "Crea tu contraseña · Portal Administrativo CACC",
        Etiqueta: "Primer ingreso",
        Titulo: "Crea tu contraseña",
        Intro: "Un administrador habilitó tu acceso al Portal Administrativo CACC. Para ingresar por primera vez, creá tu contraseña desde el botón de abajo.",
        Accion: "Crear mi contraseña",
        Ruta: "/crear-contrasena",
        NotaVencimiento: "Este enlace es válido durante 30 minutos y puede utilizarse una sola vez. Si vence, pedile a un administrador que vuelva a habilitar tu acceso.",
        NotaIgnorar: "Si no esperabas este correo, podés ignorarlo: no se creó ninguna contraseña.");

    // Olvidé mi contraseña: la pidió el titular de la cuenta desde el inicio de sesión.
    public static readonly TipoCorreoAcceso ReestableceContrasena = new(
        Asunto: "Reestablece tu contraseña · Portal Administrativo CACC",
        Etiqueta: "Recuperar acceso",
        Titulo: "Reestablece tu contraseña",
        Intro: "Recibimos una solicitud para reestablecer la contraseña de tu cuenta del Portal Administrativo CACC. Si fuiste vos, elegí una nueva desde el botón de abajo.",
        Accion: "Reestablecer mi contraseña",
        Ruta: "/restablecer-contrasena",
        NotaVencimiento: "Este enlace es válido durante 30 minutos y puede utilizarse una sola vez. Si vence, solicitá uno nuevo desde el inicio de sesión con “¿Olvidaste tu contraseña?”.",
        NotaIgnorar: "Si no solicitaste este cambio, podés ignorar este correo: tu contraseña actual sigue siendo la misma.");
}

public sealed class EmailLinkSender(
    IConfiguration configuration,
    ILogger<EmailLinkSender> logger,
    IHostEnvironment environment)
{
    // Recuperación de contraseña (SuperAdmin, desde "¿Olvidaste tu contraseña?").
    public Task SendPasswordResetLink(string email, string token, CancellationToken cancellationToken) =>
        SendLink(email, token, CorreosAcceso.ReestableceContrasena, cancellationToken);

    // Primer ingreso de una cuenta recién habilitada por un SuperAdmin (Usuarios y Permisos).
    public Task SendActivationLink(string email, string token, CancellationToken cancellationToken) =>
        SendLink(email, token, CorreosAcceso.CreaContrasena, cancellationToken);

    private async Task SendLink(string email, string token, TipoCorreoAcceso tipo, CancellationToken cancellationToken)
    {
        var host = configuration["Email:SmtpHost"];
        var from = configuration["Email:From"];
        var baseUrl = configuration["Email:PublicBaseUrl"];
        // SECURITY TODO: remove this local test path before production; production access links must use SMTP.
        if (environment.IsDevelopment() && configuration.GetValue<bool>("Email:LogResetLink"))
        {
            ValidateBaseUrl(baseUrl);
            logger.LogWarning("Enlace temporal de acceso para {Email} ({Tipo}): {ResetLink}", email, tipo.Titulo, BuildLink(baseUrl!, token, tipo));
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
            HtmlBody = CreateBody(baseUrl, token, tipo),
            TextBody = CreatePlainTextBody(baseUrl, token, tipo)
        };
        var headerImage = bodyBuilder.LinkedResources.Add(imagePath);
        headerImage.ContentId = "cacc-email-header";
        headerImage.ContentDisposition = new ContentDisposition(ContentDisposition.Inline);

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(from));
        message.To.Add(MailboxAddress.Parse(email));
        message.Subject = tipo.Asunto;
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

    // Cada tipo tiene su propia pantalla: crear-contrasena (primer ingreso) o restablecer-contrasena.
    internal static string BuildLink(string baseUrl, string token, TipoCorreoAcceso tipo) =>
        $"{baseUrl.TrimEnd('/')}{tipo.Ruta}?token={Uri.EscapeDataString(token)}";

    internal static string CreateBody(string baseUrl, string token, TipoCorreoAcceso tipo)
    {
        var safeLink = WebUtility.HtmlEncode(BuildLink(baseUrl, token, tipo));
        var heading = WebUtility.HtmlEncode(tipo.Titulo);
        var label = WebUtility.HtmlEncode($"Portal Administrativo · {tipo.Etiqueta}");
        return $$"""
            <!doctype html>
            <html lang="es">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>{{heading}}</title>
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
                      <div style="padding-bottom:9px;color:#a87921;font-size:11px;font-weight:700;letter-spacing:1.4px;text-transform:uppercase;">{{label}}</div>
                      <h1 style="margin:0;color:#173d29;font-size:27px;line-height:1.3;font-weight:700;text-transform:uppercase;letter-spacing:.4px;">{{heading}}</h1>
                    </td></tr>
                    <tr><td bgcolor="#eef6ef" style="padding:9px 40px 0;background-color:#eef6ef;background-image:radial-gradient(ellipse at 100% 50%,rgba(0,127,62,.10) 0%,rgba(0,127,62,0) 58%),linear-gradient(100deg,#f2f8f3 0%,#eaf4ed 100%);color:#405148;font-size:15px;line-height:1.7;">{{WebUtility.HtmlEncode(tipo.Intro)}}</td></tr>
                    <tr><td align="left" bgcolor="#eef6ef" style="padding:25px 40px 22px;background-color:#eef6ef;background-image:radial-gradient(ellipse at 0% 50%,rgba(0,166,81,.12) 0%,rgba(0,166,81,0) 60%),linear-gradient(100deg,#f2f8f3 0%,#eaf4ed 100%);">
                      <table role="presentation" cellspacing="0" cellpadding="0" border="0"><tr><td align="center" bgcolor="#007f3e" style="background-color:#007f3e;background-image:linear-gradient(110deg,#00a651 0%,#007f3e 55%,#005a2b 100%);border-radius:6px;">
                        <a href="{{safeLink}}" style="display:inline-block;padding:15px 25px;border:1px solid #007f3e;border-radius:6px;color:#ffffff;text-decoration:none;font-size:15px;font-weight:700;">{{WebUtility.HtmlEncode(tipo.Accion)}}</a>
                      </td></tr></table>
                    </td></tr>
                    <tr><td bgcolor="#eaf4ed" style="padding:0 40px 28px;background-color:#eaf4ed;background-image:radial-gradient(ellipse at 100% 100%,rgba(0,127,62,.14) 0%,rgba(0,127,62,0) 58%),linear-gradient(100deg,#eef6ef 0%,#e5f0e8 100%);color:#4f6255;font-size:13px;line-height:1.65;">
                      {{WebUtility.HtmlEncode(tipo.NotaVencimiento)}}
                    </td></tr>
                    <tr><td bgcolor="#e6f0e8" style="padding:17px 40px;background-color:#e6f0e8;border-top:1px solid #d6e3d9;color:#536459;font-size:12px;line-height:1.6;">
                      {{WebUtility.HtmlEncode(tipo.NotaIgnorar)}}
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

    internal static string CreatePlainTextBody(string baseUrl, string token, TipoCorreoAcceso tipo) =>
        $"{tipo.Titulo.ToUpperInvariant()}\n\n{tipo.Intro}\n\n{tipo.Accion}: {BuildLink(baseUrl, token, tipo)}\n\n{tipo.NotaVencimiento}\n\n{tipo.NotaIgnorar}";

    private static void ValidateBaseUrl(string? baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var publicUri) ||
            (publicUri.Scheme != Uri.UriSchemeHttps && !publicUri.IsLoopback))
            throw new InvalidOperationException("Email:PublicBaseUrl debe usar HTTPS; solo se permite HTTP para localhost.");
    }
}
