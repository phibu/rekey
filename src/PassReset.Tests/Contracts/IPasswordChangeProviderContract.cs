using PassReset.Common;
using Xunit;

namespace PassReset.Tests.Contracts;

/// <summary>
/// Shared behavioral contract every <see cref="IPasswordChangeProvider"/> implementation
/// must satisfy. Enforces parity between the Windows-native provider and the
/// cross-platform LDAP provider. Subclasses supply a <see cref="CreateProvider"/> +
/// <see cref="SeedUser"/> fixture that is constructed fresh for each test.
/// </summary>
/// <remarks>
/// Additional scenarios intentionally NOT enforced here — they are covered by each
/// provider's implementation-specific test suite because the fixture cost of encoding
/// them in the contract (AD group membership synthesis, disabled-account flags, pwdLastSet
/// arithmetic) outweighs the parity value:
///   RestrictedGroup -> ChangeNotPermitted         (see LdapPasswordChangeProviderTests)
///   AllowedGroup miss -> ChangeNotPermitted       (see LdapPasswordChangeProviderTests)
///   PreCheckMinPwdAge -> PasswordTooRecentlyChanged (see LdapPasswordChangeProviderTests)
///   Disabled account -> ChangeNotPermitted        (provider-specific)
///   Portal lockout   -> PortalLockout             (LockoutPasswordChangeProvider decorator)
/// </remarks>
public abstract class IPasswordChangeProviderContract
{
    /// <summary>
    /// Build a fresh provider instance (plus any test-fake dependencies) for a single test.
    /// Must be idempotent and isolated across calls — no shared state between tests.
    /// </summary>
    protected abstract IPasswordChangeProvider CreateProvider();

    /// <summary>
    /// Seed a user into the provider's backing fake so that a lookup for
    /// <paramref name="username"/> finds a DN and a Modify call with
    /// <paramref name="currentPassword"/> succeeds (while any other current-password
    /// value surfaces <see cref="ApiErrorCode.InvalidCredentials"/>).
    /// </summary>
    protected abstract TestUser SeedUser(string username, string currentPassword);

    protected sealed record TestUser(string Username, string Password);

    [Fact]
    public virtual async Task InvalidCredentials_ReturnsInvalidCredentials()
    {
        var provider = CreateProvider();
        var user = SeedUser("alice", "CorrectPass1!");

        var result = await provider.PerformPasswordChangeAsync(user.Username, "WrongPass1!", "NewPass1!");

        Assert.NotNull(result);
        Assert.Equal(ApiErrorCode.InvalidCredentials, result!.ErrorCode);
    }

    [Fact]
    public virtual async Task UnknownUser_ReturnsUserNotFound()
    {
        var provider = CreateProvider();
        // No SeedUser call — no user exists under any AllowedUsernameAttributes filter.

        var result = await provider.PerformPasswordChangeAsync("ghost", "any", "NewPass1!");

        Assert.NotNull(result);
        Assert.Equal(ApiErrorCode.UserNotFound, result!.ErrorCode);
    }

    [Fact]
    public virtual async Task HappyPath_ReturnsNull()
    {
        var provider = CreateProvider();
        var user = SeedUser("alice", "CorrectPass1!");

        var result = await provider.PerformPasswordChangeAsync(user.Username, user.Password, "NewPass1!");

        Assert.Null(result);
    }

    [Fact]
    public virtual async Task WeakPassword_ReturnsComplexPassword()
    {
        var provider = CreateProvider();
        var user = SeedUser("alice", "CorrectPass1!");

        // Contract convention: the sentinel value "weak" signals the fixture to emit
        // the directory's complexity-violation response (AD: ConstraintViolation with
        // ERROR_PASSWORD_RESTRICTION 0x52D; Windows: equivalent COMException mapping).
        var result = await provider.PerformPasswordChangeAsync(user.Username, user.Password, "weak");

        Assert.NotNull(result);
        Assert.Equal(ApiErrorCode.ComplexPassword, result!.ErrorCode);
    }

    [Fact]
    public virtual async Task UsernameFallback_MatchesUpnAfterSamMiss()
    {
        var provider = CreateProvider();
        // Username is the UPN form — fixture seeds against UPN, not sAMAccountName,
        // so the provider must fall through past the sAM search to find the user.
        var user = SeedUser("dave@corp.example.com", "CorrectPass1!");

        var result = await provider.PerformPasswordChangeAsync(user.Username, user.Password, "NewPass1!");

        Assert.Null(result);
    }

    [Fact]
    public virtual async Task EmptyNewPassword_ReturnsComplexPassword()
    {
        var provider = CreateProvider();
        var user = SeedUser("alice", "CorrectPass1!");

        // Empty/too-short new password is a complexity violation from the directory's perspective.
        // Fixture routes this through the same "weak" sentinel path.
        var result = await provider.PerformPasswordChangeAsync(user.Username, user.Password, string.Empty);

        Assert.NotNull(result);
        Assert.Equal(ApiErrorCode.ComplexPassword, result!.ErrorCode);
    }

    [Fact]
    public virtual async Task IdenticalOldAndNew_ReturnsComplexPassword()
    {
        var provider = CreateProvider();
        var user = SeedUser("alice", "CorrectPass1!");

        // Old == new is an AD password-history violation → ComplexPassword semantics.
        // Fixture treats this as another "weak" sentinel case.
        var result = await provider.PerformPasswordChangeAsync(user.Username, user.Password, user.Password);

        Assert.NotNull(result);
        Assert.Equal(ApiErrorCode.ComplexPassword, result!.ErrorCode);
    }
}
