namespace PassReset.Web.Configuration;

/// <summary>
/// Kestrel TLS certificate configuration. Used when <c>AdminSettings</c>/installer
/// selects <c>HostingMode.Service</c> — Kestrel binds HTTPS directly with this cert.
/// Ignored under IIS (IIS terminates TLS).
/// Exactly one of <see cref="Thumbprint"/> or <see cref="PfxPath"/> must be set in
/// Service mode; both null or both set is a validation error.
/// </summary>
public sealed class KestrelHttpsCertOptions
{
    /// <summary>SHA-1 thumbprint of a certificate in <see cref="StoreLocation"/>/<see cref="StoreName"/>.</summary>
    public string? Thumbprint { get; set; }

    /// <summary>Certificate store location. Defaults to <c>LocalMachine</c>. <c>CurrentUser</c> is not supported in Service mode.</summary>
    public string StoreLocation { get; set; } = "LocalMachine";

    /// <summary>Certificate store name. Defaults to <c>My</c> (Personal store).</summary>
    public string StoreName { get; set; } = "My";

    /// <summary>Absolute path to a PFX file. Mutually exclusive with <see cref="Thumbprint"/>.</summary>
    public string? PfxPath { get; set; }

    /// <summary>Password for the PFX file. Store via <c>SecretStore</c> (Phase 13) in production.</summary>
    public string? PfxPassword { get; set; }
}
