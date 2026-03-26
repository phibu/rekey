using Microsoft.Extensions.Options;
using PassReset.Common;
using PassReset.Web.Models;

namespace PassReset.Web.Services;

/// <summary>
/// Background service that runs daily and sends password-expiry reminder emails
/// to members of AllowedAdGroups whose passwords are approaching expiry.
/// </summary>
internal sealed class PasswordExpiryNotificationService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly PasswordExpiryNotificationSettings _notifSettings;
    private readonly ILogger<PasswordExpiryNotificationService> _logger;

    // Tracks (username, date) pairs that have already been notified today to avoid duplicates.
    private readonly HashSet<(string Username, DateOnly Date)> _notifiedToday = [];

    public PasswordExpiryNotificationService(
        IServiceProvider services,
        IOptions<PasswordExpiryNotificationSettings> notifSettings,
        ILogger<PasswordExpiryNotificationService> logger)
    {
        _services      = services;
        _notifSettings = notifSettings.Value;
        _logger        = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PasswordExpiryNotificationService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = ComputeDelayUntilNextRun();
            _logger.LogDebug("Next expiry notification run in {Delay}", delay);

            await Task.Delay(delay, stoppingToken);

            if (stoppingToken.IsCancellationRequested) break;

            // Clear dedup set at the start of each daily run — entries are only valid for the current day.
            _notifiedToday.Clear();

            await RunNotificationsAsync(stoppingToken);
        }
    }

    private async Task RunNotificationsAsync(CancellationToken ct)
    {
        _logger.LogInformation("Running password expiry notification check");

        try
        {
            // Resolve scoped/singleton services via a scope so this background service
            // doesn't hold onto long-lived AD connections.
            using var scope    = _services.CreateScope();
            var provider       = scope.ServiceProvider.GetRequiredService<IPasswordChangeProvider>();
            var emailService   = scope.ServiceProvider.GetRequiredService<IEmailService>();
            var passwordOptions = scope.ServiceProvider
                .GetRequiredService<IOptions<PassReset.PasswordProvider.PasswordChangeOptions>>().Value;

            var maxAge    = provider.GetDomainMaxPasswordAge();
            var threshold = TimeSpan.FromDays(_notifSettings.DaysBeforeExpiry);
            var today     = DateOnly.FromDateTime(DateTime.UtcNow);

            // Collect users from all allowed groups (deduplicated by username)
            var seenUsernames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var groups = passwordOptions.AllowedAdGroups ?? [];

            foreach (var group in groups)
            {
                foreach (var (username, email, lastSet) in provider.GetUsersInGroup(group))
                {
                    if (!seenUsernames.Add(username)) continue;
                    if (_notifiedToday.Contains((username, today))) continue;
                    if (lastSet == null) continue;  // must-change flag set — skip
                    if (maxAge == TimeSpan.MaxValue) continue;  // no expiry policy

                    var age      = DateTime.UtcNow - lastSet.Value.ToUniversalTime();
                    var remaining = maxAge - age;

                    if (remaining <= TimeSpan.Zero || remaining > threshold) continue;

                    var expiryDate     = DateTime.UtcNow.Add(remaining).ToString("yyyy-MM-dd");
                    var daysRemaining  = Math.Max(0, (int)Math.Ceiling(remaining.TotalDays));

                    var body = _notifSettings.ExpiryEmailBodyTemplate
                        .Replace("{Username}",      username,      StringComparison.Ordinal)
                        .Replace("{DaysRemaining}", daysRemaining.ToString(), StringComparison.Ordinal)
                        .Replace("{ExpiryDate}",    expiryDate,    StringComparison.Ordinal)
                        .Replace("{PassResetUrl}",   _notifSettings.PassResetUrl, StringComparison.Ordinal);

                    await emailService.SendAsync(email, username, _notifSettings.ExpiryEmailSubject, body);
                    _notifiedToday.Add((username, today));

                    _logger.LogInformation(
                        "Expiry notification sent to {Username} ({Email}) — expires in {Days} day(s)",
                        username, email, daysRemaining);
                }
            }

            _logger.LogInformation("Password expiry notification check complete");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during password expiry notification run");
        }
    }

    /// <summary>
    /// Calculates how long to wait until the next configured notification time (UTC).
    /// Minimum delay is 1 minute to prevent tight loops on misconfigured time values.
    /// </summary>
    private TimeSpan ComputeDelayUntilNextRun()
    {
        if (!TimeOnly.TryParse(_notifSettings.NotificationTimeUtc, out var targetTime))
            targetTime = new TimeOnly(8, 0); // default 08:00 UTC

        var now      = DateTime.UtcNow;
        var todayRun = new DateTime(now.Year, now.Month, now.Day,
                                   targetTime.Hour, targetTime.Minute, 0, DateTimeKind.Utc);

        var next = todayRun > now ? todayRun : todayRun.AddDays(1);
        var delay = next - now;

        return delay < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : delay;
    }
}
