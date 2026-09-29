using DaoLibrary;

namespace ApiGestion.Tests;

public sealed class PasswordHasherTests
{
    [Fact]
    public void Hash_VerifiesOnlyTheOriginalPassword()
    {
        var encoded = PasswordHasher.Hash("una-clave-de-prueba-larga");

        Assert.StartsWith("PBKDF2$210000$", encoded);
        Assert.True(PasswordHasher.Verify("una-clave-de-prueba-larga", encoded));
        Assert.False(PasswordHasher.Verify("otra-clave", encoded));
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain-text-password")]
    [InlineData("PBKDF2$10$invalid$invalid")]
    [InlineData("PBKDF2$210000$AAAAAAAAAAAAAAAAAAAAAA==$")]
    public void Verify_RejectsMalformedOrLegacyValues(string encoded)
    {
        Assert.False(PasswordHasher.Verify("some-password", encoded));
    }
}
