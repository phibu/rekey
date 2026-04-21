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
/// STAB-014 — Integration coverage for the POST /api/password rate-limit and
/// reCAPTCHA branches. Each test spins up its own <see cref="WebApplicationFactory{TEntryPoint}"/>
/// subclass so the fixed-window rate-limiter partition is freshly constructed per test
/// (prevents the 5-req/5-min budget leaking across tests — see 09-RESEARCH.md Pitfall 1).
/// </summary>
public class RateLimitAndRecaptchaTests
{
    // ─── Wire DTOs (ApiResult/ApiErrorItem expose getter-only props — STJ can't
    // populate them on deserialization). Mirror of PasswordControllerTests shapes.
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

    private static ChangePasswordModel MakeRequest(string username) => new()
    {
        Username          = username,
        CurrentPassword   = "OldPassword1!",
        NewPassword       = "BrandNewP@ssword123",
        NewPasswordVerify = "BrandNewP@ssword123",
        Recaptcha         = string.Empty,
    };

    /// <summary>
    /// Default fixture — debug provider + reCAPTCHA disabled. Matches
    /// <c>PasswordControllerTests.DebugFactory</c> so rate-limit tests exercise
    /// the same config surface as the existing controller suite.
    /// </summary>
    public sealed class RateLimitFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["WebSettings:UseDebugProvider"]                 = "true",
                    ["WebSettings:EnableHttpsRedirect"]              = "false",
                    ["ClientSettings:MinimumDistance"]               = "0",
                    ["ClientSettings:Recaptcha:Enabled"]             = "false",
                    ["EmailNotificationSettings:Enabled"]            = "false",
                    ["PasswordExpiryNotificationSettings:Enabled"]   = "false",
                    ["SiemSettings:Syslog:Enabled"]                  = "false",
                    ["SiemSettings:AlertEmail:Enabled"]              = "false",
                    ["PasswordChangeOptions:PortalLockoutThreshold"] = "0",
                    ["PasswordChangeOptions:UseAutomaticContext"]    = "true",
                });
            });
        }
    }

    /// <summary>
    /// STAB-014(c) D-19: reCAPTCHA-enabled path. Hits real Google siteverify with
    /// an invalid token — see 09-CONTEXT.md §"D-19".
    /// </summary>
    public sealed class RecaptchaEnabledFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["WebSettings:UseDebugProvider"]                   = "true",
                    ["WebSettings:EnableHttpsRedirect"]                = "false",
                    ["ClientSettings:MinimumDistance"]                 = "0",
                    // Google's publicly documented always-fail test secret — see
                    // https://developers.google.com/recaptcha/docs/faq#id-like-to-run-automated-tests-with-recaptcha
                    ["ClientSettings:Recaptcha:Enabled"]               = "true",
                    // Google's public test site key — non-empty, required by the
                    // SiteKey validator even though the client-side widget isn't invoked here.
                    ["ClientSettings:Recaptcha:SiteKey"]               = "6LeIxAcTAAAAAJcZVRqyHh71UMIEGNQ_MXjiZKhI",
                    ["ClientSettings:Recaptcha:PrivateKey"]            = "6LeIxAcTAAAAAGG-vFI1TnRWxMZNFuojJ4WifJWe",
                    ["ClientSettings:Recaptcha:FailOpenOnUnavailable"] = "false",
                    ["EmailNotificationSettings:Enabled"]              = "false",
                    ["PasswordExpiryNotificationSettings:Enabled"]     = "false",
                    ["SiemSettings:Syslog:Enabled"]                    = "false",
                    ["SiemSettings:AlertEmail:Enabled"]                = "false",
                    ["PasswordChangeOptions:PortalLockoutThreshold"]   = "0",
                    ["PasswordChangeOptions:UseAutomaticContext"]      = "true",
                });
            });
        }
    }

    // ─── Rate-limit coverage ──────────────────────────────────────────────────

    [Fact]
    public async Task RateLimit_SixthRequestInWindow_Returns429()
    {
        using var factory = new RateLimitFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        // First 5 POSTs are inside the 5-req/5-min fixed window — all succeed.
        for (int i = 0; i < 5; i++)
        {
            var response = await client.PostAsJsonAsync("/api/password", MakeRequest("alice"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // 6th POST exhausts the window — rate limiter emits 429 before controller executes.
        var rejected = await client.PostAsJsonAsync("/api/password", MakeRequest("alice"));
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    [Fact]
    public async Task RateLimit_FirstRequestInFreshFactory_Succeeds()
    {
        // Explicit regression guard for Pitfall 1 (09-RESEARCH.md): per-test factory
        // instances must produce independent rate-limit partitions. A fresh factory
        // after the saturation test above MUST still accept its first request.
        using var factory = new RateLimitFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.PostAsJsonAsync("/api/password", MakeRequest("alice"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ─── reCAPTCHA coverage ───────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "RequiresInternet")]
    public async Task Recaptcha_EnabledWithInvalidToken_ReturnsInvalidCaptcha()
    {
        // D-19: no abstraction — hit real Google siteverify. The configured private key
        // is Google's documented always-fail test secret so the response is deterministic.
        using var factory = new RecaptchaEnabledFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var req = MakeRequest("alice");
        req.Recaptcha = "ThisTokenWillNotVerify";

        var response = await client.PostAsJsonAsync("/api/password", req);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var result = await ReadResultAsync(response);
        Assert.NotNull(result);
        Assert.Contains(result!.Errors, e => e.ErrorCode == ApiErrorCode.InvalidCaptcha);
    }

    [Fact]
    public async Task Recaptcha_DisabledWithEmptyToken_RequestProceeds()
    {
        // With Recaptcha:Enabled=false, the controller's reCAPTCHA branch is skipped
        // regardless of whether the Recaptcha field is empty. This test documents
        // that guarantee explicitly so a regression (e.g., accidentally gating on
        // empty token) surfaces with a clear name.
        using var factory = new RateLimitFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.PostAsJsonAsync("/api/password", MakeRequest("alice"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
