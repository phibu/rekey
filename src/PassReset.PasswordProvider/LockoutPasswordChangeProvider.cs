using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PassReset.Common;

namespace PassReset.PasswordProvider;

/// <summary>
/// Exposes lockout diagnostics for health monitoring.
/// Only the active entry count is exposed — no usernames or per-entry details.
/// </summary>
public interface ILockoutDiagnostics
{
    /// <summary>Number of active (non-expired) lockout entries.</summary>
    int ActiveEntries { get; }
}

/// <summary>
/// Decorator around <see cref="IPasswordChangeProvider"/> that tracks per-username
/// credential failure counts and blocks requests before they reach Active Directory
/// once the portal lockout threshold is reached.
///
/// This prevents both accidental self-lockout (AD lockout from repeated typos) and
/// targeted lockout attacks (an attacker burning the AD bad-password quota for a victim).
/// The counter is keyed on the normalised username (SAM part only, lowercase), so it is
/// effective regardless of how the caller formats the input or which IP they use.
///
/// Threshold semantics (example: threshold = 3):
///   failures 1–2  → request is passed through to the inner provider
///   failure  3    → inner provider is called; on InvalidCredentials the response is
///                   upgraded to <see cref="ApiErrorCode.ApproachingLockout"/> to signal
///                   the UI to show a warning banner ("next attempt will lock you out")
///   failure  4+   → <see cref="ApiErrorCode.PortalLockout"/> is returned immediately
///                   without contacting AD
///
/// The lockout window is <em>absolute</em>: the expiry is fixed at the time of the first
/// failure and is never reset by subsequent failures within the window. This prevents an
/// attacker from keeping the counter perpetually alive by spacing attempts just under the
/// window boundary.
///
/// Set <see cref="PasswordChangeOptions.PortalLockoutThreshold"/> to 0 to disable.
///
/// <b>IIS deployment note:</b> Lockout state is held in-process memory and is lost on
/// application pool recycle. The IIS app pool <c>MaxProcesses</c> must remain at 1
/// (the default) to ensure consistent lockout enforcement. If multiple worker processes
/// are used, each maintains an independent counter, effectively multiplying the lockout
/// threshold. A periodic timer evicts expired entries to prevent unbounded memory growth.
/// </summary>
public sealed class LockoutPasswordChangeProvider : IPasswordChangeProvider, ILockoutDiagnostics, IDisposable
{
    private const string CacheKeyPrefix = "portal_lockout:";

    // Thread-safe counter store: key → (failure count, absolute expiry).
    // Using ConcurrentDictionary + AddOrUpdate guarantees atomic increment without locks.
    private readonly ConcurrentDictionary<string, (int Count, DateTimeOffset Expiry)> _counters
        = new(StringComparer.Ordinal);

    private readonly IPasswordChangeProvider _inner;
    private readonly PasswordChangeOptions _options;
    private readonly ILogger<LockoutPasswordChangeProvider> _logger;
    private readonly Timer _cleanupTimer;

    public LockoutPasswordChangeProvider(
        IPasswordChangeProvider inner,
        IOptions<PasswordChangeOptions> options,
        ILogger<LockoutPasswordChangeProvider> logger)
    {
        _inner   = inner;
        _options = options.Value;
        _logger  = logger;

        // Sweep expired entries every 5 minutes to prevent unbounded dictionary growth.
        _cleanupTimer = new Timer(_ => EvictExpiredEntries(), null,
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public void Dispose() => _cleanupTimer.Dispose();

    /// <inheritdoc />
    public int ActiveEntries => _counters.Count(kvp => DateTimeOffset.UtcNow < kvp.Value.Expiry);

    /// <inheritdoc />
    public async Task<ApiErrorItem?> PerformPasswordChangeAsync(string username, string currentPassword, string newPassword)
    {
        var threshold = _options.PortalLockoutThreshold;

        if (threshold > 0)
        {
            var key = BuildCacheKey(username);
            var now = DateTimeOffset.UtcNow;

            if (_counters.TryGetValue(key, out var entry)
                && now < entry.Expiry
                && entry.Count >= threshold)
            {
                _logger.LogWarning(
                    "Portal lockout active for {Username} — {Count}/{Threshold} failures in window, AD not contacted",
                    username, entry.Count, threshold);

                return new ApiErrorItem(ApiErrorCode.PortalLockout);
            }
        }

        var result = await _inner.PerformPasswordChangeAsync(username, currentPassword, newPassword);

        if (threshold > 0)
        {
            var key = BuildCacheKey(username);
            var now = DateTimeOffset.UtcNow;

            if (result?.ErrorCode == ApiErrorCode.InvalidCredentials)
            {
                var newCount = IncrementCounter(key, now);

                _logger.LogWarning(
                    "Portal failure counter for {Username}: {Count}/{Threshold}",
                    username, newCount, threshold);

                // Warn on the attempt that hits the threshold exactly: the *next* attempt
                // will be blocked. This ensures "one more attempt will lock you out" is accurate.
                if (newCount == threshold)
                    return new ApiErrorItem(ApiErrorCode.ApproachingLockout);
            }
            else if (result is null)
            {
                // Successful password change — reset the counter immediately.
                _counters.TryRemove(key, out _);
            }
        }

        return result;
    }

    /// <inheritdoc />
    public string? GetUserEmail(string username) => _inner.GetUserEmail(username);

    /// <inheritdoc />
    public IEnumerable<(string Username, string Email, DateTime? PasswordLastSet)>
        GetUsersInGroup(string groupName) => _inner.GetUsersInGroup(groupName);

    /// <inheritdoc />
    public TimeSpan GetDomainMaxPasswordAge() => _inner.GetDomainMaxPasswordAge();

    /// <inheritdoc />
    public Task<PasswordPolicy?> GetEffectivePasswordPolicyAsync() =>
        _inner.GetEffectivePasswordPolicyAsync();

    // ─── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Atomically increments the failure counter for <paramref name="key"/>.
    /// The expiry is set once on the <em>first</em> failure and never extended,
    /// making the lockout window absolute rather than sliding.
    /// If the existing entry has already expired it is treated as a fresh start.
    /// </summary>
    private int IncrementCounter(string key, DateTimeOffset now)
    {
        var window = _options.PortalLockoutWindow;

        var (newCount, _) = _counters.AddOrUpdate(
            key,
            addValueFactory:    _ => (1, now + window),
            updateValueFactory: (_, existing) =>
                now >= existing.Expiry
                    ? (1, now + window)                              // window expired — start fresh
                    : (existing.Count + 1, existing.Expiry));        // keep original expiry (absolute window)

        return newCount;
    }

    /// <summary>
    /// Normalises the username to a SAM-like key for consistent cache lookups
    /// regardless of how the caller formatted the input
    /// (bare: <c>jdoe</c>, UPN: <c>jdoe@corp.com</c>, NetBIOS: <c>CORP\jdoe</c>).
    /// Must stay in sync with <c>FindBySamAccountName</c> in <c>PasswordChangeProvider</c>.
    /// </summary>
    internal static string BuildCacheKey(string username)
    {
        var normalised = username.Trim().ToLowerInvariant();
        normalised = normalised.Contains('\\') ? normalised[(normalised.IndexOf('\\') + 1)..] :
                     normalised.Contains('@')  ? normalised[..normalised.IndexOf('@')]          :
                     normalised;
        return CacheKeyPrefix + normalised;
    }

    private void EvictExpiredEntries()
    {
        var now = DateTimeOffset.UtcNow;
        var evicted = 0;

        // Phase 1: Remove expired entries
        foreach (var kvp in _counters)
        {
            if (now >= kvp.Value.Expiry && _counters.TryRemove(kvp.Key, out _))
                evicted++;
        }

        // Phase 2: Safety cap — evict oldest 25% if dictionary is over-large
        const int MaxEntries = 10_000;
        if (_counters.Count > MaxEntries)
        {
            _logger.LogWarning(
                "Lockout dictionary has {Count} entries (cap={Cap}) — evicting oldest 25%",
                _counters.Count, MaxEntries);

            var toEvict = _counters
                .OrderBy(kvp => kvp.Value.Expiry)
                .Take(_counters.Count / 4)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in toEvict)
                _counters.TryRemove(key, out _);

            evicted += toEvict.Count;
        }

        if (evicted > 0)
            _logger.LogDebug("Evicted {Count} lockout entries", evicted);
    }
}
