using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace ReKey.PasswordProvider;

/// <summary>
/// Checks a password against the HaveIBeenPwned k-anonymity API.
/// Uses a static HttpClient (recommended pattern) and the synchronous Send() API
/// so it can be called from the synchronous IPasswordChangeProvider interface.
/// See https://haveibeenpwned.com/API/v2#PwnedPasswords
/// </summary>
internal static class PwnedPasswordChecker
{
    // Static HttpClient avoids socket exhaustion on repeated calls.
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(5),
    };

    /// <summary>
    /// Returns true if the password appears in the HaveIBeenPwned database.
    /// Fails closed — returns true (treat as pwned) if the API is unreachable.
    /// </summary>
    internal static bool IsPwnedPassword(string plaintext)
    {
        try
        {
            var hash = ComputeSha1Hex(plaintext).ToUpperInvariant();
            var prefix = hash[..5];
            var suffix = hash[5..];

            using var request  = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.pwnedpasswords.com/range/{prefix}");
            using var response = _http.Send(request);
            using var reader   = new StreamReader(response.Content.ReadAsStream());

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                // Each line is "HASH_SUFFIX:COUNT"
                var colon = line.IndexOf(':');
                if (colon > 0 && line[..colon].Equals(suffix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
        catch
        {
            // Fail closed: if the API is unreachable, reject the password to be safe.
            return true;
        }
    }

    private static string ComputeSha1Hex(string input)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}
