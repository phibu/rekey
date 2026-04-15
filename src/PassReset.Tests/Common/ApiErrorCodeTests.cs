using PassReset.Common;

namespace PassReset.Tests.Common;

/// <summary>
/// Pin the numeric values of <see cref="ApiErrorCode"/> entries. The enum is serialised
/// over the wire as an integer, so changing the value of an existing member would be a
/// breaking change for any client that caches settings or has legacy UI mappings.
/// Specifically pins <see cref="ApiErrorCode.PasswordTooRecentlyChanged"/> — added in BUG-002.
/// </summary>
public class ApiErrorCodeTests
{
    [Theory]
    [InlineData(ApiErrorCode.Generic, 0)]
    [InlineData(ApiErrorCode.FieldRequired, 1)]
    [InlineData(ApiErrorCode.FieldMismatch, 2)]
    [InlineData(ApiErrorCode.UserNotFound, 3)]
    [InlineData(ApiErrorCode.InvalidCredentials, 4)]
    [InlineData(ApiErrorCode.InvalidCaptcha, 5)]
    [InlineData(ApiErrorCode.ChangeNotPermitted, 6)]
    [InlineData(ApiErrorCode.InvalidDomain, 7)]
    [InlineData(ApiErrorCode.LdapProblem, 8)]
    [InlineData(ApiErrorCode.ComplexPassword, 9)]
    [InlineData(ApiErrorCode.MinimumScore, 10)]
    [InlineData(ApiErrorCode.MinimumDistance, 11)]
    [InlineData(ApiErrorCode.PwnedPassword, 12)]
    [InlineData(ApiErrorCode.PasswordTooYoung, 13)]
    [InlineData(ApiErrorCode.AccountDisabled, 14)]
    [InlineData(ApiErrorCode.RateLimitExceeded, 15)]
    [InlineData(ApiErrorCode.PwnedPasswordCheckFailed, 16)]
    [InlineData(ApiErrorCode.PortalLockout, 17)]
    [InlineData(ApiErrorCode.ApproachingLockout, 18)]
    [InlineData(ApiErrorCode.PasswordTooRecentlyChanged, 19)]
    public void ErrorCode_HasStableNumericValue(ApiErrorCode code, int expectedValue)
    {
        Assert.Equal(expectedValue, (int)code);
    }

    [Fact]
    public void PasswordTooRecentlyChanged_IsDefined()
    {
        // BUG-002 regression guard: the enum MUST contain this member.
        Assert.True(Enum.IsDefined(typeof(ApiErrorCode), ApiErrorCode.PasswordTooRecentlyChanged));
        Assert.Equal("PasswordTooRecentlyChanged", ApiErrorCode.PasswordTooRecentlyChanged.ToString());
    }

    [Fact]
    public void EnumMemberCount_LocksInKnownSurface()
    {
        // If you add an entry to ApiErrorCode, update this test plus the TypeScript mirror in
        // ClientApp/src/types/settings.ts. Keeping a count check catches accidental deletions.
        var values = Enum.GetValues<ApiErrorCode>();
        Assert.Equal(20, values.Length);
    }

    [Fact]
    public void ApiErrorItem_CarriesErrorCodeAndMessage()
    {
        var item = new ApiErrorItem(ApiErrorCode.PasswordTooRecentlyChanged, "wait");
        Assert.Equal(ApiErrorCode.PasswordTooRecentlyChanged, item.ErrorCode);
        Assert.Equal("wait", item.Message);
    }
}
