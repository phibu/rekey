using Microsoft.Extensions.Hosting.WindowsServices;

namespace PassReset.Web.Services.Hosting;

/// <summary>
/// Detects the active hosting mode at startup. Used for observability (Serilog
/// <c>Information</c> log) only — does not change runtime behavior.
/// Constructor seams make the detection logic unit-testable without depending on
/// the actual Windows environment.
/// </summary>
public sealed class HostingModeDetector
{
    private readonly Func<bool> _isWindowsService;
    private readonly Func<string, string?> _getEnv;

    public HostingModeDetector()
        : this(
            isWindowsService: WindowsServiceHelpers.IsWindowsService,
            getEnv: Environment.GetEnvironmentVariable)
    {
    }

    internal HostingModeDetector(Func<bool> isWindowsService, Func<string, string?> getEnv)
    {
        _isWindowsService = isWindowsService;
        _getEnv = getEnv;
    }

    public HostingMode Detect()
    {
        // Service check first — it's definitive when true.
        if (_isWindowsService()) return HostingMode.Service;

        // IIS sets ASPNETCORE_IIS_HTTPAUTH to a non-empty string (e.g. "windows;anonymous;").
        var iis = _getEnv("ASPNETCORE_IIS_HTTPAUTH");
        if (!string.IsNullOrEmpty(iis)) return HostingMode.Iis;

        return HostingMode.Console;
    }
}
