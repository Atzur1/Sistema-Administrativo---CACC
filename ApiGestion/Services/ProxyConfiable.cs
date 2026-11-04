using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace ApiGestion.Services;

// Qué saltos pueden informar la IP real (X-Forwarded-For) y el esquema original (X-Forwarded-Proto). Antes se vaciaban
// KnownNetworks y KnownProxies, o sea que CUALQUIER origen era un proxy confiable: si la API quedaba expuesta con
// ForwardedHeaders:Enabled=true, un cliente podía falsear su IP (y esquivar el límite de logins) o declararse HTTPS.
//
// Ahora:
//  - ForwardedHeaders:KnownProxies (IPs) y/o ForwardedHeaders:KnownNetworks (CIDR): la lista explícita del despliegue. Se
//    aceptan como lista de configuración (KnownNetworks:0, :1...) o como un texto separado por comas.
//  - Sin ninguna de las dos: solo loopback y redes privadas (RFC 1918 y ULA). Alcanza para docker compose, donde únicamente
//    nginx llega al backend por la red interna; un cliente de Internet no puede originar un salto desde esas redes.
//    Program.cs avisa en el log que se usa este valor por defecto.
public static class ProxyConfiable
{
    public const string ConfigProxies = "ForwardedHeaders:KnownProxies";
    public const string ConfigRedes = "ForwardedHeaders:KnownNetworks";

    public static readonly IReadOnlyList<string> RedesPorDefecto =
        new[] { "127.0.0.0/8", "::1/128", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "fc00::/7" };

    // Devuelve true si no hay lista explícita y se usaron las redes por defecto.
    public static bool Configurar(ForwardedHeadersOptions options, IConfiguration configuracion)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        // Un solo salto: el del proxy de la plataforma.
        options.ForwardLimit = 1;
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();

        string[] proxies = Lista(configuracion, ConfigProxies);
        string[] redes = Lista(configuracion, ConfigRedes);
        bool porDefecto = proxies.Length == 0 && redes.Length == 0;

        foreach (string proxy in proxies)
        {
            if (!IPAddress.TryParse(proxy, out IPAddress? ip))
                throw new InvalidOperationException($"{ConfigProxies}: '{proxy}' no es una dirección IP.");
            options.KnownProxies.Add(ip);
        }
        foreach (string red in porDefecto ? RedesPorDefecto : redes)
        {
            options.KnownNetworks.Add(Red(red));
        }
        return porDefecto;
    }

    private static string[] Lista(IConfiguration configuracion, string clave)
    {
        IConfigurationSection seccion = configuracion.GetSection(clave);
        IEnumerable<string?> valores = seccion.GetChildren().Any()
            ? seccion.GetChildren().Select(c => c.Value)
            : (seccion.Value ?? "").Split(',');
        return valores.Select(v => v?.Trim() ?? "").Where(v => v.Length > 0).ToArray();
    }

    private static Microsoft.AspNetCore.HttpOverrides.IPNetwork Red(string cidr)
    {
        string[] partes = cidr.Split('/');
        if (partes.Length != 2 || !IPAddress.TryParse(partes[0], out IPAddress? prefijo) || !int.TryParse(partes[1], out int largo)
            || largo < 0 || largo > (prefijo.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128))
            throw new InvalidOperationException($"{ConfigRedes}: '{cidr}' no es una red en formato CIDR (por ejemplo 10.0.0.0/8).");
        return new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefijo, largo);
    }
}
