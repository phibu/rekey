using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using PassReset.Web.Models;

namespace PassReset.Web.Services;

/// <summary>
/// Sends email via MailKit using STARTTLS on port 587 (or SMTPS on 465).
/// Transient failures are retried up to 3 times with exponential backoff (1s, 10s, 60s).
/// Permanent SMTP errors (auth failure, recipient rejected) fail immediately without retry.
/// Failures are swallowed and logged — email errors must never surface as user-facing failures.
/// </summary>
internal sealed class SmtpEmailService : IEmailService
{
    private static readonly TimeSpan[] RetryDelays = [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(60),
    ];

    private readonly SmtpSettings _settings;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IOptions<SmtpSettings> settings, ILogger<SmtpEmailService> logger)
    {
        _settings = settings.Value;
        _logger   = logger;
    }

    public async Task SendAsync(string toAddress, string toName, string subject, string body)
    {
        if (string.IsNullOrWhiteSpace(_settings.Host))
        {
            _logger.LogWarning("SMTP host is not configured — email to {To} skipped", toAddress);
            return;
        }

        // Build message once outside retry loop
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_settings.FromName, _settings.FromAddress));
        message.To.Add(new MailboxAddress(toName, toAddress));
        message.Subject = subject;
        message.Body    = new TextPart("plain") { Text = body };

        // Retry loop: up to RetryDelays.Length + 1 attempts (1 initial + 3 retries)
        for (int attempt = 0; attempt <= RetryDelays.Length; attempt++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                using var client = new SmtpClient();

                var secureOption = _settings.Port == 465
                    ? SecureSocketOptions.SslOnConnect
                    : SecureSocketOptions.StartTls;

                await client.ConnectAsync(_settings.Host, _settings.Port, secureOption, cts.Token);

                if (!string.IsNullOrWhiteSpace(_settings.Username))
                    await client.AuthenticateAsync(_settings.Username, _settings.Password, cts.Token);

                await client.SendAsync(message, cts.Token);
                await client.DisconnectAsync(quit: true, cts.Token);

                _logger.LogInformation("Email sent to {To} — subject: {Subject}", toAddress, subject);
                return; // Success — exit loop
            }
            catch (SmtpCommandException ex)
            {
                // Permanent SMTP rejection (5xx status, bad recipient, relay denied) — do NOT retry
                _logger.LogError(ex,
                    "Permanent SMTP rejection for {To} (code={Code}) — not retrying",
                    toAddress, ex.StatusCode);
                return;
            }
            catch (System.Security.Authentication.AuthenticationException ex)
            {
                // Authentication failure — do NOT retry
                _logger.LogError(ex,
                    "SMTP authentication failed for {To} — not retrying", toAddress);
                return;
            }
            catch (Exception ex) when (attempt < RetryDelays.Length)
            {
                // Transient error (network, timeout, protocol) — retry
                _logger.LogWarning(ex,
                    "Email attempt {Attempt}/{Max} failed for {To}, retrying in {Delay}",
                    attempt + 1, RetryDelays.Length + 1, toAddress, RetryDelays[attempt]);
                await Task.Delay(RetryDelays[attempt]);
            }
            catch (Exception ex)
            {
                // Final attempt failed — give up
                _logger.LogError(ex,
                    "Email failed after {Max} attempts for {To}",
                    RetryDelays.Length + 1, toAddress);
            }
        }
    }
}
