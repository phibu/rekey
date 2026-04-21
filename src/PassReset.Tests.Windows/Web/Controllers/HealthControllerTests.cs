using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace PassReset.Tests.Windows.Web.Controllers;

/// <summary>
/// Integration tests for GET /api/health via <see cref="WebApplicationFactory{TEntryPoint}"/>.
/// Verifies D-01 wire shape (nested checks, aggregate rollup), no-secrets guarantee (D-04),
/// and aggregate state transitions (healthy / degraded / unhealthy → 200/503).
///
/// AD connectivity strategy (CI-friendly): each factory owns a <see cref="TcpListener"/>
/// bound to 127.0.0.1:0 and accepts connections in a background loop. The factory seeds
/// PasswordChangeOptions.LdapHostnames=["127.0.0.1"] + LdapPort=&lt;bound port&gt; so
/// CheckAdConnectivityAsync sees a reachable endpoint and reports "healthy" without any
/// domain controller being present. The debug provider path is still used for the actual
/// password change flow, so AD is only probed by the health check.
/// </summary>
public class HealthControllerTests : IDisposable
{
    private const string SmtpPasswordSentinel = "TEST_SECRET_DO_NOT_LEAK";
    private const string RecaptchaPrivateKeySentinel = "TEST_RECAPTCHA_PRIVATE_DO_NOT_LEAK";

    private readonly DebugFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

    // ── Wire-shape DTOs ────────────────────────────────────────────────────────
    private sealed class HealthResponseDto
    {
        [JsonPropertyName("status")]    public string? Status    { get; set; }
        [JsonPropertyName("timestamp")] public DateTimeOffset? Timestamp { get; set; }
        [JsonPropertyName("checks")]    public ChecksDto? Checks  { get; set; }
    }

    private sealed class ChecksDto
    {
        [JsonPropertyName("ad")]            public CheckDto? Ad            { get; set; }
        [JsonPropertyName("smtp")]          public CheckDto? Smtp          { get; set; }
        [JsonPropertyName("expiryService")] public CheckDto? ExpiryService { get; set; }
    }

    private sealed class CheckDto
    {
        [JsonPropertyName("status")]       public string? Status        { get; set; }
        [JsonPropertyName("latency_ms")]   public long?   Latency_ms    { get; set; }
        [JsonPropertyName("last_checked")] public DateTimeOffset? Last_checked { get; set; }
        [JsonPropertyName("skipped")]      public bool?   Skipped       { get; set; }
    }

    // ── Test 1 ─ Shape -----------------------------------------------------------
    [Fact]
    public async Task Get_ReturnsOk_WithNestedChecksShape()
    {
        using var client = NewClient();
        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<HealthResponseDto>();

        Assert.NotNull(dto);
        Assert.NotNull(dto!.Status);
        Assert.NotNull(dto.Timestamp);
        Assert.NotNull(dto.Checks);
        Assert.NotNull(dto.Checks!.Ad);
        Assert.NotNull(dto.Checks.Smtp);
        Assert.NotNull(dto.Checks.ExpiryService);

        Assert.NotNull(dto.Checks.Ad!.Status);
        Assert.NotNull(dto.Checks.Ad.Latency_ms);
        Assert.NotNull(dto.Checks.Ad.Last_checked);

        Assert.NotNull(dto.Checks.Smtp!.Status);
        Assert.NotNull(dto.Checks.Smtp.Latency_ms);
        Assert.NotNull(dto.Checks.Smtp.Last_checked);

        Assert.NotNull(dto.Checks.ExpiryService!.Status);
        Assert.NotNull(dto.Checks.ExpiryService.Latency_ms);
        Assert.NotNull(dto.Checks.ExpiryService.Last_checked);
    }

    // ── Test 2 ─ SMTP skipped when both email features disabled ------------------
    [Fact]
    public async Task Get_SmtpSkipped_WhenBothEmailFeaturesDisabled()
    {
        using var client = NewClient();
        var response = await client.GetAsync("/api/health");
        var dto = await response.Content.ReadFromJsonAsync<HealthResponseDto>();

        Assert.NotNull(dto);
        Assert.Equal("skipped", dto!.Checks!.Smtp!.Status);
        Assert.True(dto.Checks.Smtp.Skipped);
    }

    // ── Test 3 ─ ExpiryService not-enabled => healthy aggregate ------------------
    [Fact]
    public async Task Get_ExpiryService_NotEnabled_ReturnsHealthy()
    {
        using var client = NewClient();
        var response = await client.GetAsync("/api/health");
        var dto = await response.Content.ReadFromJsonAsync<HealthResponseDto>();

        Assert.NotNull(dto);
        Assert.Equal("not-enabled", dto!.Checks!.ExpiryService!.Status);
        Assert.Equal("healthy", dto.Status);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Test 4 ─ No secrets in body ---------------------------------------------
    [Fact]
    public async Task Health_Body_ContainsNoSecrets()
    {
        using var client = NewClient();
        var response = await client.GetAsync("/api/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(SmtpPasswordSentinel, body);
        Assert.DoesNotContain(RecaptchaPrivateKeySentinel, body);
        Assert.DoesNotContain("\"password\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("privateKey", body, StringComparison.OrdinalIgnoreCase);
    }

    // ── Test 5 ─ All healthy → 200 ----------------------------------------------
    [Fact]
    public async Task AllHealthy_Returns200()
    {
        using var client = NewClient();
        var response = await client.GetAsync("/api/health");
        var dto = await response.Content.ReadFromJsonAsync<HealthResponseDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("healthy", dto!.Status);
    }

    // ── Test 6 ─ Any unhealthy → 503 --------------------------------------------
    [Fact]
    public async Task AnyUnhealthy_RollsUpToUnhealthy()
    {
        using var factory = new UnhealthySmtpFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/health");
        var dto = await response.Content.ReadFromJsonAsync<HealthResponseDto>();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.NotNull(dto);
        Assert.Equal("unhealthy", dto!.Status);
        Assert.Equal("unhealthy", dto.Checks!.Smtp!.Status);
    }

    // ── Factories ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Base factory that owns a loopback TCP listener used as a stand-in for an LDAP
    /// endpoint so CheckAdConnectivityAsync can report "healthy" in CI without requiring
    /// a real domain controller. The listener simply accepts + closes connections.
    /// </summary>
    public abstract class FakeAdFactory : WebApplicationFactory<Program>
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _acceptLoopCts = new();

        protected int FakeAdPort { get; }

        protected FakeAdFactory()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            FakeAdPort = ((IPEndPoint)_listener.LocalEndpoint).Port;

            // Background accept loop — accepts then immediately closes each connection.
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!_acceptLoopCts.IsCancellationRequested)
                    {
                        var c = await _listener.AcceptTcpClientAsync(_acceptLoopCts.Token);
                        c.Close();
                    }
                }
                catch (OperationCanceledException) { /* shutdown */ }
                catch (ObjectDisposedException)    { /* shutdown */ }
                catch (SocketException)            { /* shutdown */ }
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _acceptLoopCts.Cancel(); }  catch { /* swallow */ }
                try { _listener.Stop(); }         catch { /* swallow */ }
                _acceptLoopCts.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Development fixture — debug provider, email/expiry disabled. Seeds sentinels
    /// so tests can assert they never leak into the health response body.
    /// </summary>
    public sealed class DebugFactory : FakeAdFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var adPort = FakeAdPort.ToString(CultureInfo.InvariantCulture);
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["WebSettings:UseDebugProvider"]                          = "true",
                    ["WebSettings:EnableHttpsRedirect"]                       = "false",
                    ["ClientSettings:MinimumDistance"]                        = "0",
                    ["ClientSettings:Recaptcha:Enabled"]                      = "false",
                    ["ClientSettings:Recaptcha:PrivateKey"]                   = RecaptchaPrivateKeySentinel,
                    ["EmailNotificationSettings:Enabled"]                     = "false",
                    ["PasswordExpiryNotificationSettings:Enabled"]            = "false",
                    ["SiemSettings:Syslog:Enabled"]                           = "false",
                    ["SiemSettings:AlertEmail:Enabled"]                       = "false",
                    // Keep SmtpSettings.Host empty so CheckSmtpAsync never runs; the validator
                    // is a no-op when Host is empty (see SmtpSettingsValidator line 20).
                    ["SmtpSettings:Host"]                                     = "",
                    ["SmtpSettings:Port"]                                     = "25",
                    ["SmtpSettings:Password"]                                 = SmtpPasswordSentinel,
                    ["PasswordChangeOptions:PortalLockoutThreshold"]          = "0",
                    // Drive CheckAdConnectivityAsync through the LDAP-hostname branch against
                    // the loopback listener owned by this factory → reachable → healthy.
                    ["PasswordChangeOptions:UseAutomaticContext"]             = "false",
                    ["PasswordChangeOptions:LdapHostnames:0"]                 = "127.0.0.1",
                    ["PasswordChangeOptions:LdapPort"]                        = adPort,
                });
            });
        }
    }

    /// <summary>
    /// Fixture that forces the SMTP probe to run (email enabled) but points it at
    /// TEST-NET-1 so the connect pends and the 3s CTS fires → smtp=unhealthy.
    /// AD check uses the same loopback listener pattern so it stays healthy.
    /// </summary>
    public sealed class UnhealthySmtpFactory : FakeAdFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var adPort = FakeAdPort.ToString(CultureInfo.InvariantCulture);
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["WebSettings:UseDebugProvider"]                          = "true",
                    ["WebSettings:EnableHttpsRedirect"]                       = "false",
                    ["ClientSettings:MinimumDistance"]                        = "0",
                    ["ClientSettings:Recaptcha:Enabled"]                      = "false",
                    ["EmailNotificationSettings:Enabled"]                     = "true",
                    ["PasswordExpiryNotificationSettings:Enabled"]            = "false",
                    ["SiemSettings:Syslog:Enabled"]                           = "false",
                    ["SiemSettings:AlertEmail:Enabled"]                       = "false",
                    // TEST-NET-1 / RFC 5737 blackhole — the 3s CTS must fire.
                    ["SmtpSettings:Host"]                                     = "192.0.2.1",
                    ["SmtpSettings:Port"]                                     = "1",
                    ["SmtpSettings:FromAddress"]                              = "passreset@test.invalid",
                    ["SmtpSettings:Username"]                                 = "",
                    ["SmtpSettings:Password"]                                 = "",
                    ["PasswordChangeOptions:PortalLockoutThreshold"]          = "0",
                    ["PasswordChangeOptions:UseAutomaticContext"]             = "false",
                    ["PasswordChangeOptions:LdapHostnames:0"]                 = "127.0.0.1",
                    ["PasswordChangeOptions:LdapPort"]                        = adPort,
                });
            });
        }
    }
}
