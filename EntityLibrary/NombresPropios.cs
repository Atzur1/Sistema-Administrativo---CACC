using System.Text;

namespace EntityLibrary
{
    // Nombres y apellidos de personas, guardados siempre igual sin importar cómo se tipearon ("JUAN perez" -> "Juan Perez").
    // La misma regla la usan los dos portales: la base es una sola y los datos tienen que quedar parejos.
    public static class NombresPropios
    {
        // Van en minúscula salvo que sean la primera palabra ("María de los Ángeles", pero "De la Fuente").
        private static readonly HashSet<string> Particulas = new(StringComparer.Ordinal)
        {
            "de", "del", "la", "las", "los", "y", "e"
        };

        public static string Normalizar(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return string.Empty;

            var palabras = texto.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < palabras.Length; i++)
            {
                string minuscula = palabras[i].ToLowerInvariant();
                palabras[i] = i > 0 && Particulas.Contains(minuscula) ? minuscula : CapitalizarPalabra(minuscula);
            }
            return string.Join(' ', palabras);
        }

        // Mayúscula al inicio y después de guion o apóstrofo ("Pérez-Gómez", "D'Angelo"); "Mc" lleva mayúscula también en la siguiente ("McDonald").
        private static string CapitalizarPalabra(string minuscula)
        {
            var resultado = new StringBuilder(minuscula.Length);
            bool mayuscula = true;
            for (int i = 0; i < minuscula.Length; i++)
            {
                char c = minuscula[i];
                resultado.Append(mayuscula ? char.ToUpperInvariant(c) : c);
                mayuscula = c is '-' or '\'' or '’';
            }

            if (resultado.Length > 2 && resultado[0] == 'M' && resultado[1] == 'c' && char.IsLetter(resultado[2]))
            {
                resultado[2] = char.ToUpperInvariant(resultado[2]);
            }
            return resultado.ToString();
        }
    }
}
