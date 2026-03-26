namespace ReKey.Common;

/// <summary>
/// Interface for any Application settings provider (LDAP/AD connection settings).
/// </summary>
public interface IAppSettings
{
    /// <summary>Gets or sets the default domain.</summary>
    string DefaultDomain { get; set; }

    /// <summary>
    /// Gets or sets the LDAP port.
    /// Optional, defaults to 636 -- the default port for LDAPS (LDAP over TLS).
    /// </summary>
    int LdapPort { get; set; }

    /// <summary>
    /// Gets or sets the LDAP hostnames.
    /// Required, one or more hostnames or IP addresses exposing an LDAP/LDAPS endpoint.
    /// </summary>
    string[] LdapHostnames { get; set; }

    /// <summary>Gets or sets the LDAP password.</summary>
    string LdapPassword { get; set; }

    /// <summary>Gets or sets the LDAP username.</summary>
    string LdapUsername { get; set; }
}
