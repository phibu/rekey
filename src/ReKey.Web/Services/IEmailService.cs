namespace ReKey.Web.Services;

/// <summary>
/// Abstraction for sending outbound emails.
/// Implementations must not throw — failures should be logged and swallowed
/// so that email errors never surface as user-facing failures.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends an email to a single recipient.
    /// </summary>
    /// <param name="toAddress">Recipient email address.</param>
    /// <param name="toName">Recipient display name.</param>
    /// <param name="subject">Email subject line.</param>
    /// <param name="body">Plain-text email body.</param>
    Task SendAsync(string toAddress, string toName, string subject, string body);
}
