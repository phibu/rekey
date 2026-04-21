using Microsoft.Extensions.Options;
using PassReset.Common;
using PassReset.Common.LocalPolicy;

namespace PassReset.PasswordProvider;

/// <summary>
/// Validates <see cref="PasswordChangeOptions"/> at application startup so that
/// mis-configuration is caught immediately with a clear error rather than producing
/// a cryptic LDAP socket error at runtime.
/// </summary>
public sealed class PasswordChangeOptionsValidator : IValidateOptions<PasswordChangeOptions>
{
    private static string Fmt(string path, string reason, string actual)
        => $"{path}: {reason} (got \"{actual}\"). Edit appsettings.Production.json or run Install-PassReset.ps1 -Reconfigure.";

    public ValidateOptionsResult Validate(string? name, PasswordChangeOptions options)
    {
        var failures = new List<string>();

        if (!options.UseAutomaticContext)
        {
            // Manual context requires at least one resolvable hostname.
            if (options.LdapHostnames.Length == 0
                || options.LdapHostnames.All(h => string.IsNullOrWhiteSpace(h)))
            {
                failures.Add(Fmt(
                    "PasswordChangeOptions.LdapHostnames",
                    "must contain at least one non-empty hostname when UseAutomaticContext is false",
                    "[]"));
            }

            if (options.LdapPort <= 0 || options.LdapPort > 65535)
            {
                failures.Add(Fmt(
                    "PasswordChangeOptions.LdapPort",
                    "is not a valid port number (use 636 for LDAPS, 389 for plain LDAP)",
                    options.LdapPort.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            // ProviderMode cross-platform sanity checks (Phase 11).
            var isWindows = OperatingSystem.IsWindows();

            if (options.ProviderMode == ProviderMode.Windows && !isWindows)
            {
                failures.Add($"PasswordChangeOptions.ProviderMode=Windows requires a Windows host; current platform is {System.Runtime.InteropServices.RuntimeInformation.OSDescription}. Set ProviderMode to Ldap or Auto.");
            }

            var resolvesToLdap =
                options.ProviderMode == ProviderMode.Ldap ||
                (options.ProviderMode == ProviderMode.Auto && !isWindows);

            if (resolvesToLdap)
            {
                if (string.IsNullOrWhiteSpace(options.ServiceAccountDn))
                    failures.Add("PasswordChangeOptions.ServiceAccountDn is required when ProviderMode resolves to Ldap.");
                if (string.IsNullOrWhiteSpace(options.ServiceAccountPassword))
                    failures.Add("PasswordChangeOptions.ServiceAccountPassword is required when ProviderMode resolves to Ldap. Bind via PasswordChangeOptions__ServiceAccountPassword env var.");
                if (string.IsNullOrWhiteSpace(options.BaseDn))
                    failures.Add("PasswordChangeOptions.BaseDn is required when ProviderMode resolves to Ldap.");
                if (options.LdapHostnames is null || options.LdapHostnames.Length == 0)
                    failures.Add("PasswordChangeOptions.LdapHostnames must contain at least one hostname when ProviderMode resolves to Ldap.");
            }
        }

        // LocalPolicy validation (always checked regardless of UseAutomaticContext).
        ValidateLocalPolicy(options.LocalPolicy, failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateLocalPolicy(LocalPolicyOptions localPolicy, List<string> failures)
    {
        if (localPolicy.MinBannedTermLength < 1)
            failures.Add("PasswordChangeOptions.LocalPolicy.MinBannedTermLength must be >= 1.");

        if (!string.IsNullOrEmpty(localPolicy.BannedWordsPath) && !File.Exists(localPolicy.BannedWordsPath))
            failures.Add($"PasswordChangeOptions.LocalPolicy.BannedWordsPath: file not found (got \"{localPolicy.BannedWordsPath}\").");

        if (!string.IsNullOrEmpty(localPolicy.LocalPwnedPasswordsPath))
        {
            if (!Directory.Exists(localPolicy.LocalPwnedPasswordsPath))
            {
                failures.Add($"PasswordChangeOptions.LocalPolicy.LocalPwnedPasswordsPath: directory not found (got \"{localPolicy.LocalPwnedPasswordsPath}\").");
            }
            else
            {
                // Must contain at least one HIBP prefix file: basename = exactly 5 hex chars.
                var hasPrefix = Directory.EnumerateFiles(localPolicy.LocalPwnedPasswordsPath, "*.txt")
                    .Any(f =>
                    {
                        var stem = Path.GetFileNameWithoutExtension(f);
                        if (stem.Length != 5) return false;
                        foreach (var c in stem)
                            if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f')))
                                return false;
                        return true;
                    });

                if (!hasPrefix)
                    failures.Add($"PasswordChangeOptions.LocalPolicy.LocalPwnedPasswordsPath contains no HIBP prefix files (expected *.txt files with 5-hex-char names).");
            }
        }
    }
}
