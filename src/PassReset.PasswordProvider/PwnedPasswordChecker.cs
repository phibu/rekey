using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace PassReset.PasswordProvider;

/// <summary>
/// Checks a password against the HaveIBeenPwned k-anonymity API.
/// Uses a static HttpClient (recommended pattern) and the synchronous Send() API
/// so it can be called from the synchronous IPasswordChangeProvider interface.
/// See https://haveibeenpwned.com/API/v2#PwnedPasswords
/// </summary>
internal static class PwnedPasswordChecker
{
    // Static HttpClient avoids socket exhaustion on repeated calls.
    // PooledConnectionLifetime ensures DNS changes are respected without restarting the process.
    private static readonly HttpClient _http = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    })
    {
        Timeout = TimeSpan.FromSeconds(5),
    };

    /// <summary>
    /// Checks whether the password appears in the HaveIBeenPwned database.
    /// Returns <see langword="true"/> if confirmed pwned, <see langword="false"/> if confirmed clean,
    /// or <see langword="null"/> if the API was unreachable so the caller can surface a distinct error.
    /// </summary>
    internal static async Task<bool?> IsPwnedPasswordAsync(string plaintext)
    {
        try
        {
            var hash = ComputeSha1Hex(plaintext).ToUpperInvariant();
            var prefix = hash[..5];
            var suffix = hash[5..];

            using var response = await _http.GetAsync(
                $"https://api.pwnedpasswords.com/range/{prefix}").ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var colon = line.IndexOf(':');
                if (colon > 0 && line[..colon].Equals(suffix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
        catch
        {
            // API unreachable — return null so the caller can surface a distinct error
            // rather than silently blocking the password change.
            return null;
        }
    }

    private static string ComputeSha1Hex(string input)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}
