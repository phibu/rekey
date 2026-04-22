using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;
using PassReset.Web.Configuration;
using PassReset.Web.Services.Hosting;

namespace PassReset.Web.Models;

/// <summary>
/// Validates <see cref="KestrelHttpsCertOptions"/> at startup.
/// In Service mode, exactly one of Thumbprint or PfxPath must be set, StoreLocation must be valid,
/// and CurrentUser store is forbidden (non-portable for service identity).
/// IIS and Console modes accept any configuration (both are ignored at runtime).
/// </summary>
internal sealed class KestrelHttpsCertOptionsValidator : IValidateOptions<KestrelHttpsCertOptions>
{
    private readonly Func<HostingMode> _getHostingMode;

    public KestrelHttpsCertOptionsValidator(Func<HostingMode> getHostingMode)
    {
        _getHostingMode = getHostingMode;
    }

    public ValidateOptionsResult Validate(string? name, KestrelHttpsCertOptions options)
    {
        var mode = _getHostingMode();

        // IIS and Console modes don't use these settings; skip validation.
        if (mode is HostingMode.Iis or HostingMode.Console)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        var hasThumbprint = !string.IsNullOrWhiteSpace(options.Thumbprint);
        var hasPfxPath = !string.IsNullOrWhiteSpace(options.PfxPath);

        // Exactly one of Thumbprint or PfxPath must be set.
        if (hasThumbprint && hasPfxPath)
        {
            failures.Add($"{nameof(KestrelHttpsCertOptions)}.{nameof(KestrelHttpsCertOptions.Thumbprint)} and {nameof(KestrelHttpsCertOptions.PfxPath)} are mutually exclusive; only one may be set.");
        }
        else if (!hasThumbprint && !hasPfxPath)
        {
            failures.Add($"{nameof(KestrelHttpsCertOptions)}: Either {nameof(KestrelHttpsCertOptions.Thumbprint)} or {nameof(KestrelHttpsCertOptions.PfxPath)} must be set in Service mode.");
        }

        // Validate StoreLocation enum value (only checked if Thumbprint is used).
        if (hasThumbprint)
        {
            if (!Enum.TryParse<StoreLocation>(options.StoreLocation, ignoreCase: true, out _))
            {
                failures.Add($"{nameof(KestrelHttpsCertOptions)}.{nameof(KestrelHttpsCertOptions.StoreLocation)} is invalid; must be a valid StoreLocation enum value. Got: '{options.StoreLocation}'.");
            }
            else if (options.StoreLocation.Equals("CurrentUser", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"{nameof(KestrelHttpsCertOptions)}.{nameof(KestrelHttpsCertOptions.StoreLocation)} cannot be 'CurrentUser' in Service mode; use 'LocalMachine' for portable certificates.");
            }
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
