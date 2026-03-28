using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;
using PassReset.Web.Models;

namespace PassReset.Web.Services;

/// <summary>
/// Forwards security events to a SIEM via RFC 5424 syslog (UDP or TCP)
/// and optional email alerts through the configured <see cref="IEmailService"/>.
/// All failures are swallowed and logged — SIEM errors must never affect the user-facing response.
/// </summary>
internal sealed class SiemService : ISiemService
{
    // RFC 5424 severity numbers
    private static readonly Dictionary<SiemEventType, int> SeverityMap = new()
    {
        [SiemEventType.PasswordChanged]    = 5, // Notice
        [SiemEventType.InvalidCredentials] = 4, // Warning
        [SiemEventType.UserNotFound]       = 5, // Notice
        [SiemEventType.PortalLockout]      = 4, // Warning
        [SiemEventType.ApproachingLockout] = 4, // Warning
        [SiemEventType.RateLimitExceeded]  = 4, // Warning
        [SiemEventType.RecaptchaFailed]    = 4, // Warning
        [SiemEventType.ChangeNotPermitted] = 4, // Warning
        [SiemEventType.ValidationFailed]   = 5, // Notice
        [SiemEventType.Generic]            = 3, // Error
    };

    private readonly SiemSettings _settings;
    private readonly IEmailService _emailService;
    private readonly ILogger<SiemService> _logger;

    public SiemService(
        IOptions<SiemSettings> settings,
        IEmailService emailService,
        ILogger<SiemService> logger)
    {
        _settings     = settings.Value;
        _emailService = emailService;
        _logger       = logger;
    }

    /// <inheritdoc />
    public void LogEvent(SiemEventType eventType, string username, string ipAddress, string? detail = null)
    {
        if (_settings.Syslog.Enabled)
            EmitSyslog(eventType, username, ipAddress, detail);

        if (_settings.AlertEmail.Enabled)
            EnqueueAlertEmail(eventType, username, ipAddress, detail);
    }

    // ─── Syslog ───────────────────────────────────────────────────────────────

    private void EmitSyslog(SiemEventType eventType, string username, string ipAddress, string? detail)
    {
        try
        {
            var syslog   = _settings.Syslog;
            var severity = SeverityMap.GetValueOrDefault(eventType, 5);
            var priority = syslog.Facility * 8 + severity;
            var ts       = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            var hostname = Dns.GetHostName();
            var detailPart = detail != null ? $" detail=\"{EscapeSd(detail)}\"" : string.Empty;

            // RFC 5424: <PRIVAL>VERSION TIMESTAMP HOSTNAME APP-NAME PROCID MSGID STRUCTURED-DATA
            var message = $"<{priority}>1 {ts} {hostname} {syslog.AppName} - - - " +
                          $"[PassReset@0 event=\"{eventType}\" user=\"{EscapeSd(username)}\" ip=\"{ipAddress}\"{detailPart}]";

            var bytes = Encoding.UTF8.GetBytes(message);

            if (syslog.Protocol.Equals("TCP", StringComparison.OrdinalIgnoreCase))
                SendTcp(syslog.Host, syslog.Port, bytes);
            else
                SendUdp(syslog.Host, syslog.Port, bytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Syslog delivery failed for event {Event} user {User}", eventType, username);
        }
    }

    private static void SendUdp(string host, int port, byte[] bytes)
    {
        using var client = new UdpClient();
        client.Send(bytes, bytes.Length, host, port);
    }

    private static void SendTcp(string host, int port, byte[] bytes)
    {
        using var client = new TcpClient(host, port);
        using var stream = client.GetStream();
        // RFC 6587 octet-counting framing
        var frame = Encoding.ASCII.GetBytes($"{bytes.Length} ");
        stream.Write(frame, 0, frame.Length);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }

    // ─── Email alerts ─────────────────────────────────────────────────────────

    private void EnqueueAlertEmail(SiemEventType eventType, string username, string ipAddress, string? detail)
    {
        var cfg = _settings.AlertEmail;
        if (cfg.Recipients is not { Count: > 0 }) return;

        if (!cfg.AlertOnEvents.Any(e =>
                string.Equals(e, eventType.ToString(), StringComparison.OrdinalIgnoreCase)))
            return;

        var subject = $"[PassReset SIEM] {eventType} — {username}";
        var body    = $"Security event detected in PassReset.\n\n"  +
                      $"Event:    {eventType}\n"                     +
                      $"User:     {username}\n"                      +
                      $"IP:       {ipAddress}\n"                     +
                      $"Time:     {DateTime.UtcNow:u} UTC\n";
        if (detail != null)
            body += $"Detail:   {detail}\n";

        var emailSvc = _emailService;
        foreach (var recipient in cfg.Recipients)
        {
            var to = recipient;
            _ = Task.Run(async () => await emailSvc.SendAsync(to, "Security Team", subject, body));
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Escapes RFC 5424 SD-PARAM special characters: backslash, double-quote, closing bracket.</summary>
    private static string EscapeSd(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("\"", "\\\"", StringComparison.Ordinal)
             .Replace("]",  "\\]",  StringComparison.Ordinal);
}
