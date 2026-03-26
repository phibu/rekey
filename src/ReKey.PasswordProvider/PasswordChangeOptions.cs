using ReKey.Common;

namespace ReKey.PasswordProvider;

/// <summary>
/// Represents the options / configuration for the Windows AD password change provider.
/// </summary>
/// <seealso cref="IAppSettings" />
public class PasswordChangeOptions : IAppSettings
{
    private string? _defaultDomain;
    private string? _ldapPassword;
    private string[]? _ldapHostnames;
    private string? _ldapUsername;

    /// <summary>Gets or sets a value indicating whether to use automatic domain context.</summary>
    public bool UseAutomaticContext { get; set; } = true;

    /// <summary>Gets or sets the restricted AD groups.</summary>
    public List<string>? RestrictedAdGroups { get; set; }

    /// <summary>Gets or sets the allowed AD groups.</summary>
    public List<string>? AllowedAdGroups { get; set; }

    /// <summary>Gets or sets the identifier type for user lookup.</summary>
    public string? IdTypeForUser { get; set; }

    /// <summary>Gets or sets a value indicating whether to update the last password timestamp.</summary>
    public bool UpdateLastPassword { get; set; }

    /// <summary>
    /// When true, clears the "must change password at next logon" AD flag after a successful password change.
    /// </summary>
    public bool ClearMustChangePasswordFlag { get; set; } = true;

    /// <summary>
    /// When true, blocks password changes that occur before the domain minimum password age (minPwdAge) has elapsed.
    /// </summary>
    public bool EnforceMinimumPasswordAge { get; set; } = true;

    /// <inheritdoc />
    public string DefaultDomain
    {
        get => _defaultDomain ?? string.Empty;
        set => _defaultDomain = value;
    }

    /// <inheritdoc />
    public int LdapPort { get; set; }

    /// <inheritdoc />
    public string[] LdapHostnames
    {
        get => _ldapHostnames ?? [];
        set => _ldapHostnames = value;
    }

    /// <inheritdoc />
    public string LdapPassword
    {
        get => _ldapPassword ?? string.Empty;
        set => _ldapPassword = value;
    }

    /// <inheritdoc />
    public string LdapUsername
    {
        get => _ldapUsername ?? string.Empty;
        set => _ldapUsername = value;
    }
}
