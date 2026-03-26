using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace ReKey.Web.Controllers;

/// <summary>
/// Provides a lightweight health probe for load balancers and monitoring.
/// GET /api/health
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class HealthController : ControllerBase
{
    private static readonly string _version =
        Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "0.0.0";

    /// <summary>Returns the application health status.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Get() =>
        Ok(new
        {
            status    = "healthy",
            timestamp = DateTimeOffset.UtcNow,
            version   = _version,
        });
}
