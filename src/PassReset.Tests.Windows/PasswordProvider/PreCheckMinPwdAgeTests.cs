using PassReset.Common;
using PassReset.PasswordProvider;

namespace PassReset.Tests.Windows.PasswordProvider;

/// <summary>
/// STAB-004 pre-check coverage. Exercises the pure
/// <see cref="PasswordChangeProvider.EvaluateMinPwdAge"/> helper so the
/// consecutive-change policy does not depend on live AD state.
/// </summary>
public class PreCheckMinPwdAgeTests
{
    private static readonly DateTime Now = new(2026, 04, 16, 12, 00, 00, DateTimeKind.Utc);

    [Fact]
    public void UserWithinMinAge_ReturnsPasswordTooRecentlyChanged()
    {
        var lastSet = Now.AddMinutes(-1);
        var minAge = TimeSpan.FromMinutes(5);

        var result = PasswordChangeProvider.EvaluateMinPwdAge(lastSet, minAge, Now);

        Assert.NotNull(result);
        Assert.Equal(ApiErrorCode.PasswordTooRecentlyChanged, result!.ErrorCode);
        Assert.Contains("minute(s) remaining", result.Message);
    }

    [Fact]
    public void UserOutsideMinAge_ReturnsNull()
    {
        var lastSet = Now.AddMinutes(-10);
        var minAge = TimeSpan.FromMinutes(5);

        var result = PasswordChangeProvider.EvaluateMinPwdAge(lastSet, minAge, Now);

        Assert.Null(result);
    }

    [Fact]
    public void ExactlyAtBoundary_ReturnsNull()
    {
        var lastSet = Now.AddMinutes(-5);
        var minAge = TimeSpan.FromMinutes(5);

        var result = PasswordChangeProvider.EvaluateMinPwdAge(lastSet, minAge, Now);

        Assert.Null(result);
    }

    [Fact]
    public void Message_QuotesElapsedPolicyAndRemainingMinutes()
    {
        var lastSet = Now.AddMinutes(-2);
        var minAge = TimeSpan.FromMinutes(10);

        var result = PasswordChangeProvider.EvaluateMinPwdAge(lastSet, minAge, Now);

        Assert.NotNull(result);
        Assert.Contains("2 minute(s) ago", result!.Message);
        Assert.Contains("10 minute(s)", result.Message);
        Assert.Contains("8 minute(s) remaining", result.Message);
    }

    [Fact]
    public void RoundingFloorsElapsedAndCeilsRemaining()
    {
        // Elapsed 30s, min 5min → elapsed floors to 0, remaining ceils to 5.
        var lastSet = Now.AddSeconds(-30);
        var minAge = TimeSpan.FromMinutes(5);

        var result = PasswordChangeProvider.EvaluateMinPwdAge(lastSet, minAge, Now);

        Assert.NotNull(result);
        Assert.Contains("0 minute(s) ago", result!.Message);
        Assert.Contains("5 minute(s) remaining", result.Message);
    }

    [Fact]
    public void RemainingMinutesClampedToAtLeastOne()
    {
        // 299 seconds elapsed, 5-minute minAge → remaining = 1s → ceil to 1.
        var lastSet = Now.AddSeconds(-299);
        var minAge = TimeSpan.FromMinutes(5);

        var result = PasswordChangeProvider.EvaluateMinPwdAge(lastSet, minAge, Now);

        Assert.NotNull(result);
        Assert.Contains("1 minute(s) remaining", result!.Message);
    }
}
