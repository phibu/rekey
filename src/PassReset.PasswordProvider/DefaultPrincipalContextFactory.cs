using System.DirectoryServices.AccountManagement;

namespace PassReset.PasswordProvider;

/// <summary>
/// Production <see cref="IPrincipalContextFactory"/>: calls the real BCL types.
/// Contains no logic — exists only to satisfy DI without widening the public API
/// of <see cref="PasswordChangeProvider"/>.
/// </summary>
public sealed class DefaultPrincipalContextFactory : IPrincipalContextFactory
{
    public PrincipalContext CreateDomainContext(string? container = null, string? username = null, string? password = null)
    {
        // PrincipalContext ctor overloads differ by parameter count; pick the right one
        // to avoid passing nulls where the overload doesn't accept them.
        if (username is not null)
            return new PrincipalContext(ContextType.Domain, null, container, username, password);
        if (container is not null)
            return new PrincipalContext(ContextType.Domain, null, container);
        return new PrincipalContext(ContextType.Domain);
    }

    public UserPrincipal? FindUser(PrincipalContext context, IdentityType identityType, string identityValue) =>
        UserPrincipal.FindByIdentity(context, identityType, identityValue);
}
