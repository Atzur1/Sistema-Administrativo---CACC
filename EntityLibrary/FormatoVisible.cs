using System.Globalization;

namespace EntityLibrary
{
    // HU-090: fechas y montos dentro de los mensajes que ve el usuario, siempre con el formato del portal y sin depender
    // de la cultura del servidor. En .NET la "/" de "dd/MM/yyyy" es el separador de la cultura activa (con de-DE sale
    // 01.10.2026) y "N0" usa su separador de miles (con en-US sale 85,000): por eso la cultura va explícita.
    public static class FormatoVisible
    {
        private static readonly CultureInfo Pesos_ = CultureInfo.GetCultureInfo("es-AR");

        // 01/10/2026
        public static string Fecha(DateTime fecha) => fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        // Sin fecha, vacío (igual que interpolar un DateTime? nulo).
        public static string Fecha(DateTime? fecha) => fecha is { } valor ? Fecha(valor) : "";

        // $85.000 (sin centavos, como el resto del portal)
        public static string Pesos(decimal monto) => "$" + monto.ToString("N0", Pesos_);
    }
}
