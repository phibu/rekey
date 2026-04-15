using Microsoft.Extensions.Caching.Memory;
using PassReset.Common;

namespace PassReset.PasswordProvider;

/// <summary>
/// In-memory TTL cache around <see cref="IPasswordChangeProvider.GetEffectivePasswordPolicyAsync"/>.
/// Successful policy fetches are cached for 1 hour; failures (null) for 60 seconds so we
/// retry promptly after a transient AD outage without hammering the DC on every page load.
/// </summary>
public sealed class PasswordPolicyCache
{
    private const string CacheKey = "ad-password-policy";
    private static readonly TimeSpan SuccessTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan FailureTtl = TimeSpan.FromSeconds(60);

    private readonly IMemoryCache _cache;
    private readonly IPasswordChangeProvider _provider;

    public PasswordPolicyCache(IMemoryCache cache, IPasswordChangeProvider provider)
    {
        _cache = cache;
        _provider = provider;
    }

    public async Task<PasswordPolicy?> GetOrFetchAsync()
    {
        if (_cache.TryGetValue(CacheKey, out PasswordPolicy? cached))
            return cached;

        var policy = await _provider.GetEffectivePasswordPolicyAsync();
        _cache.Set(CacheKey, policy, policy is null ? FailureTtl : SuccessTtl);
        return policy;
    }
}
