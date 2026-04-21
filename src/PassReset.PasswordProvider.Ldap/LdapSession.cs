using System.DirectoryServices.Protocols;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

namespace PassReset.PasswordProvider.Ldap;

/// <summary>
/// Default <see cref="ILdapSession"/> implementation wrapping a <see cref="LdapConnection"/>.
/// One session per password-change request (no pooling — low-frequency operation).
/// </summary>
public sealed class LdapSession : ILdapSession
{
    private readonly LdapConnection _conn;
    private readonly string _hostname;
    private readonly NetworkCredential _creds;
    private readonly ILogger _logger;
    private readonly IReadOnlySet<string> _trustedThumbprints;
    private SearchResultEntry? _rootDse;
    private bool _rootDseLoaded;

    public LdapSession(
        string hostname,
        int port,
        bool useLdaps,
        string serviceAccountDn,
        string serviceAccountPassword,
        IEnumerable<string>? trustedThumbprints,
        ILogger logger)
    {
        _conn = new LdapConnection(new LdapDirectoryIdentifier(hostname, port));
        _hostname = hostname;
        _conn.SessionOptions.ProtocolVersion = 3;
        _conn.SessionOptions.SecureSocketLayer = useLdaps;
        _conn.AuthType = AuthType.Basic;
        _creds = new NetworkCredential(serviceAccountDn, serviceAccountPassword);
        _logger = logger;
        _trustedThumbprints = (trustedThumbprints ?? Enumerable.Empty<string>())
            .Select(t => t.Replace(":", "").ToUpperInvariant())
            .ToHashSet();

        if (useLdaps && _trustedThumbprints.Count > 0)
        {
            _conn.SessionOptions.VerifyServerCertificate = VerifyServerCertificate;
        }
    }

    public void Bind() => _conn.Bind(_creds);

    public SearchResponse Search(SearchRequest request) => (SearchResponse)_conn.SendRequest(request);

    public ModifyResponse Modify(ModifyRequest request) => (ModifyResponse)_conn.SendRequest(request);

    public SearchResultEntry? RootDse
    {
        get
        {
            if (_rootDseLoaded) return _rootDse;
            _rootDseLoaded = true;
            try
            {
                var req = new SearchRequest(
                    distinguishedName: null,
                    ldapFilter: "(objectClass=*)",
                    searchScope: SearchScope.Base,
                    attributeList: new[] { "defaultNamingContext", "dnsHostName", "minPwdAge", "maxPwdAge", "minPwdLength" });
                var resp = (SearchResponse)_conn.SendRequest(req);
                _rootDse = resp.Entries.Count > 0 ? resp.Entries[0] : null;
            }
            catch (Exception ex) when (ex is LdapException or DirectoryOperationException)
            {
                _logger.LogWarning(ex, "Root DSE query failed on {Host}", _hostname);
                _rootDse = null;
            }
            return _rootDse;
        }
    }

    private bool VerifyServerCertificate(LdapConnection connection, X509Certificate certificate)
    {
        using var cert2 = new X509Certificate2(certificate);

        // If the system store already trusts the cert (with offline revocation check), let it through.
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.Offline;
        if (chain.Build(cert2)) return true;

        // Otherwise, match against the operator-configured thumbprint allow-list.
        var sha1 = cert2.Thumbprint ?? string.Empty;
        var sha256 = Convert.ToHexString(cert2.GetCertHash(System.Security.Cryptography.HashAlgorithmName.SHA256));
        var trusted = _trustedThumbprints.Contains(sha1.ToUpperInvariant())
                      || _trustedThumbprints.Contains(sha256.ToUpperInvariant());

        if (!trusted)
        {
            _logger.LogError(
                "LDAPS certificate rejected: neither system trust nor LdapTrustedCertificateThumbprints matched. SHA-1={Sha1}, SHA-256={Sha256}",
                sha1, sha256);
        }
        return trusted;
    }

    public void Dispose() => _conn.Dispose();
}
