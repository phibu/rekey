namespace PassReset.Web.Configuration;

/// <summary>
/// Settings for the Phase 13 admin UI + encrypted secret storage.
/// See <c>docs/Admin-UI.md</c>.
/// </summary>
public sealed class AdminSettings
{
    /// <summary>Master feature flag. Defaults to <c>false</c> (opt-in): the admin listener is only started and pages mapped when explicitly enabled in configuration.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>TCP port for the 127.0.0.1-bound Kestrel listener. Range 1024-65535.</summary>
    public int LoopbackPort { get; set; } = 5010;

    /// <summary>
    /// Absolute path where ASP.NET Core Data Protection persists its key ring.
    /// When null, defaults to <c>&lt;AppContext.BaseDirectory&gt;/keys</c>.
    /// </summary>
    public string? KeyStorePath { get; set; }

    /// <summary>
    /// SHA-1 thumbprint of an X.509 cert in <c>LocalMachine\My</c> used to protect the DP
    /// key ring on Linux. Ignored on Windows (DPAPI is used automatically).
    /// Required on Linux when <see cref="Enabled"/> is true.
    /// </summary>
    public string? DataProtectionCertThumbprint { get; set; }

    /// <summary>
    /// Absolute path to the <c>appsettings.Production.json</c> file that
    /// <see cref="Services.Configuration.IAppSettingsEditor"/> reads and writes.
    /// When null, resolves to <c>&lt;AppContext.BaseDirectory&gt;/appsettings.Production.json</c>.
    /// </summary>
    public string? AppSettingsFilePath { get; set; }

    /// <summary>
    /// Absolute path to the encrypted <c>secrets.dat</c> file.
    /// When null, resolves to <c>&lt;AppContext.BaseDirectory&gt;/secrets.dat</c>.
    /// </summary>
    public string? SecretsFilePath { get; set; }
}
