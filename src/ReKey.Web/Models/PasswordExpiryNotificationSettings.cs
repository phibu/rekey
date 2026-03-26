namespace ReKey.Web.Models;

/// <summary>
/// Configuration for the daily background service that notifies users in AllowedAdGroups
/// when their AD password is approaching expiry.
/// Supported body template tokens: {Username}, {DaysRemaining}, {ExpiryDate}, {ReKeyUrl}
/// </summary>
public class PasswordExpiryNotificationSettings
{
    /// <summary>Set true to enable daily expiry notification emails.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Send a notification when a user's password will expire within this many days.
    /// Default: 14.
    /// </summary>
    public int DaysBeforeExpiry { get; set; } = 14;

    /// <summary>
    /// Time of day (UTC) at which the daily check runs. Format: HH:mm (24-hour).
    /// Default: "08:00".
    /// </summary>
    public string NotificationTimeUtc { get; set; } = "08:00";

    /// <summary>Public URL of this ReKey instance, included in notification emails.</summary>
    public string ReKeyUrl { get; set; } = string.Empty;

    /// <summary>Subject line for expiry notification emails.</summary>
    public string ExpiryEmailSubject { get; set; } = "Your password will expire soon";

    /// <summary>
    /// Plain-text email body for expiry notifications. Supported tokens:
    /// {Username}      — the account username
    /// {DaysRemaining} — days until the password expires
    /// {ExpiryDate}    — the formatted expiry date
    /// {ReKeyUrl}   — link to this ReKey instance
    /// </summary>
    public string ExpiryEmailBodyTemplate { get; set; } =
        "Hello {Username},\n\n" +
        "Your Active Directory password will expire in {DaysRemaining} day(s) on {ExpiryDate}.\n\n" +
        "Please change your password before it expires: {ReKeyUrl}";
}
