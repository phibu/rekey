using Microsoft.Extensions.Options;
using PassReset.Web.Services;

namespace PassReset.Web.Models;

/// <summary>
/// Validates <see cref="SiemSettings"/> at application startup. Each nested section
/// (syslog, alert email) is validated only when its own <c>Enabled</c> flag is true.
/// </summary>
public sealed class SiemSettingsValidator : IValidateOptions<SiemSettings>
{
    private static readonly string[] ValidProtocols = ["UDP", "TCP"];

    private static string Fmt(string path, string reason, string actual)
        => $"{path}: {reason} (got \"{actual}\"). Edit appsettings.Production.json or run Install-PassReset.ps1 -Reconfigure.";

    public ValidateOptionsResult Validate(string? name, SiemSettings options)
    {
        var failures = new List<string>();

        var syslog = options.Syslog;
        if (syslog is not null && syslog.Enabled)
        {
            if (string.IsNullOrWhiteSpace(syslog.Host))
                failures.Add(Fmt(
                    "SiemSettings.Syslog.Host",
                    "must be non-empty when Syslog.Enabled is true",
                    ""));

            if (syslog.Port <= 0 || syslog.Port > 65535)
                failures.Add(Fmt(
                    "SiemSettings.Syslog.Port",
                    "must be a valid TCP/UDP port (1-65535)",
                    syslog.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)));

            if (!ValidProtocols.Contains(syslog.Protocol, StringComparer.OrdinalIgnoreCase))
                failures.Add(Fmt(
                    "SiemSettings.Syslog.Protocol",
                    "must be 'UDP' or 'TCP'",
                    syslog.Protocol ?? ""));

            // STAB-015 (D-20): SD-ID syntax per RFC 5424 §6.3.2 — 1-32 printusascii chars
            // excluding '=', space, ']', '"'.
            if (string.IsNullOrEmpty(syslog.SdId)
                || syslog.SdId.Length > 32
                || syslog.SdId.IndexOfAny([' ', '=', ']', '"']) >= 0)
            {
                failures.Add(Fmt(
                    "SiemSettings.Syslog.SdId",
                    "must be 1-32 RFC 5424 printusascii chars excluding '=', space, ']', '\"' (e.g. 'passreset@32473')",
                    syslog.SdId ?? ""));
            }
        }

        var alert = options.AlertEmail;
        if (alert is not null && alert.Enabled)
        {
            if (alert.Recipients is null || alert.Recipients.Count == 0)
            {
                failures.Add(Fmt(
                    "SiemSettings.AlertEmail.Recipients",
                    "must contain at least one recipient when AlertEmail.Enabled is true",
                    "[]"));
            }
            else
            {
                foreach (var r in alert.Recipients)
                {
                    if (string.IsNullOrWhiteSpace(r) || !r.Contains('@'))
                    {
                        failures.Add(Fmt(
                            "SiemSettings.AlertEmail.Recipients",
                            "each entry must be a valid email address (must contain '@')",
                            r ?? ""));
                        break;
                    }
                }
            }

            if (alert.AlertOnEvents is not null)
            {
                foreach (var e in alert.AlertOnEvents)
                {
                    if (!Enum.TryParse<SiemEventType>(e, ignoreCase: false, out _))
                    {
                        failures.Add(Fmt(
                            "SiemSettings.AlertEmail.AlertOnEvents",
                            "each entry must be a valid SiemEventType name " +
                            "(PasswordChanged, InvalidCredentials, UserNotFound, PortalLockout, " +
                            "ApproachingLockout, RateLimitExceeded, RecaptchaFailed, " +
                            "ChangeNotPermitted, ValidationFailed, Generic)",
                            e ?? ""));
                        break;
                    }
                }
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
