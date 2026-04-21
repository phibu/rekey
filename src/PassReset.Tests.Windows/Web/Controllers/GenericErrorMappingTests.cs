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

namespace PassReset.Tests.Windows.Web.Controllers;

/// <summary>
/// STAB-013 — Proves that POST /api/password collapses account-enumeration error codes
/// (<see cref="ApiErrorCode.InvalidCredentials"/> and <see cref="ApiErrorCode.UserNotFound"/>)
/// to <see cref="ApiErrorCode.Generic"/> (0) on the wire in the Production environment,
/// while preserving granular codes in Development (D-03 regression guard) and preserving
/// non-auth codes (e.g. <see cref="ApiErrorCode.ChangeNotPermitted"/>) in every environment.
///
/// WR-02 (code review): Also asserts the D-05 invariant — when the wire response is
/// redacted to <c>Generic</c>, the SIEM event must still record the granular event
/// type (<c>InvalidCredentials</c> / <c>UserNotFound</c>) so SOC operators can triage
/// from syslog alone.
/// </summary>
public class GenericErrorMappingTests : IDisposable
{
    /// <summary>
    /// WR-02: test-double ISiemService that records every <see cref="SiemEventType"/>
    /// it sees so tests can assert the D-05 invariant (wire collapses, SIEM stays granular).
    /// </summary>
    public sealed class RecordingSiemService : ISiemService
    {
        public List<SiemEventType> Events { get; } = new();
        public void LogEvent(SiemEventType eventType, string username, string ipAddress, string? detail = null)
            => Events.Add(eventType);
        public void LogEvent(AuditEvent evt) => Events.Add(evt.EventType);
    }
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
    /// WR-02: replace the default SIEM service with a recorder so tests can assert
    /// which <see cref="SiemEventType"/> values the controller emitted.
    /// </summary>
    private static void SwapInRecordingSiem(IServiceCollection services, RecordingSiemService recorder)
    {
        var descriptors = services.Where(d => d.ServiceType == typeof(ISiemService)).ToList();
        foreach (var d in descriptors) services.Remove(d);
        services.AddSingleton<ISiemService>(recorder);
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
    /// WR-02: Production env + recording SIEM. Used to assert the D-05 invariant
    /// that SIEM stays granular while the wire collapses.
    /// </summary>
    public sealed class ProductionEnvFactoryWithRecorder : WebApplicationFactory<Program>
    {
        public RecordingSiemService Recorder { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(TestConfig());
            });
            builder.ConfigureTestServices(services =>
            {
                SwapInDebugProvider(services);
                SwapInRecordingSiem(services, Recorder);
            });
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

    /// <summary>
    /// WR-02 regression guard (D-05 invariant): when the wire collapses
    /// InvalidCredentials to Generic in Production, the SIEM event must still
    /// carry the granular <see cref="SiemEventType.InvalidCredentials"/> so
    /// SOC operators can triage from syslog alone. A bug that collapsed BOTH
    /// wire and SIEM (a plausible refactor mistake) would silently pass the
    /// existing four tests — this one catches it.
    /// </summary>
    [Fact]
    public async Task Production_InvalidCredentials_SiemRemainsGranular()
    {
        using var factory = new ProductionEnvFactoryWithRecorder();
        using var client  = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.PostAsJsonAsync("/api/password", MakeRequest("invalidCredentials"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var result = await ReadResultAsync(response);
        Assert.Equal(ApiErrorCode.Generic, result!.Errors[0].ErrorCode);

        // SIEM event must be the granular InvalidCredentials despite the wire collapse.
        Assert.Contains(SiemEventType.InvalidCredentials, factory.Recorder.Events);
        Assert.DoesNotContain(SiemEventType.Generic, factory.Recorder.Events);
    }

    /// <summary>
    /// WR-02 regression guard (D-05 invariant): same as the InvalidCredentials
    /// check but for the UserNotFound → Generic collapse path.
    /// </summary>
    [Fact]
    public async Task Production_UserNotFound_SiemRemainsGranular()
    {
        using var factory = new ProductionEnvFactoryWithRecorder();
        using var client  = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.PostAsJsonAsync("/api/password", MakeRequest("userNotFound"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var result = await ReadResultAsync(response);
        Assert.Equal(ApiErrorCode.Generic, result!.Errors[0].ErrorCode);

        Assert.Contains(SiemEventType.UserNotFound, factory.Recorder.Events);
        Assert.DoesNotContain(SiemEventType.Generic, factory.Recorder.Events);
    }
}
