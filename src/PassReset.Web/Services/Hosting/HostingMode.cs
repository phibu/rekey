namespace PassReset.Web.Services.Hosting;

/// <summary>
/// Runtime hosting mode for the PassReset web process. Determined once at startup
/// and logged for observability. Does not change runtime behavior; operators pick
/// the mode at install time via <c>Install-PassReset.ps1 -HostingMode</c>.
/// </summary>
public enum HostingMode
{
    /// <summary>Hosted by IIS via the ASP.NET Core Module. TLS is terminated by IIS.</summary>
    Iis,

    /// <summary>Running as a Windows Service under SCM. Kestrel terminates TLS directly.</summary>
    Service,

    /// <summary>Running as a console process (dev / debugging). Kestrel terminates TLS if configured.</summary>
    Console,
}
