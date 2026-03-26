namespace ReKey.Web.Models;

/// <summary>
/// Configuration for the password-changed email notification sent after a successful password change.
/// Supported body template tokens: {Username}, {Timestamp}, {IpAddress}
/// </summary>
public class EmailNotificationSettings
{
    /// <summary>Set true to enable email notifications on successful password change.</summary>
    public bool Enabled { get; set; }

    /// <summary>Email subject line.</summary>
    public string Subject { get; set; } = "Your password has been changed";

    /// <summary>
    /// Plain-text email body. Supported tokens:
    /// {Username}  — the account username
    /// {Timestamp} — UTC date/time of the change
    /// {IpAddress} — originating IP address of the request
    /// </summary>
    public string BodyTemplate { get; set; } =
        "Hello {Username},\n\n" +
        "Your password was changed successfully on {Timestamp} from IP address {IpAddress}.\n\n" +
        "If you did not make this change, contact IT Support immediately.";
}
