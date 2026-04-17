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
internal sealed class SiemService : ISiemService, IDisposable
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

    // Pooled connections for syslog delivery — avoids creating a new connection per event.
    private readonly object _syslogLock = new();
    private UdpClient? _udpClient;
    private TcpClient? _tcpClient;
    private NetworkStream? _tcpStream;

    public SiemService(
        IOptions<SiemSettings> settings,
        IEmailService emailService,
        ILogger<SiemService> logger)
    {
        _settings     = settings.Value;
        _emailService = emailService;
        _logger       = logger;
    }

    public void Dispose()
    {
        _tcpStream?.Dispose();
        _tcpClient?.Dispose();
        _udpClient?.Dispose();
    }

    /// <inheritdoc />
    public void LogEvent(SiemEventType eventType, string username, string ipAddress, string? detail = null)
    {
        if (_settings.Syslog.Enabled)
            EmitSyslog(eventType, username, ipAddress, detail);

        if (_settings.AlertEmail.Enabled)
            EnqueueAlertEmail(eventType, username, ipAddress, detail);
    }

    /// <inheritdoc />
    public void LogEvent(AuditEvent evt)
    {
        if (_settings.Syslog.Enabled)
            EmitSyslogStructured(evt);

        if (_settings.AlertEmail.Enabled)
            EnqueueAlertEmail(evt.EventType, evt.Username, evt.ClientIp ?? string.Empty, evt.Detail);
    }

    // ─── Syslog ───────────────────────────────────────────────────────────────

    private void EmitSyslog(SiemEventType eventType, string username, string ipAddress, string? detail)
    {
        try
        {
            var syslog   = _settings.Syslog;
            var severity = SeverityMap.GetValueOrDefault(eventType, 5);
            var hostname = Dns.GetHostName();

            // RFC 5424 formatting delegated to pure static helper (testable without sockets).
            // WR-01: pass configured SdId so operators can override the default (passreset@32473).
            var message = SiemSyslogFormatter.Format(
                timestampUtc: DateTimeOffset.UtcNow,
                facility:     syslog.Facility,
                severity:     severity,
                hostname:     hostname,
                appName:      syslog.AppName,
                sdId:         syslog.SdId,
                eventType:    eventType.ToString(),
                username:     username,
                ipAddress:    ipAddress,
                detail:       detail);

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

    // STAB-015: Emit an RFC 5424 STRUCTURED-DATA element for an AuditEvent. Mirrors
    // EmitSyslog's transport + try/catch-swallow-and-log invariant so that audit
    // emission never escapes to the hot path.
    private void EmitSyslogStructured(AuditEvent evt)
    {
        try
        {
            var syslog   = _settings.Syslog;
            var severity = SeverityMap.GetValueOrDefault(evt.EventType, 5);
            var hostname = Dns.GetHostName();

            var message = SiemSyslogFormatter.Format(
                timestampUtc: DateTimeOffset.UtcNow,
                facility:     syslog.Facility,
                severity:     severity,
                hostname:     hostname,
                appName:      syslog.AppName,
                sdId:         syslog.SdId,
                evt:          evt);

            var bytes = Encoding.UTF8.GetBytes(message);

            if (syslog.Protocol.Equals("TCP", StringComparison.OrdinalIgnoreCase))
                SendTcp(syslog.Host, syslog.Port, bytes);
            else
                SendUdp(syslog.Host, syslog.Port, bytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Syslog structured delivery failed for event {Event} user {User}", evt.EventType, evt.Username);
        }
    }

    private void SendUdp(string host, int port, byte[] bytes)
    {
        lock (_syslogLock)
        {
            _udpClient ??= new UdpClient(host, port);
            _udpClient.Send(bytes, bytes.Length);
        }
    }

    private void SendTcp(string host, int port, byte[] bytes)
    {
        lock (_syslogLock)
        {
            try
            {
                if (_tcpClient is null || !_tcpClient.Connected)
                {
                    _tcpStream?.Dispose();
                    _tcpClient?.Dispose();
                    _tcpClient = new TcpClient(host, port);
                    _tcpStream = _tcpClient.GetStream();
                }

                // RFC 6587 octet-counting framing
                var frame = Encoding.ASCII.GetBytes($"{bytes.Length} ");
                _tcpStream!.Write(frame, 0, frame.Length);
                _tcpStream.Write(bytes, 0, bytes.Length);
                _tcpStream.Flush();
            }
            catch
            {
                // Connection failed — reset so the next call reconnects.
                _tcpStream?.Dispose();
                _tcpClient?.Dispose();
                _tcpStream = null;
                _tcpClient = null;
                throw;
            }
        }
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

}
