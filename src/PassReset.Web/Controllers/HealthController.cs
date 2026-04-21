using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PassReset.Common;
using PassReset.PasswordProvider;
using PassReset.Web.Models;
using PassReset.Web.Services;

namespace PassReset.Web.Controllers;

/// <summary>
/// Provides a health probe for load balancers and monitoring.
/// GET /api/health — returns nested per-dependency checks (AD, SMTP, ExpiryService)
/// with an aggregate rollup. 200 when healthy; 503 when degraded/unhealthy.
/// Response body contains no secrets.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class HealthController : ControllerBase
{
    private readonly IOptions<PasswordChangeOptions> _options;
    private readonly IOptions<SmtpSettings> _smtp;
    private readonly IOptions<EmailNotificationSettings> _emailNotif;
    private readonly IOptions<PasswordExpiryNotificationSettings> _expiryNotif;
    private readonly IExpiryServiceDiagnostics _expiryDiagnostics;
    private readonly ILockoutDiagnostics _lockoutDiagnostics;
    private readonly IAdConnectivityProbe _adProbe;
    private readonly ILogger<HealthController> _logger;

    public HealthController(
        IOptions<PasswordChangeOptions> options,
        IOptions<SmtpSettings> smtp,
        IOptions<EmailNotificationSettings> emailNotif,
        IOptions<PasswordExpiryNotificationSettings> expiryNotif,
        IExpiryServiceDiagnostics expiryDiagnostics,
        ILockoutDiagnostics lockoutDiagnostics,
        IAdConnectivityProbe adProbe,
        ILogger<HealthController> logger)
    {
        _options            = options;
        _smtp               = smtp;
        _emailNotif         = emailNotif;
        _expiryNotif        = expiryNotif;
        _expiryDiagnostics  = expiryDiagnostics;
        _lockoutDiagnostics = lockoutDiagnostics;
        _adProbe            = adProbe;
        _logger             = logger;
    }

    /// <summary>Returns the application health status with nested AD/SMTP/ExpiryService checks.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetAsync()
    {
        var adResult   = await CheckAdConnectivityAsync();
        var smtpResult = await CheckSmtpAsync();
        var expResult  = CheckExpiryService();

        var statuses = new[] { adResult.status, smtpResult.status, expResult.status };
        var aggregate = statuses.Contains("unhealthy") ? "unhealthy"
                       : statuses.Contains("degraded")  ? "degraded"
                       : "healthy";

        var now = DateTimeOffset.UtcNow;
        var result = new
        {
            status    = aggregate,
            timestamp = now,
            checks    = new
            {
                ad            = new { status = adResult.status,   latency_ms = adResult.latencyMs,   last_checked = now },
                smtp          = new { status = smtpResult.status, latency_ms = smtpResult.latencyMs, last_checked = now, skipped = smtpResult.skipped },
                expiryService = new { status = expResult.status,  latency_ms = expResult.latencyMs,  last_checked = now },
            },
        };

        return aggregate == "healthy" ? Ok(result) : StatusCode(503, result);
    }

    private async Task<(string status, long latencyMs, bool skipped)> CheckSmtpAsync()
    {
        var emailEnabled  = _emailNotif.Value.Enabled;
        var expiryEnabled = _expiryNotif.Value.Enabled;
        if (!emailEnabled && !expiryEnabled)
            return ("skipped", 0, true);

        var sw = Stopwatch.StartNew();
        try
        {
            using var cts    = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var client = new TcpClient();
            await client.ConnectAsync(_smtp.Value.Host, _smtp.Value.Port, cts.Token);
            return ("healthy", sw.ElapsedMilliseconds, false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SMTP health check failed ({Host}:{Port})", _smtp.Value.Host, _smtp.Value.Port);
            return ("unhealthy", sw.ElapsedMilliseconds, false);
        }
    }

    private (string status, long latencyMs) CheckExpiryService()
    {
        if (!_expiryDiagnostics.IsEnabled)
            return ("not-enabled", 0);
        if (_expiryDiagnostics.LastTickUtc is null)
            return ("degraded", 0);
        return ("healthy", 0);
    }

    private async Task<(string status, long latencyMs)> CheckAdConnectivityAsync()
    {
        var result = await _adProbe.CheckAsync(HttpContext.RequestAborted);
        var status = result.Status switch
        {
            AdProbeStatus.Healthy        => "healthy",
            AdProbeStatus.NotConfigured  => "healthy",   // debug/unconfigured scenarios
            _                            => "unhealthy",
        };
        return (status, result.LatencyMs);
    }
}
