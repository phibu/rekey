namespace PassReset.Common;

/// <summary>
/// Narrow "is the directory reachable?" probe used by health endpoints. One
/// implementation per <see cref="ProviderMode"/>: <c>DomainJoinedProbe</c> for
/// Windows (PrincipalContext domain-join check) and <c>LdapTcpProbe</c> for
/// cross-platform deployments (TCP connect on configured LDAP hosts).
/// Implementations must not throw — failures are returned as <see cref="AdProbeStatus.Unhealthy"/>.
/// </summary>
public interface IAdConnectivityProbe
{
    Task<AdProbeResult> CheckAsync(CancellationToken cancellationToken = default);
}

public enum AdProbeStatus
{
    Healthy,
    Unhealthy,
    // "No AD configured" — e.g. debug-provider scenarios where LdapHostnames is empty.
    // HealthController treats this as healthy but surfaces it distinctly so operators can tell.
    NotConfigured,
}

public readonly record struct AdProbeResult(AdProbeStatus Status, long LatencyMs);
