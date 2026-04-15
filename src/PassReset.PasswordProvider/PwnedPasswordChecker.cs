using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace PassReset.PasswordProvider;

/// <summary>
/// Checks a password against the HaveIBeenPwned k-anonymity API.
/// Instance-based class wired through <see cref="IHttpClientFactory"/> so the underlying
/// <see cref="HttpMessageHandler"/> can be substituted in tests.
/// See https://haveibeenpwned.com/API/v2#PwnedPasswords
/// </summary>
public sealed class PwnedPasswordChecker : IPwnedPasswordChecker
{
    private readonly HttpClient _http;
    private readonly ILogger<PwnedPasswordChecker>? _logger;

    /// <summary>
    /// Creates a new checker using the injected <see cref="HttpClient"/>.
    /// Callers must configure a reasonable BaseAddress / Timeout on the underlying client.
    /// </summary>
    public PwnedPasswordChecker(HttpClient http, ILogger<PwnedPasswordChecker>? logger = null)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>
    /// Fetches the raw HIBP k-anonymity range body for the given 5-char SHA-1 hex prefix.
    /// The HIBP API is case-insensitive on the prefix; we upper-case for consistency.
    /// On non-success or exception, returns <c>(string.Empty, true)</c> so the caller
    /// can decide whether to fail open or closed.
    /// </summary>
    public async Task<(string RangeBody, bool Unavailable)> FetchRangeAsync(string sha1Prefix5, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(sha1Prefix5) || sha1Prefix5.Length != 5 || !IsHexPrefix(sha1Prefix5))
            return (string.Empty, true);

        try
        {
            using var response = await _http.GetAsync(
                $"range/{sha1Prefix5.ToUpperInvariant()}", ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("HaveIBeenPwned API returned {StatusCode}", response.StatusCode);
                return (string.Empty, true);
            }

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return (body, false);
        }
        catch (Exception ex)
        {
            // Prefix is whitelist-validated to [0-9A-Fa-f]{5} above, so it cannot carry
            // CR/LF or other untrusted content into log output (cs/log-forging).
            _logger?.LogWarning(ex, "HaveIBeenPwned range fetch failed for prefix {Prefix}", sha1Prefix5.ToUpperInvariant());
            return (string.Empty, true);
        }
    }

    private static bool IsHexPrefix(string s)
    {
        foreach (var c in s)
        {
            var hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!hex) return false;
        }
        return true;
    }

    /// <summary>
    /// Checks whether the password appears in the HaveIBeenPwned database.
    /// Returns <see langword="true"/> if confirmed pwned, <see langword="false"/> if confirmed clean,
    /// or <see langword="null"/> if the API was unreachable so the caller can surface a distinct error.
    /// </summary>
    public async Task<bool?> IsPwnedPasswordAsync(string plaintext)
    {
        var hash = ComputeSha1Hex(plaintext).ToUpperInvariant();
        var prefix = hash[..5];
        var suffix = hash[5..];

        var (body, unavailable) = await FetchRangeAsync(prefix).ConfigureAwait(false);
        if (unavailable) return null;

        foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = line.IndexOf(':');
            if (colon > 0 && line[..colon].Trim().Equals(suffix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string ComputeSha1Hex(string input)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}
