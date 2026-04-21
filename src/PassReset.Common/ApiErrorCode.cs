namespace PassReset.Common;

/// <summary>
/// Represents API error codes.
/// </summary>
public enum ApiErrorCode
{
    /// <summary>The generic</summary>
    Generic = 0,

    /// <summary>The field required</summary>
    FieldRequired = 1,

    /// <summary>The field mismatch</summary>
    FieldMismatch = 2,

    /// <summary>The user not found</summary>
    UserNotFound = 3,

    /// <summary>The invalid credentials</summary>
    InvalidCredentials = 4,

    /// <summary>The invalid captcha</summary>
    InvalidCaptcha = 5,

    /// <summary>The change not permitted</summary>
    ChangeNotPermitted = 6,

    /// <summary>The invalid domain</summary>
    InvalidDomain = 7,

    /// <summary>LDAP problem connection</summary>
    LdapProblem = 8,

    /// <summary>Complex password issue</summary>
    ComplexPassword = 9,

    /// <summary>The score is minor than the Minimum Score</summary>
    MinimumScore = 10,

    /// <summary>The distance is minor than the Minimum Distance</summary>
    MinimumDistance = 11,

    /// <summary>The password is in Pwned database</summary>
    PwnedPassword = 12,

    /// <summary>Password was changed too recently; minimum password age has not elapsed.</summary>
    PasswordTooYoung = 13,

    /// <summary>The user account is disabled.</summary>
    AccountDisabled = 14,

    /// <summary>Too many requests; rate limit exceeded.</summary>
    RateLimitExceeded = 15,

    /// <summary>The breach-password check service was unreachable. The change was not blocked — try again.</summary>
    PwnedPasswordCheckFailed = 16,

    /// <summary>Portal-layer lockout: too many consecutive failed attempts. The AD bind was not attempted.</summary>
    PortalLockout = 17,

    /// <summary>
    /// Invalid credentials and one more failed attempt will trigger the portal lockout.
    /// Carries the same semantic as <see cref="InvalidCredentials"/> but signals the UI to show a warning.
    /// </summary>
    ApproachingLockout = 18,

    /// <summary>
    /// Active Directory rejected the password change because the domain's minimum password age
    /// (<c>minPwdAge</c>) has not yet elapsed since the previous change. Distinct from
    /// <see cref="PasswordTooYoung"/> which is the portal-side pre-check.
    /// </summary>
    PasswordTooRecentlyChanged = 19,

    /// <summary>
    /// The new password matched a term in the operator-configured banned-words list.
    /// Rejected by <c>LocalPolicyPasswordChangeProvider</c> before any AD round-trip.
    /// </summary>
    BannedWord = 20,

    /// <summary>
    /// The new password appeared in the operator-hosted local HIBP SHA-1 corpus.
    /// Rejected by <c>LocalPolicyPasswordChangeProvider</c>. Distinct from <see cref="PwnedPassword"/>,
    /// which is returned by the remote HIBP API path.
    /// </summary>
    LocallyKnownPwned = 21,
}
