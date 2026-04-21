using PassReset.Common;
using PassReset.Tests.Contracts;
using Xunit;

namespace PassReset.Tests.Windows.Contracts;

/// <summary>
/// Windows-provider side of the shared <see cref="IPasswordChangeProviderContract"/>.
///
/// All contract facts are currently <see cref="FactAttribute.Skip"/>-gated.
/// <see cref="PassReset.PasswordProvider.PasswordChangeProvider"/> depends on
/// <see cref="System.DirectoryServices.AccountManagement.PrincipalContext"/> directly:
/// it instantiates <c>PrincipalContext</c> and <c>UserPrincipal.FindByIdentity</c>
/// inside <c>PerformPasswordChangeAsync</c>, so there is no seam where a test fake
/// can replace the directory backend (short of bringing up a real AD domain or a
/// local LDAP server — neither is available on CI).
///
/// The LDAP provider was deliberately built around <see cref="PassReset.PasswordProvider.Ldap.ILdapSession"/>
/// precisely so that parity could be enforced via this contract. Introducing an
/// equivalent seam on the Windows provider (e.g. an <c>IPrincipalContext</c> facade)
/// is out of scope for Phase 11 and is tracked as follow-up work. Until then,
/// contract parity is enforced one-sided: the contract is satisfied by
/// <see cref="LdapPasswordChangeProviderContractTests"/> only, and the Windows
/// provider is covered by its implementation-specific tests in this project
/// (PasswordProvider/, Services/, Web/).
/// </summary>
public sealed class PasswordChangeProviderContractTests : IPasswordChangeProviderContract
{
    private const string SkipReason =
        "Windows PasswordChangeProvider has no test seam — it uses PrincipalContext/UserPrincipal directly. " +
        "Unblock by introducing an IPrincipalContext (or equivalent) abstraction on the Windows provider " +
        "in a future phase, then remove these Skip attributes.";

    protected override IPasswordChangeProvider CreateProvider() =>
        throw new NotSupportedException(SkipReason);

    protected override TestUser SeedUser(string username, string currentPassword) =>
        throw new NotSupportedException(SkipReason);

    // Override every contract [Fact] to mark it Skip. Without these overrides, xUnit
    // would discover the facts from the abstract base and attempt to run them — which
    // would all hit the NotSupportedException above and appear as test failures.
    // Explicit Skip keeps the output honest: parity is pending, not broken.

    [Fact(Skip = SkipReason)]
    public override Task InvalidCredentials_ReturnsInvalidCredentials() => Task.CompletedTask;

    [Fact(Skip = SkipReason)]
    public override Task UnknownUser_ReturnsUserNotFound() => Task.CompletedTask;

    [Fact(Skip = SkipReason)]
    public override Task HappyPath_ReturnsNull() => Task.CompletedTask;

    [Fact(Skip = SkipReason)]
    public override Task WeakPassword_ReturnsComplexPassword() => Task.CompletedTask;

    [Fact(Skip = SkipReason)]
    public override Task UsernameFallback_MatchesUpnAfterSamMiss() => Task.CompletedTask;

    [Fact(Skip = SkipReason)]
    public override Task EmptyNewPassword_ReturnsComplexPassword() => Task.CompletedTask;

    [Fact(Skip = SkipReason)]
    public override Task IdenticalOldAndNew_ReturnsComplexPassword() => Task.CompletedTask;
}
