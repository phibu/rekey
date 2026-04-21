using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PassReset.Common;
using PassReset.PasswordProvider;

namespace PassReset.Tests.Windows.PasswordProvider;

/// <summary>
/// Verifies the per-username portal lockout decorator: threshold upgrade to
/// <see cref="ApiErrorCode.ApproachingLockout"/>, escalation to
/// <see cref="ApiErrorCode.PortalLockout"/>, and counter reset on success.
/// </summary>
public class LockoutPasswordChangeProviderTests
{
    private const string User = "alice";
    private const string GoodPassword = "correct";
    private const string BadPassword = "wrong";

    private static (LockoutPasswordChangeProvider provider, StubInner inner) Build(
        int threshold = 3, TimeSpan? window = null)
    {
        var inner = new StubInner();
        var options = Options.Create(new PasswordChangeOptions
        {
            PortalLockoutThreshold = threshold,
            PortalLockoutWindow    = window ?? TimeSpan.FromMinutes(30),
        });
        var logger = Substitute.For<ILogger<LockoutPasswordChangeProvider>>();
        var provider = new LockoutPasswordChangeProvider(inner, options, logger);
        return (provider, inner);
    }

    [Fact]
    public async Task BelowThreshold_BadCredentialsFlowThroughAsInvalidCredentials()
    {
        var (provider, _) = Build(threshold: 3);

        var r1 = await provider.PerformPasswordChangeAsync(User, BadPassword, "new");
        var r2 = await provider.PerformPasswordChangeAsync(User, BadPassword, "new");

        Assert.Equal(ApiErrorCode.InvalidCredentials, r1?.ErrorCode);
        Assert.Equal(ApiErrorCode.InvalidCredentials, r2?.ErrorCode);
    }

    [Fact]
    public async Task AtThreshold_UpgradesToApproachingLockout()
    {
        var (provider, _) = Build(threshold: 3);

        await provider.PerformPasswordChangeAsync(User, BadPassword, "new");
        await provider.PerformPasswordChangeAsync(User, BadPassword, "new");
        var atThreshold = await provider.PerformPasswordChangeAsync(User, BadPassword, "new");

        Assert.Equal(ApiErrorCode.ApproachingLockout, atThreshold?.ErrorCode);
    }

    [Fact]
    public async Task AboveThreshold_ReturnsPortalLockoutWithoutCallingInner()
    {
        var (provider, inner) = Build(threshold: 3);

        for (var i = 0; i < 3; i++)
            await provider.PerformPasswordChangeAsync(User, BadPassword, "new");

        inner.CallCount = 0;
        var result = await provider.PerformPasswordChangeAsync(User, BadPassword, "new");

        Assert.Equal(ApiErrorCode.PortalLockout, result?.ErrorCode);
        Assert.Equal(0, inner.CallCount);
    }

    [Fact]
    public async Task Success_ResetsCounter()
    {
        var (provider, _) = Build(threshold: 3);

        await provider.PerformPasswordChangeAsync(User, BadPassword, "new");
        await provider.PerformPasswordChangeAsync(User, BadPassword, "new");

        var success = await provider.PerformPasswordChangeAsync(User, GoodPassword, "new");
        Assert.Null(success);

        // Two more failures should NOT reach threshold because counter was reset.
        var r1 = await provider.PerformPasswordChangeAsync(User, BadPassword, "new");
        var r2 = await provider.PerformPasswordChangeAsync(User, BadPassword, "new");
        Assert.Equal(ApiErrorCode.InvalidCredentials, r1?.ErrorCode);
        Assert.Equal(ApiErrorCode.InvalidCredentials, r2?.ErrorCode);
    }

    [Fact]
    public async Task ThresholdZero_DisablesLockoutCompletely()
    {
        var (provider, _) = Build(threshold: 0);

        for (var i = 0; i < 10; i++)
        {
            var r = await provider.PerformPasswordChangeAsync(User, BadPassword, "new");
            Assert.Equal(ApiErrorCode.InvalidCredentials, r?.ErrorCode);
        }
    }

    [Fact]
    public async Task PerUsername_CountersAreIndependent()
    {
        var (provider, _) = Build(threshold: 3);

        await provider.PerformPasswordChangeAsync("alice", BadPassword, "new");
        await provider.PerformPasswordChangeAsync("alice", BadPassword, "new");
        await provider.PerformPasswordChangeAsync("alice", BadPassword, "new"); // hits threshold

        var bobFirst = await provider.PerformPasswordChangeAsync("bob", BadPassword, "new");
        Assert.Equal(ApiErrorCode.InvalidCredentials, bobFirst?.ErrorCode);
    }

    [Fact]
    public async Task UsernameNormalisation_KeysByBareSam()
    {
        var (provider, _) = Build(threshold: 3);

        // CORP\alice, alice@corp.com, and alice must all map to the same counter.
        await provider.PerformPasswordChangeAsync(@"CORP\alice", BadPassword, "new");
        await provider.PerformPasswordChangeAsync("alice@corp.com", BadPassword, "new");
        var atThreshold = await provider.PerformPasswordChangeAsync("ALICE", BadPassword, "new");

        Assert.Equal(ApiErrorCode.ApproachingLockout, atThreshold?.ErrorCode);
    }

    [Fact]
    public void ActiveEntries_StartsAtZero()
    {
        var (provider, _) = Build();
        Assert.Equal(0, provider.ActiveEntries);
    }

    [Fact]
    public async Task ActiveEntries_ReflectsFailures()
    {
        var (provider, _) = Build(threshold: 3);
        await provider.PerformPasswordChangeAsync(User, BadPassword, "new");
        Assert.Equal(1, provider.ActiveEntries);
    }

    // ─── Stub inner provider ──────────────────────────────────────────────────

    /// <summary>
    /// Accepts "correct" as the current password; everything else returns InvalidCredentials.
    /// </summary>
    private sealed class StubInner : IPasswordChangeProvider
    {
        public int CallCount;

        public Task<ApiErrorItem?> PerformPasswordChangeAsync(string username, string currentPassword, string newPassword)
        {
            CallCount++;
            ApiErrorItem? result = currentPassword == GoodPassword
                ? null
                : new ApiErrorItem(ApiErrorCode.InvalidCredentials);
            return Task.FromResult(result);
        }

        public string? GetUserEmail(string username) => null;
        public IEnumerable<(string Username, string Email, DateTime? PasswordLastSet)> GetUsersInGroup(string groupName) => [];
        public TimeSpan GetDomainMaxPasswordAge() => TimeSpan.MaxValue;
        public Task<PasswordPolicy?> GetEffectivePasswordPolicyAsync() =>
            Task.FromResult<PasswordPolicy?>(null);
    }
}
