using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PassReset.Common;
using PassReset.PasswordProvider;
using PassReset.Tests.Windows.Infrastructure;
using PassReset.Web.Models;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace PassReset.Tests.Windows.PasswordProvider;

/// <summary>
/// Sentinel-plaintext coverage: verifies that no password value (current or new)
/// appears in any rendered log message or structured property across the
/// controller → lockout decorator → provider code path. Covers the information
/// disclosure threat tracked as T-07-01.
///
/// <para>
/// <b>Coverage gap (documented, accepted for phase 07):</b> these tests exercise
/// the <c>FakeInvalidCredsProvider</c> stub and the end-to-end debug provider —
/// they do NOT drive the real <c>PasswordChangeProvider</c> catch blocks at
/// <c>ChangePasswordInternal</c> lines 485 / 505 / 515, <c>ValidateUserCredentials</c>
/// line 345, <c>ClearMustChangeFlag</c> line 466, or <c>ValidateGroups</c> lines
/// 377 / 386 that actually call <see cref="ExceptionChainLogger.LogExceptionChain"/>.
/// Driving those paths requires either a live <c>UserPrincipal</c> (impossible
/// without a domain controller) or refactoring <c>PasswordChangeProvider</c> to
/// allow fault injection via a swappable AD abstraction — significant rework
/// beyond phase 07 scope. The redaction safety of those sites rests on two
/// facts: (a) none of the real catch-block templates pass <c>currentPassword</c>
/// or <c>newPassword</c> as template arguments (verified by code review
/// WR-01/WR-02 deep-review pass), and (b)
/// <see cref="ExceptionChainLogger_CapturesInnerExceptionMessages_AcceptedRisk"/>
/// directly exercises the helper those sites invoke. Any future redaction layer
/// should hook <see cref="ExceptionChainLogger"/> centrally rather than relying
/// on call-site discipline.
/// </para>
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
    public async Task FakeProvider_DoesNotLogPlaintext()
    {
        var (sink, seri) = BuildSink();
        using var factory = new SerilogLoggerFactory(seri, dispose: true);

        var provider = new FakeInvalidCredsProvider(factory.CreateLogger<FakeInvalidCredsProvider>());
        var result = await provider.PerformPasswordChangeAsync("invalidCredentials", CurrentSentinel, NewSentinel);

        Assert.NotNull(result);
        AssertNoSentinels(sink);
    }

    [Fact]
    public async Task LockoutDecorator_DoesNotLogPlaintext_OverFakeInner()
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
    /// Directly exercises <see cref="ExceptionChainLogger.LogExceptionChain"/> with a
    /// synthesised inner-exception chain whose <see cref="Exception.Message"/> contains
    /// both sentinels. Asserts the sentinels DO reach the structured
    /// <c>ExceptionChain</c> property — proving the chain walker faithfully captures
    /// AD-surfaced error messages.
    ///
    /// <para>
    /// This documents an accepted-risk leakage channel: AD-supplied exception messages
    /// (e.g. <c>DirectoryServicesCOMException.Message</c>,
    /// <c>PasswordException.Message</c>) are the ONLY path through which provider-side
    /// log events could surface password-shaped text, and only if AD itself echoes the
    /// password back in an error (historically rare). Redaction of AD-message
    /// passthrough is out of scope for phase 07; any future redaction layer must hook
    /// <see cref="ExceptionChainLogger"/> rather than the call sites.
    /// </para>
    /// </summary>
    [Fact]
    public void ExceptionChainLogger_CapturesInnerExceptionMessages_AcceptedRisk()
    {
        var (sink, seri) = BuildSink();
        using var factory = new SerilogLoggerFactory(seri, dispose: true);
        var logger = factory.CreateLogger("ExceptionChainLoggerTest");

        // Build a 3-level inner chain whose messages carry both sentinels. This mimics
        // the worst-case where AD surfaces password-shaped text in an error string.
        var inner  = new InvalidOperationException($"inner-leak {CurrentSentinel}");
        var middle = new ApplicationException($"middle-leak {NewSentinel}", inner);
        var top    = new InvalidOperationException("top-level COM-ish failure", middle);

        ExceptionChainLogger.LogExceptionChain(logger, top,
            "ChangePassword failed (HRESULT={HResult})", unchecked((int)0x80070056));

        // Positive control: the chain walker MUST faithfully capture AD-supplied
        // message text — this is the documented accepted-risk channel.
        var allProps = string.Join("\n", sink.AllPropertyValues());
        Assert.Contains(CurrentSentinel, allProps);
        Assert.Contains(NewSentinel, allProps);
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
                    ["PasswordExpiryNotificationSettings:Enabled"] = "false",
                });
            });

            // Production Program.cs wires Serilog via builder.Host.UseSerilog(...), which
            // registers Serilog as the sole ILoggerProvider backed by Log.Logger and
            // bypasses the standard ILoggerFactory provider chain — so AddProvider(...) on
            // the services logging builder is ignored. The reliable override is to call
            // UseSerilog a second time on IWebHostBuilder with our capture-sink logger;
            // the last registration wins.
            var seri = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .Enrich.FromLogContext()
                .WriteTo.Sink(Sink)
                .CreateLogger();

            builder.ConfigureServices(services =>
            {
                // UseSerilog registers its own ILoggerFactory via services. Replace all
                // existing logging providers with a single Serilog provider backed by our
                // capture sink — this wins over any earlier UseSerilog registration
                // because it's registered last in the service collection.
                services.AddSingleton<Serilog.ILogger>(seri);
                services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(
                    new SerilogLoggerFactory(seri, dispose: true));
            });
        }
    }
}
