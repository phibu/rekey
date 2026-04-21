namespace PassReset.Common;

/// <summary>
/// Selects which <see cref="IPasswordChangeProvider"/> implementation PassReset uses at runtime.
/// </summary>
public enum ProviderMode
{
    /// <summary>
    /// Picks <see cref="Windows"/> on Windows platforms, <see cref="Ldap"/> elsewhere.
    /// Default for upgraded deployments (existing Windows installs see no behavior change).
    /// </summary>
    Auto = 0,

    /// <summary>
    /// AccountManagement-based provider (requires <c>net10.0-windows</c>).
    /// Fails validation on non-Windows platforms.
    /// </summary>
    Windows = 1,

    /// <summary>
    /// LdapConnection-based provider (<c>System.DirectoryServices.Protocols</c>).
    /// Works on Windows, Linux, and macOS.
    /// </summary>
    Ldap = 2,
}
