using System.Globalization;
using System.Text;

namespace ServiceLibrary
{
    // Requisitos de una contraseña NUEVA del Portal Administrativo (activación de cuenta y recuperación). Las
    // contraseñas que ya existen siguen funcionando: esto se controla solo al elegir una.
    //  - Al menos 10 caracteres (y no más de 128).
    //  - Letras y números.
    //  - No puede contener el DNI, el nombre, el apellido ni el usuario del correo de la persona.
    //  - No puede ser una de las contraseñas más usadas (ni el nombre del club).
    public static class PoliticaContrasenia
    {
        public const int LongitudMinima = 10;
        public const int LongitudMaxima = 128;

        // Partes del nombre más cortas que esto no se controlan ("Li", "De"): darían falsos rechazos.
        private const int LargoMinimoDeDatoPersonal = 3;

        // Las más usadas (listas públicas de filtraciones) y las obvias para este portal. Se comparan sin
        // mayúsculas, acentos ni símbolos, así "Camioneros2026!" cae igual que "camioneros2026".
        private static readonly HashSet<string> Comunes = new(StringComparer.Ordinal)
        {
            "1234567890", "0123456789", "12345678910", "1234567890a", "a1234567890", "qwerty1234", "qwertyuiop1",
            "1q2w3e4r5t", "1qaz2wsx3edc", "password12", "password123", "password1234", "passw0rd123", "contrasena1",
            "contrasena12", "contrasena123", "abc1234567", "abcd123456", "abcdef1234", "asdfghjkl1", "zxcvbnm123",
            "iloveyou12", "football12", "futbol1234", "argentina1", "argentina123", "argentina2026", "boca123456",
            "river123456", "bocajuniors1", "riverplate1", "talleres123", "belgrano123", "cordoba1234", "camioneros",
            "camioneros1", "camioneros123", "camioneros2026", "clubcamioneros", "clubcamioneros1", "cacc123456",
            "cacc2026cacc", "portalcacc1", "admin12345", "admin123456", "administrador1", "superadmin1", "bienvenido1",
            "bienvenido123", "1111111111", "0000000000", "1212121212", "1122334455", "9876543210", "aaaaaaaaa1",
            "welcome123", "letmein123", "monkey12345", "dragon12345", "sunshine12", "princess12", "master12345",
        };

        // Lista de lo que no cumple, en el orden en que conviene leerlo; vacía si la contraseña sirve.
        public static IReadOnlyList<string> Validar(string? contrasenia, DatosTitular titular)
        {
            var problemas = new List<string>();
            string valor = contrasenia ?? string.Empty;

            if (valor.Length < LongitudMinima)
                problemas.Add($"Tiene que tener al menos {LongitudMinima} caracteres.");
            if (valor.Length > LongitudMaxima)
                problemas.Add($"No puede superar los {LongitudMaxima} caracteres.");
            if (!valor.Any(char.IsLetter) || !valor.Any(char.IsDigit))
                problemas.Add("Tiene que combinar letras y números.");

            string normalizada = Normalizar(valor);
            if (titular.DatosPersonales().Select(Normalizar).Any(dato => dato.Length >= LargoMinimoDeDatoPersonal && normalizada.Contains(dato)))
                problemas.Add("No puede contener tu DNI, tu nombre, tu apellido ni tu correo.");
            if (Comunes.Contains(normalizada))
                problemas.Add("Es una contraseña demasiado común. Elegí otra.");

            return problemas;
        }

        // Minúsculas, sin acentos y solo letras y dígitos: "Pérez-Gómez" -> "perezgomez", "30.111.222" -> "30111222".
        internal static string Normalizar(string texto)
        {
            var sb = new StringBuilder(texto.Length);
            foreach (char c in texto.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }
    }

    // Lo que se sabe de la persona dueña de la cuenta, para que la contraseña no lo contenga.
    public sealed record DatosTitular(string? Nombre, string? Apellido, string? Dni, string? Email)
    {
        internal IEnumerable<string> DatosPersonales()
        {
            if (!string.IsNullOrWhiteSpace(Dni)) yield return Dni;
            if (!string.IsNullOrWhiteSpace(Email)) yield return Email.Split('@')[0];
            // Cada palabra del nombre y del apellido por separado ("Juan Pablo" -> "juan", "pablo").
            foreach (var parte in $"{Nombre} {Apellido}".Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                yield return parte;
        }
    }
}
