using System.Security.Cryptography;
using DaoLibrary;

namespace ApiGestion.Tests;

public sealed class PasswordHasherTests
{
    [Fact]
    public void HashPassword_UsesBCryptAndVerifiesOnlyTheOriginalPassword()
    {
        var hash = PasswordHasher.HashPassword("una-clave-de-prueba-larga");

        Assert.Matches($@"^\$2[aby]\${PasswordHasher.WorkFactor}\$", hash);
        Assert.True(PasswordHasher.VerifyPassword("una-clave-de-prueba-larga", hash));
        Assert.False(PasswordHasher.VerifyPassword("otra-clave", hash));
        Assert.False(PasswordHasher.NeedsRehash(hash));
    }

    [Fact]
    public void VerifyPassword_AcceptsLegacyPbkdf2HashesAndFlagsThemForRehash()
    {
        var hash = LegacyPbkdf2("clave-anterior-al-cambio");

        Assert.True(PasswordHasher.VerifyPassword("clave-anterior-al-cambio", hash));
        Assert.False(PasswordHasher.VerifyPassword("otra-clave", hash));
        Assert.True(PasswordHasher.NeedsRehash(hash));
    }

    [Fact]
    public void NeedsRehash_FlagsBCryptHashesBelowTheCurrentWorkFactor()
    {
        var weakHash = BCrypt.Net.BCrypt.HashPassword("clave", PasswordHasher.WorkFactor - 2);

        Assert.True(PasswordHasher.VerifyPassword("clave", weakHash));
        Assert.True(PasswordHasher.NeedsRehash(weakHash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain-text-password")]
    [InlineData("PBKDF2$10$invalid$invalid")]
    [InlineData("PBKDF2$210000$AAAAAAAAAAAAAAAAAAAAAA==$")]
    [InlineData("$2a$12$not-a-valid-bcrypt-hash")]
    public void VerifyPassword_RejectsMalformedValues(string hash)
    {
        Assert.False(PasswordHasher.VerifyPassword("some-password", hash));
    }

    // The format PasswordHasher wrote before BCrypt, rebuilt here so the legacy path stays covered.
    private static string LegacyPbkdf2(string password)
    {
        const int iterations = 210_000;
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
        return $"PBKDF2${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }
}
