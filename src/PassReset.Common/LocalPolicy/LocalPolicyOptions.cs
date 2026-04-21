namespace PassReset.Common.LocalPolicy;

/// <summary>
/// Configuration for the operator-managed local password policy layer.
/// All properties are optional; null/empty disables the corresponding check.
/// Nested under <see cref="PasswordChangeOptions.LocalPolicy"/>.
/// </summary>
public sealed class LocalPolicyOptions
{
    /// <summary>
    /// Absolute path to a plaintext UTF-8 banned-words file. One term per line.
    /// Lines starting with <c>#</c> are comments; blank lines are ignored.
    /// When null or empty, banned-words enforcement is disabled.
    /// </summary>
    public string? BannedWordsPath { get; set; }

    /// <summary>
    /// Absolute path to a directory containing HIBP SHA-1 range files named
    /// <c>&lt;PREFIX&gt;.txt</c> (prefix = 5 uppercase hex chars). Each file holds
    /// <c>&lt;35-hex-suffix&gt;:&lt;count&gt;</c> lines. When set, the remote HIBP API
    /// call is disabled via <c>PwnedPasswordChecker</c>'s disabled flag.
    /// </summary>
    public string? LocalPwnedPasswordsPath { get; set; }

    /// <summary>
    /// Minimum length for a banned-words term to be considered. Terms shorter than
    /// this are skipped at load time. Protects against DoS-style single-character
    /// entries. Must be &gt;= 1. Default 4.
    /// </summary>
    public int MinBannedTermLength { get; set; } = 4;
}
