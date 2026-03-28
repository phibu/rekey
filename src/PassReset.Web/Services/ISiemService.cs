namespace PassReset.Web.Services;

/// <summary>
/// Security event types forwarded to the SIEM via syslog and/or email alerts.
/// </summary>
public enum SiemEventType
{
    /// <summary>Password was changed successfully.</summary>
    PasswordChanged,

    /// <summary>Wrong current password supplied.</summary>
    InvalidCredentials,

    /// <summary>Username not found in AD.</summary>
    UserNotFound,

    /// <summary>Portal lockout threshold reached — further attempts blocked without contacting AD.</summary>
    PortalLockout,

    /// <summary>One more wrong attempt will trigger portal lockout.</summary>
    ApproachingLockout,

    /// <summary>Request rejected by the rate limiter (429).</summary>
    RateLimitExceeded,

    /// <summary>reCAPTCHA v3 validation failed.</summary>
    RecaptchaFailed,

    /// <summary>Password change not permitted due to group membership rules.</summary>
    ChangeNotPermitted,

    /// <summary>Request rejected by model validation.</summary>
    ValidationFailed,

    /// <summary>Unexpected server-side error (may indicate AD unreachability).</summary>
    Generic,
}

/// <summary>
/// Forwards security events to a SIEM via syslog and/or email alerts.
/// Implementations must not throw — failures must be logged and swallowed.
/// </summary>
public interface ISiemService
{
    /// <summary>Records a security event synchronously (no async I/O on the hot path).</summary>
    void LogEvent(SiemEventType eventType, string username, string ipAddress, string? detail = null);
}
