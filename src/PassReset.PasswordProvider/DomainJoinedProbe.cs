using System.Diagnostics;
using System.DirectoryServices.AccountManagement;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PassReset.Common;

namespace PassReset.PasswordProvider;

/// <summary>
/// Windows-only AD connectivity probe. Opens a <see cref="PrincipalContext"/>
/// in <see cref="ContextType.Domain"/> mode and verifies <c>ConnectedServer</c>
/// is non-null. Requires a domain-joined Windows host.
/// Returns <see cref="AdProbeStatus.NotConfigured"/> when
/// <see cref="PasswordChangeOptions.UseAutomaticContext"/> is false — the LDAP
/// probe should be wired in that case.
/// </summary>
public sealed class DomainJoinedProbe : IAdConnectivityProbe
{
    private readonly IOptions<PasswordChangeOptions> _options;
    private readonly ILogger<DomainJoinedProbe> _logger;

    public DomainJoinedProbe(
        IOptions<PasswordChangeOptions> options,
        ILogger<DomainJoinedProbe> logger)
    {
        _options = options;
        _logger  = logger;
    }

    public Task<AdProbeResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var opts = _options.Value;
        var sw   = Stopwatch.StartNew();

        if (!opts.UseAutomaticContext)
            return Task.FromResult(new AdProbeResult(AdProbeStatus.NotConfigured, sw.ElapsedMilliseconds));

        try
        {
            using var ctx = new PrincipalContext(ContextType.Domain);
            var status = ctx.ConnectedServer != null
                ? AdProbeStatus.Healthy
                : AdProbeStatus.Unhealthy;
            return Task.FromResult(new AdProbeResult(status, sw.ElapsedMilliseconds));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AD connectivity probe failed (automatic context)");
            return Task.FromResult(new AdProbeResult(AdProbeStatus.Unhealthy, sw.ElapsedMilliseconds));
        }
    }
}
