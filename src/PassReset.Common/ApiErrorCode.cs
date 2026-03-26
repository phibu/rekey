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
}
