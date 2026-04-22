using Microsoft.AspNetCore.DataProtection;
using PassReset.Web.Services.Configuration;

namespace PassReset.Tests.Windows.Configuration;

public sealed class ConfigProtectorTests
{
    private static IConfigProtector MakeSut(IDataProtectionProvider? provider = null) =>
        new ConfigProtector(provider ?? new EphemeralDataProtectionProvider());

    [Fact]
    public void ProtectUnprotect_RoundTripsPlaintext()
    {
        var sut = MakeSut();
        var ciphertext = sut.Protect("hello-world");
        Assert.NotEqual("hello-world", ciphertext);
        Assert.Equal("hello-world", sut.Unprotect(ciphertext));
    }

    [Fact]
    public void Protect_TwoCallsSamePlaintext_ProduceDifferentCiphertext()
    {
        var sut = MakeSut();
        var a = sut.Protect("same");
        var b = sut.Protect("same");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Protect_EmptyString_RoundTrips()
    {
        var sut = MakeSut();
        var ciphertext = sut.Protect("");
        Assert.Equal("", sut.Unprotect(ciphertext));
    }

    [Fact]
    public void Unprotect_TamperedCiphertext_Throws()
    {
        var sut = MakeSut();
        var ciphertext = sut.Protect("real-value");
        var tampered = ciphertext[..^4] + "XXXX";
        Assert.ThrowsAny<Exception>(() => sut.Unprotect(tampered));
    }

    [Fact]
    public void PurposeIsolation_CiphertextFromDifferentPurpose_DoesNotDecrypt()
    {
        var provider = new EphemeralDataProtectionProvider();
        var ours = new ConfigProtector(provider);                   // uses "PassReset.Configuration.v1"
        var other = provider.CreateProtector("some.other.purpose"); // different purpose
        var foreignCt = other.Protect("leaked");
        Assert.ThrowsAny<Exception>(() => ours.Unprotect(foreignCt));
    }
}
