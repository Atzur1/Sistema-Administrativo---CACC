namespace DaoLibrary;

using System.Security.Cryptography;

public static class PasswordHasher
{
    public const int WorkFactor = 12;

    private const string LegacyPrefix = "PBKDF2$";
    private const string BCryptPrefix = "$2";

    public static string HashPassword(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);
    }

    // Accepts BCrypt hashes and the PBKDF2 ones written before the switch, so existing accounts keep
    // signing in until AuthDao upgrades them on their next successful login.
    public static bool VerifyPassword(string password, string hash)
    {
        if (string.IsNullOrEmpty(hash))
        {
            return false;
        }
        if (hash.StartsWith(LegacyPrefix, StringComparison.Ordinal))
        {
            return VerifyLegacyPassword(password, hash);
        }
        if (!hash.StartsWith(BCryptPrefix, StringComparison.Ordinal))
        {
            return false;
        }
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }

    // True for PBKDF2 hashes and for BCrypt hashes with a lower cost than WorkFactor.
    public static bool NeedsRehash(string hash)
    {
        if (!hash.StartsWith(BCryptPrefix, StringComparison.Ordinal))
        {
            return true;
        }
        try
        {
            return BCrypt.Net.BCrypt.PasswordNeedsRehash(hash, WorkFactor);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return true;
        }
    }

    private static bool VerifyLegacyPassword(string password, string hash)
    {
        string[] parts = hash.Split('$');
        if (parts.Length != 4 || !int.TryParse(parts[1], out int rounds) || rounds < 100_000 || rounds > 1_000_000)
        {
            return false;
        }
        try
        {
            byte[] salt = Convert.FromBase64String(parts[2]);
            byte[] expected = Convert.FromBase64String(parts[3]);
            if (salt.Length != 16 || expected.Length != 32)
            {
                return false;
            }
            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, rounds, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
