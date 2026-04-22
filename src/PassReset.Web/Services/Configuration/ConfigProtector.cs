using Microsoft.AspNetCore.DataProtection;

namespace PassReset.Web.Services.Configuration;

/// <summary>
/// Production implementation of <see cref="IConfigProtector"/>. Uses purpose
/// <c>"PassReset.Configuration.v1"</c> to isolate ciphertext from other Data Protection
/// consumers (antiforgery tokens, session state, etc.).
/// </summary>
internal sealed class ConfigProtector : IConfigProtector
{
    internal const string Purpose = "PassReset.Configuration.v1";

    private readonly IDataProtector _protector;

    public ConfigProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
}
