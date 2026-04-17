using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PassReset.Common;
using PassReset.PasswordProvider;
using PassReset.Web.Helpers;
using PassReset.Web.Models;
using PassReset.Web.Services;

namespace PassReset.Tests.Web.Controllers;

/// <summary>
/// STAB-013 — Proves that POST /api/password collapses account-enumeration error codes
/// (<see cref="ApiErrorCode.InvalidCredentials"/> and <see cref="ApiErrorCode.UserNotFound"/>)
/// to <see cref="ApiErrorCode.Generic"/> (0) on the wire in the Production environment,
/// while preserving granular codes in Development (D-03 regression guard) and preserving
/// non-auth codes (e.g. <see cref="ApiErrorCode.ChangeNotPermitted"/>) in every environment.
///
/// SIEM granularity is NOT covered here — see <c>SiemSyslogFormatterTests</c> and the
/// <c>Audit()</c> call site in <c>PasswordController.PostAsync</c> (D-05).
/// </summary>
public class GenericErrorMappingTests : IDisposable
{
    // Per-test factory disposal keeps rate-limiter partition state isolated and lets
    // individual tests flip the hosting environment without leaking across test methods.
    public void Dispose() => GC.SuppressFinalize(this);

    private static ChangePasswordModel MakeRequest(string username) => new()
    {
        Username          = username,
        CurrentPassword   = "OldPassword1!",
        NewPassword       = "BrandNewP@ssword123",
        NewPasswordVerify = "BrandNewP@ssword123",
        Recaptcha         = string.Empty,
    };

    // ApiResult/ApiErrorItem expose data via getter-only properties — System.Text.Json
    // cannot populate those on deserialization. Wire-shaped DTOs mirror the JSON contract.
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

    private static Dictionary<string, string?> TestConfig() => new()
    {
        // UseDebugProvider must be false in Production (Program.cs guard).
        // We instead inject DebugPasswordChangeProvider via ConfigureTestServices below.
        ["WebSettings:UseDebugProvider"]                 = "false",
        ["WebSettings:EnableHttpsRedirect"]              = "false",
        ["ClientSettings:MinimumDistance"]               = "0",
        ["ClientSettings:Recaptcha:Enabled"]             = "false",
        ["EmailNotificationSettings:Enabled"]            = "false",
        ["PasswordExpiryNotificationSettings:Enabled"]   = "false",
        ["SiemSettings:Syslog:Enabled"]                  = "false",
        ["SiemSettings:AlertEmail:Enabled"]              = "false",
        ["PasswordChangeOptions:PortalLockoutThreshold"] = "0",
        ["PasswordChangeOptions:UseAutomaticContext"]    = "true",
    };

    /// <summary>
    /// Swap the AD-bound provider chain for the debug provider so magic usernames
    /// (invalidCredentials/userNotFound/changeNotPermitted) produce deterministic
    /// ApiErrorCode responses without touching AD. This is needed because Program.cs
    /// refuses to boot with UseDebugProvider=true outside the Development environment.
    /// </summary>
    private static void SwapInDebugProvider(IServiceCollection services)
    {
        var descriptors = services.Where(d =>
            d.ServiceType == typeof(IPasswordChangeProvider) ||
            d.ServiceType == typeof(LockoutPasswordChangeProvider) ||
            d.ServiceType == typeof(PasswordChangeProvider) ||
            d.ServiceType == typeof(ILockoutDiagnostics) ||
            d.ServiceType == typeof(IEmailService)).ToList();
        foreach (var d in descriptors) services.Remove(d);

        services.AddSingleton<DebugPasswordChangeProvider>();
        services.AddSingleton<LockoutPasswordChangeProvider>(sp =>
            new LockoutPasswordChangeProvider(
                sp.GetRequiredService<DebugPasswordChangeProvider>(),
                sp.GetRequiredService<IOptions<PasswordChangeOptions>>(),
                sp.GetRequiredService<ILogger<LockoutPasswordChangeProvider>>()));
        services.AddSingleton<IPasswordChangeProvider>(sp =>
            sp.GetRequiredService<LockoutPasswordChangeProvider>());
        services.AddSingleton<ILockoutDiagnostics>(sp =>
            sp.GetRequiredService<LockoutPasswordChangeProvider>());
        services.AddSingleton<IEmailService, NoOpEmailService>();
    }

    /// <summary>
    /// Forces <c>IHostEnvironment.EnvironmentName == "Production"</c> so the STAB-013
    /// collapse gate fires.
    /// </summary>
    public sealed class ProductionEnvFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production"); // CRITICAL for STAB-013 gate
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(TestConfig());
            });
            builder.ConfigureTestServices(SwapInDebugProvider);
        }
    }

    /// <summary>
    /// Baseline development env — granular error codes must still reach the wire
    /// (D-03 locks env-based gate, no config flag).
    /// </summary>
    public sealed class DevelopmentEnvFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(TestConfig());
            });
            builder.ConfigureTestServices(SwapInDebugProvider);
        }
    }

    [Fact]
    public async Task Production_InvalidCredentials_WireReturnsGeneric()
    {
        using var factory = new ProductionEnvFactory();
        using var client  = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.PostAsJsonAsync("/api/password", MakeRequest("invalidCredentials"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var result = await ReadResultAsync(response);
        Assert.NotNull(result);
        Assert.Single(result!.Errors);
        Assert.Equal(ApiErrorCode.Generic, result.Errors[0].ErrorCode);
    }

    [Fact]
    public async Task Production_UserNotFound_WireReturnsGeneric()
    {
        using var factory = new ProductionEnvFactory();
        using var client  = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.PostAsJsonAsync("/api/password", MakeRequest("userNotFound"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var result = await ReadResultAsync(response);
        Assert.NotNull(result);
        Assert.Single(result!.Errors);
        Assert.Equal(ApiErrorCode.Generic, result.Errors[0].ErrorCode);
    }

    [Fact]
    public async Task Production_ChangeNotPermitted_WirePreservesCode()
    {
        using var factory = new ProductionEnvFactory();
        using var client  = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.PostAsJsonAsync("/api/password", MakeRequest("changeNotPermitted"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var result = await ReadResultAsync(response);
        Assert.NotNull(result);
        Assert.Single(result!.Errors);
        Assert.Equal(ApiErrorCode.ChangeNotPermitted, result.Errors[0].ErrorCode);
    }

    [Fact]
    public async Task Development_InvalidCredentials_WirePreservesCode()
    {
        using var factory = new DevelopmentEnvFactory();
        using var client  = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.PostAsJsonAsync("/api/password", MakeRequest("invalidCredentials"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var result = await ReadResultAsync(response);
        Assert.NotNull(result);
        Assert.Single(result!.Errors);
        Assert.Equal(ApiErrorCode.InvalidCredentials, result.Errors[0].ErrorCode);
    }
}
