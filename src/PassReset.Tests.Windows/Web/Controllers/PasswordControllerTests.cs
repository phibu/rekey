using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using PassReset.Common;
using PassReset.Web.Models;

namespace PassReset.Tests.Windows.Web.Controllers;

/// <summary>
/// Integration tests for POST /api/password via <see cref="WebApplicationFactory{TEntryPoint}"/>.
/// Uses the in-process <c>DebugPasswordChangeProvider</c> so magic usernames produce
/// deterministic <see cref="ApiErrorCode"/> responses without touching Active Directory.
/// </summary>
public class PasswordControllerTests : IDisposable
{
    // Fresh factory per test instance — isolates the in-memory rate limiter state
    // so the 5-req/5-min fixed window policy does not leak across tests.
    private readonly DebugFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

    private static ChangePasswordModel MakeRequest(string username) => new()
    {
        Username          = username,
        CurrentPassword   = "OldPassword1!",
        NewPassword       = "BrandNewP@ssword123",
        NewPasswordVerify = "BrandNewP@ssword123",
        Recaptcha         = string.Empty,
    };

    // ApiResult/ApiErrorItem expose data as getter-only properties — System.Text.Json
    // cannot populate those on deserialization. Wire-shaped DTOs for tests.
    private sealed class ApiResultDto
    {
        [JsonPropertyName("errors")]
        public List<ApiErrorItemDto> Errors { get; set; } = new();

        [JsonPropertyName("payload")]
        public object? Payload { get; set; }
    }

    private sealed class ApiErrorItemDto
    {
        [JsonPropertyName("errorCode")]
        public ApiErrorCode ErrorCode { get; set; }

        [JsonPropertyName("fieldName")]
        public string? FieldName { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }

    private static async Task<ApiResultDto?> ReadResultAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<ApiResultDto>();

    [Fact]
    public async Task Get_ReturnsClientSettings()
    {
        using var client = NewClient();
        var response = await client.GetAsync("/api/password");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Post_ValidDebugUser_Returns200()
    {
        using var client = NewClient();
        var response = await client.PostAsJsonAsync("/api/password", MakeRequest("anyuser"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Post_InvalidCredentialsMagicUser_ReturnsInvalidCredentialsErrorCode()
    {
        using var client = NewClient();
        var response = await client.PostAsJsonAsync("/api/password", MakeRequest("invalidCredentials"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var result = await ReadResultAsync(response);
        Assert.NotNull(result);
        Assert.Contains(result!.Errors, e => e.ErrorCode == ApiErrorCode.InvalidCredentials);
    }

    [Fact]
    public async Task Post_UserNotFoundMagicUser_ReturnsUserNotFoundErrorCode()
    {
        using var client = NewClient();
        var response = await client.PostAsJsonAsync("/api/password", MakeRequest("userNotFound"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var result = await ReadResultAsync(response);
        Assert.Contains(result!.Errors, e => e.ErrorCode == ApiErrorCode.UserNotFound);
    }

    [Fact]
    public async Task Post_ChangeNotPermittedMagicUser_ReturnsChangeNotPermittedErrorCode()
    {
        using var client = NewClient();
        var response = await client.PostAsJsonAsync("/api/password", MakeRequest("changeNotPermitted"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var result = await ReadResultAsync(response);
        Assert.Contains(result!.Errors, e => e.ErrorCode == ApiErrorCode.ChangeNotPermitted);
    }

    [Fact]
    public async Task Post_MissingRequiredFields_Returns400WithFieldRequired()
    {
        using var client = NewClient();
        var bad = new ChangePasswordModel
        {
            Username          = "alice",
            CurrentPassword   = string.Empty,
            NewPassword       = "NewPass1!",
            NewPasswordVerify = "NewPass1!",
        };
        var response = await client.PostAsJsonAsync("/api/password", bad);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.Contains("FieldRequired", raw);
    }

    [Fact]
    public async Task Post_NewPasswordMismatch_Returns400WithFieldMismatch()
    {
        using var client = NewClient();
        var bad = new ChangePasswordModel
        {
            Username          = "alice",
            CurrentPassword   = "old",
            NewPassword       = "NewPass1!",
            NewPasswordVerify = "Different!",
        };
        var response = await client.PostAsJsonAsync("/api/password", bad);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.Contains("FieldMismatch", raw);
    }

    /// <summary>
    /// Development-environment fixture that boots <c>Program</c> with
    /// <c>WebSettings:UseDebugProvider=true</c> so the in-process debug provider is wired.
    /// </summary>
    public sealed class DebugFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["WebSettings:UseDebugProvider"]                          = "true",
                    ["WebSettings:EnableHttpsRedirect"]                       = "false",
                    ["ClientSettings:MinimumDistance"]                        = "0",
                    ["ClientSettings:Recaptcha:Enabled"]                      = "false",
                    ["EmailNotificationSettings:Enabled"]                     = "false",
                    ["PasswordExpiryNotificationSettings:Enabled"]            = "false",
                    ["SiemSettings:Syslog:Enabled"]                           = "false",
                    ["SiemSettings:AlertEmail:Enabled"]                       = "false",
                    ["PasswordChangeOptions:PortalLockoutThreshold"]          = "0",
                    ["PasswordChangeOptions:UseAutomaticContext"]             = "true",
                });
            });
        }
    }
}
