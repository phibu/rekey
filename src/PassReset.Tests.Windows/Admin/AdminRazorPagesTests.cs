using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PassReset.Web.Services.Configuration;
using Xunit;

namespace PassReset.Tests.Windows.Admin;

public sealed class AdminRazorPagesTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = default!;
    private string _tempDir = default!;

    public ValueTask InitializeAsync()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "passreset-admin-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var appSettingsPath = Path.Combine(_tempDir, "appsettings.Production.json");
        File.WriteAllText(appSettingsPath, "{}");

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Development");
                b.ConfigureAppConfiguration((_, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["AdminSettings:Enabled"] = "true",
                        ["AdminSettings:LoopbackPort"] = "5011",
                        ["AdminSettings:AppSettingsFilePath"] = appSettingsPath,
                        ["AdminSettings:SecretsFilePath"] = Path.Combine(_tempDir, "secrets.dat"),
                        ["AdminSettings:KeyStorePath"] = Path.Combine(_tempDir, "keys"),
                    });
                });
            });
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _factory.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        return ValueTask.CompletedTask;
    }

    private HttpClient NewAdminClient() => _factory.CreateDefaultClient();

    [Fact]
    public async Task Get_AdminIndex_Returns200()
    {
        using var client = NewAdminClient();
        var resp = await client.GetAsync("/admin");
        // WebApplicationFactory drives the in-memory TestServer on a single port,
        // so MapWhen's port filter is effectively always matched; integration
        // testing covers the Razor Pages contract, not the port-split itself
        // (which is covered by LoopbackOnlyGuardTests).
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Get_AdminSmtp_Returns200()
    {
        using var client = NewAdminClient();
        var resp = await client.GetAsync("/admin/Smtp");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Post_AdminSmtp_WithoutAntiforgery_Returns400()
    {
        using var client = NewAdminClient();
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Input.Host", "smtp.example.com"),
            new KeyValuePair<string, string>("Input.Port", "25"),
            new KeyValuePair<string, string>("Input.FromAddress", "noreply@example.com"),
        });
        var resp = await client.PostAsync("/admin/Smtp", content);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Post_AdminSmtp_WithInvalidPort_ReRendersWithValidationError()
    {
        using var client = NewAdminClient();
        var (token, cookie) = await GetAntiforgeryAsync(client, "/admin/Smtp");
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
            new KeyValuePair<string, string>("Input.Host", "smtp.example.com"),
            new KeyValuePair<string, string>("Input.Port", "0"),
            new KeyValuePair<string, string>("Input.FromAddress", "noreply@example.com"),
        });
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        var resp = await client.PostAsync("/admin/Smtp", content);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("Input.Port", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Post_AdminSmtp_WithEmptyPassword_DoesNotOverwriteStoredSecret()
    {
        // Pre-seed a secret via ISecretStore
        using var scope = _factory.Services.CreateScope();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretStore>();
        secrets.Save(new SecretBundle(null, null, "pre-existing-password", null));

        using var client = NewAdminClient();
        var (token, cookie) = await GetAntiforgeryAsync(client, "/admin/Smtp");
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
            new KeyValuePair<string, string>("Input.Host", "smtp.example.com"),
            new KeyValuePair<string, string>("Input.Port", "25"),
            new KeyValuePair<string, string>("Input.FromAddress", "noreply@example.com"),
            new KeyValuePair<string, string>("Input.NewPassword", ""), // blank = keep
        });
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        var resp = await client.PostAsync("/admin/Smtp", content);
        Assert.True(resp.StatusCode is HttpStatusCode.OK or HttpStatusCode.Redirect);

        // Assert the stored secret is unchanged
        Assert.Equal("pre-existing-password", secrets.Load().SmtpPassword);
    }

    private static async Task<(string Token, string Cookie)> GetAntiforgeryAsync(HttpClient client, string path)
    {
        var resp = await client.GetAsync(path);
        resp.EnsureSuccessStatusCode();
        var html = await resp.Content.ReadAsStringAsync();
        var idx = html.IndexOf("name=\"__RequestVerificationToken\"", StringComparison.Ordinal);
        if (idx < 0) return ("", "");
        var valIdx = html.IndexOf("value=\"", idx, StringComparison.Ordinal) + 7;
        var valEnd = html.IndexOf('"', valIdx);
        var token = html[valIdx..valEnd];
        var cookie = resp.Headers.TryGetValues("Set-Cookie", out var sc) ? string.Join("; ", sc) : "";
        return (token, cookie);
    }
}
