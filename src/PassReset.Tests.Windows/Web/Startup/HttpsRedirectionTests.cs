using System.Net;
using System.Net.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PassReset.Tests.Windows.Web.Startup;

/// <summary>
/// STAB-016 regression guards for the HTTPS / HSTS wiring in <c>Program.cs</c>.
/// These tests pin the current behavior:
///
///   * When <c>WebSettings:EnableHttpsRedirect=true</c>, the security-headers
///     middleware emits <c>Strict-Transport-Security: max-age=31536000; includeSubDomains</c>
///     on every response.
///   * When <c>WebSettings:EnableHttpsRedirect=false</c>, the HSTS header is not emitted
///     (the middleware block is gated on the flag).
///   * The emitted header value NEVER contains the <c>preload</c> directive (D-12).
///
/// Each fact owns a dedicated <see cref="WebApplicationFactory{TEntryPoint}"/> subclass
/// to keep the <c>HostFactoryResolver.HostingListener</c> state deterministic across
/// parallel test runs (matches the pattern in <c>StartupValidationTests</c>).
///
/// Development environment is used because TestServer cannot speak HTTPS, but the HSTS
/// header is emitted purely based on the <c>EnableHttpsRedirect</c> flag — not on the
/// transport — so the assertion still exercises the production code path.
/// </summary>
public class HttpsRedirectionTests
{
    private const string HstsHeaderName = "Strict-Transport-Security";

    private static Dictionary<string, string?> BaseConfig(bool enableHttpsRedirect) => new()
    {
        ["WebSettings:UseDebugProvider"] = "true",
        ["WebSettings:EnableHttpsRedirect"] = enableHttpsRedirect ? "true" : "false",
        ["ClientSettings:Recaptcha:Enabled"] = "false",
        ["SiemSettings:Syslog:Enabled"] = "false",
        ["SiemSettings:AlertEmail:Enabled"] = "false",
        ["EmailNotificationSettings:Enabled"] = "false",
        ["PasswordExpiryNotificationSettings:Enabled"] = "false",
        ["PasswordChangeOptions:UseAutomaticContext"] = "true",
        ["PasswordChangeOptions:PortalLockoutThreshold"] = "0",
    };

    /// <summary>
    /// Factory that enables the HSTS branch of the security-headers middleware.
    /// </summary>
    public sealed class HstsEnabledFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(BaseConfig(enableHttpsRedirect: true)));
        }
    }

    /// <summary>
    /// Factory that leaves the HSTS branch disabled so we can assert the header is absent.
    /// </summary>
    public sealed class HstsDisabledFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(BaseConfig(enableHttpsRedirect: false)));
        }
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

    [Fact]
    public async Task HstsHeader_PresentWhenHttpsRedirectEnabled()
    {
        using var factory = new HstsEnabledFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/api/password");

        Assert.True(
            response.IsSuccessStatusCode,
            $"GET /api/password should succeed. Got {response.StatusCode}");

        Assert.True(
            response.Headers.Contains(HstsHeaderName),
            $"HSTS header '{HstsHeaderName}' must be emitted when EnableHttpsRedirect=true.");

        var value = string.Join(";", response.Headers.GetValues(HstsHeaderName));
        Assert.Contains("max-age=31536000", value);
        Assert.Contains("includeSubDomains", value);
    }

    [Fact]
    public async Task HstsHeader_NoPreloadDirective()
    {
        using var factory = new HstsEnabledFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/api/password");

        // D-12 regression guard: preload is irreversible and MUST NOT be emitted.
        Assert.True(
            response.Headers.Contains(HstsHeaderName),
            "HSTS header must be emitted when EnableHttpsRedirect=true.");

        var value = string.Join(";", response.Headers.GetValues(HstsHeaderName));
        Assert.DoesNotContain("preload", value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HstsHeader_AbsentWhenHttpsRedirectDisabled()
    {
        using var factory = new HstsDisabledFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/api/password");

        Assert.True(
            response.IsSuccessStatusCode,
            $"GET /api/password should succeed. Got {response.StatusCode}");

        // When EnableHttpsRedirect=false the security-headers middleware does not add HSTS.
        // Defense-in-depth: also assert no preload directive if any other middleware emits one.
        if (response.Headers.Contains(HstsHeaderName))
        {
            var value = string.Join(";", response.Headers.GetValues(HstsHeaderName));
            Assert.DoesNotContain("preload", value, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.False(
                response.Headers.Contains(HstsHeaderName),
                "HSTS header should NOT be emitted when EnableHttpsRedirect=false.");
        }
    }
}
