using DaoLibrary;

namespace ServiceLibrary
{
    // HU-087: completar un enlace de activación de cuenta o de recuperación de contraseña. Los dos flujos son el mismo:
    //  1. el enlace tiene que seguir vigente (si no, se responde lo de siempre: "no es válido o venció");
    //  2. la contraseña nueva pasa por PoliticaContrasenia con los datos del dueño del enlace;
    //  3. si no cumple, se informan TODOS los problemas y el enlace NO se consume: se corrige y se reenvía con el mismo;
    //  4. si cumple, se guarda su hash (PasswordHasher, sin cambios) y recién ahí se consume el enlace.
    // Nunca se registra la contraseña ni el enlace: el log del rechazo, si hace falta, lo hace quien llama.
    public sealed class AccesoCuentaService
    {
        public const string MensajeEnlaceInvalido = "El enlace no es válido o venció.";
        public const string MensajeContraseniaInvalida = "La contraseña no cumple los requisitos";

        private readonly ITokensAccesoCuenta _tokens;

        public AccesoCuentaService(ITokensAccesoCuenta tokens) => _tokens = tokens;

        public ResultadoOperacion<bool> Completar(string tokenHash, string? contrasenia)
        {
            var titular = _tokens.ObtenerTitularDelToken(tokenHash);
            if (titular is null)
            {
                return ResultadoOperacion<bool>.Falla(TipoResultado.Invalido, MensajeEnlaceInvalido);
            }

            var (nombre, apellido, dni, email) = titular.Value;
            var problemas = PoliticaContrasenia.Validar(contrasenia, new DatosTitular(nombre, apellido, dni, email));
            if (problemas.Count > 0)
            {
                return ResultadoOperacion<bool>.Falla(TipoResultado.Invalido,
                    $"{MensajeContraseniaInvalida}: {string.Join(" ", problemas)}", problemas);
            }

            // Entre la lectura y la escritura el enlace pudo vencer o usarse en otra pestaña: la escritura lo vuelve a
            // controlar en su transacción.
            return _tokens.CompleteAccountAccess(tokenHash, PasswordHasher.HashPassword(contrasenia!))
                ? ResultadoOperacion<bool>.Ok(true)
                : ResultadoOperacion<bool>.Falla(TipoResultado.Invalido, MensajeEnlaceInvalido);
        }
    }
}
