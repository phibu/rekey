namespace PassReset.Web.Services.Configuration;

/// <summary>
/// Thin wrapper over <see cref="Microsoft.AspNetCore.DataProtection.IDataProtector"/>
/// with a fixed purpose string. Protects/unprotects UTF-8 strings for at-rest secret
/// storage. See <c>docs/Admin-UI.md</c>.
/// </summary>
public interface IConfigProtector
{
    /// <summary>Encrypts <paramref name="plaintext"/> and returns base64-encoded ciphertext.</summary>
    string Protect(string plaintext);

    /// <summary>Decrypts <paramref name="ciphertext"/> produced by <see cref="Protect"/>.</summary>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// Thrown when <paramref name="ciphertext"/> is tampered, from a different purpose, or unprotectable with the current key ring.
    /// </exception>
    string Unprotect(string ciphertext);
}
