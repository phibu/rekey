namespace PassReset.Common;

/// <summary>
/// Interface for any Application settings provider (LDAP/AD connection settings).
/// </summary>
public interface IAppSettings
{
    /// <summary>Gets or sets the default domain.</summary>
    string DefaultDomain { get; set; }

    /// <summary>
    /// Gets or sets the LDAP port.
    /// Defaults to 636 (LDAPS). Use 389 for plain LDAP (not recommended).
    /// </summary>
    int LdapPort { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to use LDAPS (LDAP over TLS/SSL).
    /// Defaults to true. Set to false only when LDAPS is unavailable.
    /// </summary>
    bool LdapUseSsl { get; set; }

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
