using Microsoft.Extensions.Options;
using PassReset.Web.Configuration;

namespace PassReset.Web.Models;

/// <summary>Validates <see cref="AdminSettings"/> at startup. Fail-fast.</summary>
// Internal: scoped to the Web assembly; DI registration stays in Program.cs and
// no cross-project consumers are planned. SmtpSettingsValidator is public for
// historical reasons — new validators default to internal unless needed elsewhere.
internal sealed class AdminSettingsValidator : IValidateOptions<AdminSettings>
{
    public ValidateOptionsResult Validate(string? name, AdminSettings options)
    {
        if (!options.Enabled) return ValidateOptionsResult.Success;

        var failures = new List<string>();

        if (options.LoopbackPort < 1024 || options.LoopbackPort > 65535)
        {
            failures.Add($"{nameof(AdminSettings)}.{nameof(AdminSettings.LoopbackPort)} must be between 1024 and 65535 (inclusive); got {options.LoopbackPort}.");
        }

        if (!string.IsNullOrWhiteSpace(options.KeyStorePath) && !Path.IsPathRooted(options.KeyStorePath))
        {
            failures.Add($"{nameof(AdminSettings)}.{nameof(AdminSettings.KeyStorePath)} must be an absolute path; got '{options.KeyStorePath}'.");
        }

        if (!string.IsNullOrWhiteSpace(options.AppSettingsFilePath) && !Path.IsPathRooted(options.AppSettingsFilePath))
        {
            failures.Add($"{nameof(AdminSettings)}.{nameof(AdminSettings.AppSettingsFilePath)} must be an absolute path; got '{options.AppSettingsFilePath}'.");
        }

        if (!string.IsNullOrWhiteSpace(options.SecretsFilePath) && !Path.IsPathRooted(options.SecretsFilePath))
        {
            failures.Add($"{nameof(AdminSettings)}.{nameof(AdminSettings.SecretsFilePath)} must be an absolute path; got '{options.SecretsFilePath}'.");
        }

        if (!OperatingSystem.IsWindows() && string.IsNullOrWhiteSpace(options.DataProtectionCertThumbprint))
        {
            failures.Add($"{nameof(AdminSettings)}.{nameof(AdminSettings.DataProtectionCertThumbprint)} is required on non-Windows platforms when {nameof(AdminSettings.Enabled)} is true.");
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
