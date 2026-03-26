namespace ReKey.Web.Models;

/// <summary>
/// Represents the Web server settings.
/// </summary>
public class WebSettings
{
    /// <summary>Gets or sets a value indicating whether HTTPS redirect is enabled.</summary>
    public bool EnableHttpsRedirect { get; set; }

    /// <summary>
    /// When true, the <see cref="ReKey.Web.Helpers.DebugPasswordChangeProvider"/> and
    /// <see cref="ReKey.Web.Helpers.NoOpEmailService"/> are registered instead of the
    /// real Active Directory / SMTP implementations. Controlled at runtime via configuration
    /// (no compile-time conditionals). Default: false.
    /// </summary>
    public bool UseDebugProvider { get; set; }
}
