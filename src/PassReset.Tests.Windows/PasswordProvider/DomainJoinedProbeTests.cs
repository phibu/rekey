using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PassReset.Common;
using PassReset.PasswordProvider;

namespace PassReset.Tests.Windows.PasswordProvider;

public class DomainJoinedProbeTests
{
    [Fact]
    public async Task CheckAsync_UseAutomaticContextFalse_ReturnsNotConfigured()
    {
        // DomainJoinedProbe is only meaningful when UseAutomaticContext is true.
        // When false, the probe short-circuits to NotConfigured — the LDAP probe
        // should be used instead.
        var opts = new PasswordChangeOptions { UseAutomaticContext = false };
        var probe = new DomainJoinedProbe(
            Options.Create(opts),
            NullLogger<DomainJoinedProbe>.Instance);

        var result = await probe.CheckAsync();

        Assert.Equal(AdProbeStatus.NotConfigured, result.Status);
    }

    [Fact]
    public async Task CheckAsync_UseAutomaticContextTrue_NonDomainJoinedMachine_ReturnsUnhealthy()
    {
        // CI runners aren't domain-joined. Expect Unhealthy, not a thrown exception.
        var opts = new PasswordChangeOptions { UseAutomaticContext = true };
        var probe = new DomainJoinedProbe(
            Options.Create(opts),
            NullLogger<DomainJoinedProbe>.Instance);

        var result = await probe.CheckAsync();

        Assert.Equal(AdProbeStatus.Unhealthy, result.Status);
    }
}
