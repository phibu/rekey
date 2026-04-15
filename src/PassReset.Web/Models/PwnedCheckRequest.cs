namespace PassReset.Web.Models;

/// <summary>
/// Request body for <c>POST /api/password/pwned-check</c>.
/// Carries only the 5-char SHA-1 hex prefix of the candidate password — full hash
/// and plaintext never leave the browser (HIBP k-anonymity).
/// </summary>
public sealed class PwnedCheckRequest
{
    /// <summary>5-char SHA-1 hex prefix (case-insensitive).</summary>
    public string Prefix { get; set; } = string.Empty;
}
