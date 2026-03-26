using ReKey.Web.Services;

namespace ReKey.Web.Helpers;

/// <summary>
/// No-op email service used in development / when email notifications are disabled.
/// All send operations are logged and silently discarded.
/// </summary>
internal sealed class NoOpEmailService : IEmailService
{
    private readonly ILogger<NoOpEmailService> _logger;

    public NoOpEmailService(ILogger<NoOpEmailService> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(string toAddress, string toName, string subject, string body)
    {
        _logger.LogDebug(
            "NoOpEmailService: suppressed email to={To} subject={Subject}",
            toAddress, subject);
        return Task.CompletedTask;
    }
}
