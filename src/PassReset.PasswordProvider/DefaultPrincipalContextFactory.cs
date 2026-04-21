using System.DirectoryServices.AccountManagement;

namespace PassReset.PasswordProvider;

/// <summary>
/// Production <see cref="IPrincipalContextFactory"/>: calls the real BCL types.
/// Contains no logic — exists only to satisfy DI without widening the public API
/// of <see cref="PasswordChangeProvider"/>.
/// </summary>
public sealed class DefaultPrincipalContextFactory : IPrincipalContextFactory
{
    public PrincipalContext CreateDomainContext(
        string? server = null,
        string? container = null,
        ContextOptions? options = null,
        string? username = null,
        string? password = null)
    {
        // Dispatch to the narrowest BCL ctor that fits the supplied args.
        // Positional args throughout to avoid overload-resolution ambiguity.
        if (username is not null)
            return new PrincipalContext(ContextType.Domain, server, container, options ?? ContextOptions.Negotiate, username, password);
        if (options is not null)
            return new PrincipalContext(ContextType.Domain, server, container, options.Value);
        if (container is not null)
            return new PrincipalContext(ContextType.Domain, server, container);
        if (server is not null)
            return new PrincipalContext(ContextType.Domain, server);
        return new PrincipalContext(ContextType.Domain);
    }

    public UserPrincipal? FindUser(PrincipalContext context, IdentityType identityType, string identityValue) =>
        UserPrincipal.FindByIdentity(context, identityType, identityValue);
}
