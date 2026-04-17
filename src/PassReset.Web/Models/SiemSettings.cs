namespace PassReset.Web.Models;

/// <summary>Top-level SIEM integration settings.</summary>
public class SiemSettings
{
    public SyslogSettings Syslog { get; set; } = new();
    public SiemAlertEmailSettings AlertEmail { get; set; } = new();
}

/// <summary>Syslog forwarding (RFC 5424) over UDP or TCP.</summary>
public class SyslogSettings
{
    /// <summary>Set true to enable syslog forwarding.</summary>
    public bool Enabled { get; set; }

    /// <summary>Hostname or IP address of the syslog collector / SIEM.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>UDP/TCP port. Default: 514.</summary>
    public int Port { get; set; } = 514;

    /// <summary>Transport protocol: <c>UDP</c> (default) or <c>TCP</c>.</summary>
    public string Protocol { get; set; } = "UDP";

    /// <summary>
    /// RFC 5424 facility number. Default: 10 (security/authorisation messages — authpriv).
    /// Common values: 0=kern, 1=user, 4=auth, 10=authpriv, 16–23=local0–local7.
    /// </summary>
    public int Facility { get; set; } = 10;

    /// <summary>APP-NAME field in the syslog header. Default: <c>PassReset</c>.</summary>
    public string AppName { get; set; } = "PassReset";

    /// <summary>
    /// STAB-015 (D-20): RFC 5424 SD-ID for the structured-data element emitted by
    /// <see cref="PassReset.Web.Services.SiemSyslogFormatter"/>. Default uses IANA
    /// reserved PEN 32473 (documentation/example). Operators with a registered
    /// Private Enterprise Number can override in appsettings (e.g. "myorg@12345").
    /// Must be 1-32 printusascii chars excluding '=', space, ']', '"' per RFC 5424 §6.3.2.
    /// </summary>
    public string SdId { get; set; } = "passreset@32473";
}

/// <summary>Email alert delivery for high-severity SIEM events.</summary>
public class SiemAlertEmailSettings
{
    /// <summary>Set true to enable email alerts for selected event types.</summary>
    public bool Enabled { get; set; }

    /// <summary>Recipient email addresses for alert messages.</summary>
    public List<string> Recipients { get; set; } = [];

    /// <summary>
    /// Event type names that trigger an email alert.
    /// Valid values: PasswordChanged, InvalidCredentials, UserNotFound, PortalLockout,
    /// ApproachingLockout, RateLimitExceeded, RecaptchaFailed, ChangeNotPermitted,
    /// ValidationFailed, Generic.
    /// Default: PortalLockout only.
    /// </summary>
    public List<string> AlertOnEvents { get; set; } = ["PortalLockout"];
}
