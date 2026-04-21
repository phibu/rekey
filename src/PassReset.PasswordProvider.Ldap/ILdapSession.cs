using System.DirectoryServices.Protocols;

namespace PassReset.PasswordProvider.Ldap;

/// <summary>
/// Thin adapter over <see cref="LdapConnection"/> to isolate the LDAP I/O surface for testing.
/// The fake <c>FakeLdapSession</c> used in unit + contract tests implements this interface
/// to script directory behavior without a live AD.
/// </summary>
public interface ILdapSession : IDisposable
{
    /// <summary>
    /// Bind to the configured directory using the service account credentials supplied at construction.
    /// Throws <see cref="LdapException"/> on auth failure.
    /// </summary>
    void Bind();

    /// <summary>
    /// Execute a <see cref="SearchRequest"/> and return the full response. Callers MUST check
    /// <see cref="DirectoryResponse.ResultCode"/> before reading <see cref="SearchResponse.Entries"/>.
    /// </summary>
    SearchResponse Search(SearchRequest request);

    /// <summary>
    /// Execute a <see cref="ModifyRequest"/> (including the unicodePwd atomic-change pattern).
    /// Throws <see cref="DirectoryOperationException"/> on server-side rejection so callers can
    /// inspect <see cref="DirectoryResponse.ResultCode"/> and the Win32 extended error code.
    /// </summary>
    ModifyResponse Modify(ModifyRequest request);

    /// <summary>
    /// Root DSE attributes (<c>defaultNamingContext</c>, <c>dnsHostName</c>, etc.).
    /// Convenience: returns <c>null</c> if the root DSE query fails rather than throwing.
    /// </summary>
    SearchResultEntry? RootDse { get; }
}
