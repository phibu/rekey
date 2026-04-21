using System.Text.Json.Serialization;
using PassReset.Common.LocalPolicy;

namespace PassReset.Common;

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
    /// <remarks>Deprecated — use <see cref="AllowedUsernameAttributes"/> instead.</remarks>
    public string? IdTypeForUser { get; set; }

    /// <summary>
    /// Maximum consecutive failed credential attempts allowed through this portal before
    /// the username is blocked at the application layer (without contacting AD).
    /// Set to 0 to disable portal-level lockout. Default: 3.
    /// Should be set to at least 2 less than the AD account lockout threshold so the
    /// portal blocks before the AD lockout policy triggers.
    /// </summary>
    public int PortalLockoutThreshold { get; set; } = 3;

    /// <summary>
    /// Duration of the portal lockout window. The failure counter resets after this period.
    /// Should be greater than or equal to the AD lockout observation window.
    /// Default: 30 minutes.
    /// </summary>
    public TimeSpan PortalLockoutWindow { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Ordered list of AD attributes accepted as username input.
    /// The provider tries each in order and uses the first match.
    /// Supported values: <c>samaccountname</c>, <c>userprincipalname</c>, <c>mail</c>.
    /// When empty, falls back to <see cref="IdTypeForUser"/>.
    /// </summary>
    public string[] AllowedUsernameAttributes { get; set; } = ["samaccountname"];

    /// <summary>Gets or sets a value indicating whether to update the last password timestamp.</summary>
    public bool UpdateLastPassword { get; set; }

    /// <summary>
    /// When true, clears the "must change password at next logon" AD flag after a successful password change.
    /// </summary>
    public bool ClearMustChangePasswordFlag { get; set; } = true;

    /// <summary>
    /// When true, allows password changes to proceed when the HaveIBeenPwned API is
    /// unreachable. The breach check is skipped with a warning log entry.
    /// When false (default), unreachable HIBP API blocks the password change.
    /// </summary>
    public bool FailOpenOnPwnedCheckUnavailable { get; set; }

    /// <summary>
    /// When true, allows the password change if both AD group membership checks
    /// (GetGroups and GetAuthorizationGroups) fail.
    /// Default: false (deny on failure — safer).
    /// BREAKING CHANGE from v1 behavior which allowed by default.
    /// </summary>
    public bool AllowOnGroupCheckFailure { get; set; }

    /// <summary>
    /// When true, falls back to the administrative <c>SetPassword</c> API if the user-initiated
    /// <c>ChangePassword</c> call fails with a COMException (only when <see cref="UseAutomaticContext"/> is false).
    /// Warning: <c>SetPassword</c> bypasses AD password history enforcement.
    /// Default: false.
    /// </summary>
    /// <remarks>
    /// Windows provider only. The LDAP provider ignores this flag — AD's server-side enforcement
    /// is authoritative, and no equivalent COMException fallback exists in
    /// System.DirectoryServices.Protocols.
    /// </remarks>
    public bool AllowSetPasswordFallback { get; set; }

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
    public int LdapPort { get; set; } = 636;

    /// <inheritdoc />
    public bool LdapUseSsl { get; set; } = true;

    /// <inheritdoc />
    public string[] LdapHostnames
    {
        get => _ldapHostnames ?? [];
        set => _ldapHostnames = value;
    }

    /// <inheritdoc />
    [JsonIgnore]
    public string LdapPassword
    {
        get => _ldapPassword ?? string.Empty;
        set => _ldapPassword = value;
    }

    /// <summary>
    /// Selects which password provider implementation to use. Default <see cref="ProviderMode.Auto"/> picks
    /// Windows on Windows platforms, Ldap elsewhere. Windows deployments upgrading from v1.4.x see no change.
    /// </summary>
    public ProviderMode ProviderMode { get; set; } = ProviderMode.Auto;

    /// <summary>
    /// Distinguished name of the AD service account used to bind over LDAPS when
    /// <see cref="ProviderMode"/> is <see cref="ProviderMode.Ldap"/> (or <see cref="ProviderMode.Auto"/>
    /// on non-Windows). Required: grant this account the 'Change Password' extended right on the target OU.
    /// See <c>docs/AD-ServiceAccount-LDAP-Setup.md</c>.
    /// </summary>
    public string ServiceAccountDn { get; set; } = string.Empty;

    /// <summary>
    /// Service account password for LDAPS bind. Bind via environment variable
    /// <c>PasswordChangeOptions__ServiceAccountPassword</c> per the STAB-017 env-var pattern;
    /// never commit plaintext.
    /// </summary>
    [JsonIgnore]
    public string ServiceAccountPassword { get; set; } = string.Empty;

    /// <summary>
    /// Base DN for user searches (typically the domain root, e.g. <c>DC=corp,DC=example,DC=com</c>).
    /// Required when <see cref="ProviderMode"/> resolves to <see cref="ProviderMode.Ldap"/>.
    /// </summary>
    public string BaseDn { get; set; } = string.Empty;

    /// <summary>
    /// Optional SHA-1 or SHA-256 thumbprint allow-list for LDAPS certificates whose trust root is
    /// not in the system certificate store (e.g. Linux hosts talking to an internal-CA-issued DC cert).
    /// Empty list means 'use the system trust store only'. Mirrors the
    /// <c>SmtpSettings.TrustedCertificateThumbprints</c> pattern.
    /// </summary>
    public List<string> LdapTrustedCertificateThumbprints { get; set; } = new();

    /// <inheritdoc />
    public string LdapUsername
    {
        get => _ldapUsername ?? string.Empty;
        set => _ldapUsername = value;
    }

    /// <summary>
    /// Strategy used to resolve the recipient email address when sending password-changed notifications.
    /// Default: <see cref="EmailAddressStrategy.Mail"/>.
    /// </summary>
    public EmailAddressStrategy NotificationEmailStrategy { get; set; } = EmailAddressStrategy.Mail;

    /// <summary>
    /// Domain suffix appended to the SAM account name when
    /// <see cref="NotificationEmailStrategy"/> is <see cref="EmailAddressStrategy.SamAccountNameAtDomain"/>.
    /// Falls back to <see cref="DefaultDomain"/> when empty.
    /// </summary>
    public string NotificationEmailDomain { get; set; } = string.Empty;

    /// <summary>
    /// Template string used when <see cref="NotificationEmailStrategy"/> is <see cref="EmailAddressStrategy.Custom"/>.
    /// Supported placeholders: <c>{samaccountname}</c>, <c>{userprincipalname}</c>, <c>{mail}</c>, <c>{defaultdomain}</c>.
    /// Example: <c>{samaccountname}@{defaultdomain}</c>
    /// </summary>
    public string NotificationEmailTemplate { get; set; } = string.Empty;

    /// <summary>
    /// Operator-managed local password-policy settings: banned-words list and optional
    /// bulk HIBP SHA-1 corpus. See <see cref="LocalPolicyOptions"/>. Null-safe: when
    /// the whole section is absent from configuration, defaults apply (both paths null,
    /// <see cref="LocalPolicyOptions.MinBannedTermLength"/> = 4).
    /// </summary>
    public LocalPolicyOptions LocalPolicy { get; set; } = new();
}

/// <summary>
/// Determines how the recipient email address is resolved from AD attributes
/// for password-changed notification emails.
/// </summary>
public enum EmailAddressStrategy
{
    /// <summary>Use the AD <c>mail</c> attribute directly (default).</summary>
    Mail,

    /// <summary>Use the user's <c>userPrincipalName</c> as the email address.</summary>
    UserPrincipalName,

    /// <summary>
    /// Compose the address as <c>{samaccountname}@{NotificationEmailDomain}</c>.
    /// Falls back to <c>{samaccountname}@{DefaultDomain}</c> when <c>NotificationEmailDomain</c> is empty.
    /// </summary>
    SamAccountNameAtDomain,

    /// <summary>
    /// Evaluate <see cref="PasswordChangeOptions.NotificationEmailTemplate"/> against AD attribute values.
    /// Supported placeholders: <c>{samaccountname}</c>, <c>{userprincipalname}</c>, <c>{mail}</c>, <c>{defaultdomain}</c>.
    /// </summary>
    Custom,
}
