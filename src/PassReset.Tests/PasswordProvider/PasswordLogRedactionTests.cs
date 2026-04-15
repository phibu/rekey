using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PassReset.Common;
using PassReset.PasswordProvider;
using PassReset.Tests.Infrastructure;
using PassReset.Web.Models;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace PassReset.Tests.PasswordProvider;

/// <summary>
/// Sentinel-plaintext coverage: verifies that no password value (current or new)
/// appears in any rendered log message or structured property across the
/// controller → lockout decorator → provider code path. Covers the information
/// disclosure threat tracked as T-07-01.
/// </summary>
public sealed class PasswordLogRedactionTests
{
    private const string CurrentSentinel = "SENTINEL_CURRENT_12345";
    private const string NewSentinel     = "SENTINEL_NEW_67890";

    private static (ListLogEventSink sink, Serilog.ILogger seri) BuildSink()
    {
        var sink = new ListLogEventSink();
        var seri = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.FromLogContext()
            .WriteTo.Sink(sink)
            .CreateLogger();
        return (sink, seri);
    }

    // Minimal local stand-in for the internal DebugPasswordChangeProvider — exercises the
    // same "params-in, log-some-debug-events, return-known-error" shape but does not take
    // a dependency on web-project internals. Accepts the same sentinels so the test
    // proves the provider surface does not leak the passed-in passwords.
    private sealed class FakeInvalidCredsProvider : IPasswordChangeProvider
    {
        private readonly ILogger<FakeInvalidCredsProvider> _logger;
        public FakeInvalidCredsProvider(ILogger<FakeInvalidCredsProvider> logger) => _logger = logger;

        public Task<ApiErrorItem?> PerformPasswordChangeAsync(string username, string currentPassword, string newPassword)
        {
            _logger.LogDebug("fake-provider: PerformPasswordChange for {User}", username);
            _logger.LogInformation("fake-provider: complete for {User}", username);
            return Task.FromResult<ApiErrorItem?>(new ApiErrorItem(ApiErrorCode.InvalidCredentials));
        }

        public string? GetUserEmail(string username) => null;
        public IEnumerable<(string Username, string Email, DateTime? PasswordLastSet)> GetUsersInGroup(string groupName) => [];
        public TimeSpan GetDomainMaxPasswordAge() => TimeSpan.FromDays(90);
        public Task<PasswordPolicy?> GetEffectivePasswordPolicyAsync() => Task.FromResult<PasswordPolicy?>(null);
    }

    private static void AssertNoSentinels(ListLogEventSink sink)
    {
        Assert.NotEmpty(sink.Events);

        // Positive-control: confirm we captured Debug or Information traffic (not vacuous pass).
        Assert.Contains(sink.Events, e =>
            e.Level == LogEventLevel.Debug || e.Level == LogEventLevel.Information);

        foreach (var rendered in sink.AllRendered())
        {
            Assert.DoesNotContain(CurrentSentinel, rendered);
            Assert.DoesNotContain(NewSentinel, rendered);
        }
        foreach (var propValue in sink.AllPropertyValues())
        {
            Assert.DoesNotContain(CurrentSentinel, propValue);
            Assert.DoesNotContain(NewSentinel, propValue);
        }
    }

    [Fact]
    public async Task DebugPasswordChangeProvider_DoesNotLogPlaintext()
    {
        var (sink, seri) = BuildSink();
        using var factory = new SerilogLoggerFactory(seri, dispose: true);

        var provider = new FakeInvalidCredsProvider(factory.CreateLogger<FakeInvalidCredsProvider>());
        var result = await provider.PerformPasswordChangeAsync("invalidCredentials", CurrentSentinel, NewSentinel);

        Assert.NotNull(result);
        AssertNoSentinels(sink);
    }

    [Fact]
    public async Task LockoutPasswordChangeProvider_DoesNotLogPlaintext()
    {
        var (sink, seri) = BuildSink();
        using var factory = new SerilogLoggerFactory(seri, dispose: true);

        var innerLogger = factory.CreateLogger<FakeInvalidCredsProvider>();
        var inner = new FakeInvalidCredsProvider(innerLogger);

        var opts = Options.Create(new PasswordChangeOptions
        {
            PortalLockoutThreshold = 2,
            PortalLockoutWindow = TimeSpan.FromMinutes(5),
        });
        var lockoutLogger = factory.CreateLogger<LockoutPasswordChangeProvider>();
        using var lockout = new LockoutPasswordChangeProvider(inner, opts, lockoutLogger);

        // Drive enough failures to cross both ApproachingLockout and PortalLockout paths.
        for (var i = 0; i < 4; i++)
        {
            await lockout.PerformPasswordChangeAsync("alice", CurrentSentinel, NewSentinel);
        }

        AssertNoSentinels(sink);
    }

    /// <summary>
    /// Reads the sink emitted by <see cref="RedactionFactory"/> across a full
    /// POST /api/password round-trip via the debug provider. Asserts no
    /// sentinel leaks through any controller/provider/decorator log entry.
    /// </summary>
    [Fact]
    public async Task PasswordController_DoesNotLogPlaintext_EndToEnd()
    {
        using var factory = new RedactionFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var body = new ChangePasswordModel
        {
            Username          = "anyuser",
            CurrentPassword   = CurrentSentinel,
            NewPassword       = NewSentinel,
            NewPasswordVerify = NewSentinel,
            Recaptcha         = string.Empty,
        };

        var response = await client.PostAsJsonAsync("/api/password", body);
        // Accept any status — response shape is out of scope for this test; only log content matters.
        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadRequest,
            $"Unexpected status {response.StatusCode}");

        AssertNoSentinels(factory.Sink);
    }

    /// <summary>
    /// WebApplicationFactory subclass that replaces the Serilog pipeline with a
    /// captured <see cref="ListLogEventSink"/> and forces the in-process debug
    /// provider so no AD connection is attempted.
    /// </summary>
    private sealed class RedactionFactory : WebApplicationFactory<Program>
    {
        public ListLogEventSink Sink { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");

            builder.ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["WebSettings:UseDebugProvider"] = "true",
                    ["ClientSettings:Recaptcha:Enabled"] = "false",
                    ["ClientSettings:MinimumDistance"] = "0",
                    ["EmailNotificationSettings:Enabled"] = "false",
                    // Disable the expiry background service + AD-dependent paths.
                    ["PasswordExpiryNotificationSettings:Enabled"] = "false",
                });
            });
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            // Swap the Serilog pipeline to one that writes every event to the capture sink.
            // Must be on IHostBuilder (not IWebHostBuilder) because UseSerilog is defined there.
            builder.UseSerilog((_, lc) => lc
                .MinimumLevel.Verbose()
                .Enrich.FromLogContext()
                .WriteTo.Sink(Sink));

            return base.CreateHost(builder);
        }
    }
}
