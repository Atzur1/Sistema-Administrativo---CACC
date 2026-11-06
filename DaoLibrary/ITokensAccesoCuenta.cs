namespace DaoLibrary;

// HU-087: lo que necesita completar un enlace de activación o de recuperación. Leer el titular NO consume el enlace;
// completar lo consume una sola vez y solo si sigue vigente. Así una contraseña rechazada por la política deja el enlace
// listo para volver a intentar. Lo implementa AccountAccessDao.
public interface ITokensAccesoCuenta
{
    // Datos de la persona dueña de un enlace todavía válido; null si venció, ya se usó o no existe.
    (string? Nombre, string? Apellido, string? Dni, string? Email)? ObtenerTitularDelToken(string tokenHash);

    // Guarda el hash de la contraseña nueva y consume el enlace. false si el enlace ya no servía.
    bool CompleteAccountAccess(string tokenHash, string passwordHash);
}
