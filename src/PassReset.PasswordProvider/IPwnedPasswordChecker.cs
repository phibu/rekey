namespace PassReset.PasswordProvider;

/// <summary>
/// Abstraction for the HaveIBeenPwned k-anonymity password-breach checker.
/// Supports two call patterns:
/// <list type="bullet">
///   <item><description><see cref="FetchRangeAsync"/> — returns the raw HIBP range body for a 5-char SHA-1 prefix
///     so the <b>caller</b> (typically the browser) can perform the suffix match locally. The server never
///     learns which suffix matched.</description></item>
///   <item><description><see cref="IsPwnedPasswordAsync"/> — submit-time full-password check used by
///     <see cref="PasswordChangeProvider"/>. Performs the suffix match server-side.</description></item>
/// </list>
/// </summary>
public interface IPwnedPasswordChecker
{
    /// <summary>
    /// Fetches the raw HIBP k-anonymity range body for the given 5-char SHA-1 hex prefix.
    /// The body is newline-delimited <c>SUFFIX:COUNT</c> entries as returned by
    /// <c>https://api.pwnedpasswords.com/range/{prefix}</c>.
    /// Callers perform the suffix match themselves so the server never sees which suffix matched.
    /// Returns <c>(RangeBody: "", Unavailable: true)</c> on transport or non-2xx HTTP failure.
    /// </summary>
    Task<(string RangeBody, bool Unavailable)> FetchRangeAsync(string sha1Prefix5, CancellationToken ct = default);

    /// <summary>
    /// Submit-time full-password breach check.
    /// Returns <see langword="true"/> if confirmed pwned, <see langword="false"/> if confirmed clean,
    /// or <see langword="null"/> if the HIBP API was unreachable.
    /// </summary>
    Task<bool?> IsPwnedPasswordAsync(string plaintext);
}
