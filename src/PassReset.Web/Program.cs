using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using PassReset.Common;
using PassReset.PasswordProvider;
using PassReset.PasswordProvider.Ldap;
using PassReset.Web.Helpers;
using PassReset.Web.Middleware;
using PassReset.Web.Models;
using PassReset.Web.Services;
using Serilog;
// NoOpEmailService lives in PassReset.Web.Helpers (development no-op).
// SmtpEmailService and PasswordExpiryNotificationService live in PassReset.Web.Services.

// Bootstrap logger captures startup failures before appsettings-driven config is loaded.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting PassReset");

    var builder = WebApplication.CreateBuilder(args);

    // ─── Serilog (reads Serilog section from appsettings; enrich with HTTP context) ─
    // preserveStaticLogger: true keeps the bootstrap logger independent of the host's
    // pipeline logger. Required for WebApplicationFactory<Program> test re-entry —
    // without it the static ReloadableLogger from a prior test run is "already frozen"
    // when UseSerilog tries to replace it on the next run, producing
    // InvalidOperationException: "The logger is already frozen."
    builder.Host.UseSerilog(
        (ctx, services, lc) => lc
            .ReadFrom.Configuration(ctx.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext(),
        preserveStaticLogger: true);

    // ─── Configuration — AddOptions<T>().Bind().ValidateOnStart() per D-07 ────────
    // Each validator is registered adjacent to its AddOptions call so failure surfaces
    // as OptionsValidationException at DI build → StartupValidationFailureLogger writes
    // to the Windows Application Event Log under source 'PassReset' before IIS returns 502.
    builder.Services.AddOptions<ClientSettings>()
        .Bind(builder.Configuration.GetSection(nameof(ClientSettings)))
        .ValidateOnStart();
    builder.Services.AddSingleton<IValidateOptions<ClientSettings>, ClientSettingsValidator>();

    builder.Services.AddOptions<WebSettings>()
        .Bind(builder.Configuration.GetSection(nameof(WebSettings)))
        .ValidateOnStart();
    builder.Services.AddSingleton<IValidateOptions<WebSettings>, WebSettingsValidator>();

    builder.Services.AddOptions<SmtpSettings>()
        .Bind(builder.Configuration.GetSection(nameof(SmtpSettings)))
        .ValidateOnStart();
    builder.Services.AddSingleton<IValidateOptions<SmtpSettings>, SmtpSettingsValidator>();

    builder.Services.AddOptions<EmailNotificationSettings>()
        .Bind(builder.Configuration.GetSection(nameof(EmailNotificationSettings)))
        .ValidateOnStart();
    builder.Services.AddSingleton<IValidateOptions<EmailNotificationSettings>, EmailNotificationSettingsValidator>();

    builder.Services.AddOptions<PasswordExpiryNotificationSettings>()
        .Bind(builder.Configuration.GetSection(nameof(PasswordExpiryNotificationSettings)))
        .ValidateOnStart();
    builder.Services.AddSingleton<IValidateOptions<PasswordExpiryNotificationSettings>, PasswordExpiryNotificationSettingsValidator>();

    builder.Services.AddOptions<SiemSettings>()
        .Bind(builder.Configuration.GetSection(nameof(SiemSettings)))
        .ValidateOnStart();
    builder.Services.AddSingleton<IValidateOptions<SiemSettings>, SiemSettingsValidator>();

    builder.Services.AddOptions<PasswordChangeOptions>()
        .Bind(builder.Configuration.GetSection(nameof(PasswordChangeOptions)))
        .ValidateOnStart();
    builder.Services.AddSingleton<IValidateOptions<PasswordChangeOptions>, PasswordChangeOptionsValidator>();

    // ─── PwnedPasswordChecker — HttpClient injected via IHttpClientFactory ─────
    // Registered against both the concrete type (for PasswordChangeProvider's existing
    // constructor dependency) and the IPwnedPasswordChecker interface (for the new
    // pwned-check controller endpoint — FEAT-004).
    builder.Services.AddHttpClient<PwnedPasswordChecker>(c =>
    {
        c.BaseAddress = new Uri("https://api.pwnedpasswords.com/");
        c.Timeout = TimeSpan.FromSeconds(5);
    });
    builder.Services.AddTransient<IPwnedPasswordChecker>(sp =>
        sp.GetRequiredService<PwnedPasswordChecker>());

    // ─── Provider registration (runtime config flag, no compile-time conditionals) ─
    var webSettings = builder.Configuration
        .GetSection(nameof(WebSettings))
        .Get<WebSettings>() ?? new WebSettings();

    var expirySettings = builder.Configuration
        .GetSection(nameof(PasswordExpiryNotificationSettings))
        .Get<PasswordExpiryNotificationSettings>() ?? new PasswordExpiryNotificationSettings();

    // ─── Startup configuration validation — abort on Error, warn on Warning ──
    var smtpSettings = builder.Configuration
        .GetSection(nameof(SmtpSettings))
        .Get<SmtpSettings>() ?? new SmtpSettings();
    var emailNotifSettings = builder.Configuration
        .GetSection(nameof(EmailNotificationSettings))
        .Get<EmailNotificationSettings>() ?? new EmailNotificationSettings();
    var clientSettings = builder.Configuration
        .GetSection(nameof(ClientSettings))
        .Get<ClientSettings>() ?? new ClientSettings();
    var passwordChangeOptions = builder.Configuration
        .GetSection(nameof(PasswordChangeOptions))
        .Get<PasswordChangeOptions>() ?? new PasswordChangeOptions();

    // ERROR — abort startup
    if (webSettings.UseDebugProvider && !builder.Environment.IsDevelopment())
        throw new InvalidOperationException(
            "WebSettings.UseDebugProvider is true but Environment is not 'Development'. " +
            "Set UseDebugProvider to false or run in the Development environment.");

    if (clientSettings.Recaptcha?.Enabled == true
        && string.IsNullOrWhiteSpace(clientSettings.Recaptcha.PrivateKey))
        throw new InvalidOperationException(
            "Recaptcha.Enabled is true but Recaptcha.PrivateKey is empty. " +
            "Provide a valid reCAPTCHA private key or disable reCAPTCHA.");

    // WARNING — log and continue
    if (emailNotifSettings.Enabled && string.IsNullOrWhiteSpace(smtpSettings.Host))
        Log.Warning(
            "EmailNotificationSettings.Enabled is true but SmtpSettings.Host is empty. " +
            "Password-changed notification emails will be silently discarded.");

    if (expirySettings.Enabled && string.IsNullOrWhiteSpace(smtpSettings.Host))
        Log.Warning(
            "PasswordExpiryNotificationSettings.Enabled is true but SmtpSettings.Host is empty. " +
            "Expiry notification emails will be silently discarded.");

    if (passwordChangeOptions.PortalLockoutThreshold >= 10)
        Log.Warning(
            "PortalLockoutThreshold is {Threshold} (>= 10). This is unusually high and may indicate a misconfiguration. Typical values are 3-5.",
            passwordChangeOptions.PortalLockoutThreshold);

    // Phase 11: ProviderMode-based selection (Auto | Windows | Ldap).
    // Note: ProviderMode is captured at startup from a configuration snapshot; changing it
    // requires an app restart. DI registrations are resolved once and will not respond to
    // IOptionsMonitor reloads of PasswordChangeOptions.
    var effectiveProvider = passwordChangeOptions.ProviderMode switch
    {
        ProviderMode.Windows => WiringTarget.Windows,
        ProviderMode.Ldap    => WiringTarget.Ldap,
        ProviderMode.Auto    => OperatingSystem.IsWindows() ? WiringTarget.Windows : WiringTarget.Ldap,
        _                    => throw new InvalidOperationException(
                                    $"Unknown PasswordChangeOptions.ProviderMode: {passwordChangeOptions.ProviderMode}"),
    };

    if (webSettings.UseDebugProvider)
    {
        builder.Services.AddSingleton<DebugPasswordChangeProvider>();
        builder.Services.AddSingleton<LockoutPasswordChangeProvider>(sp =>
            new LockoutPasswordChangeProvider(
                sp.GetRequiredService<DebugPasswordChangeProvider>(),
                sp.GetRequiredService<IOptions<PasswordChangeOptions>>(),
                sp.GetRequiredService<ILogger<LockoutPasswordChangeProvider>>()));
        builder.Services.AddSingleton<IEmailService, NoOpEmailService>();
        // Expiry service is never wired in debug mode — diagnostics report "not-enabled".
        builder.Services.AddSingleton<IExpiryServiceDiagnostics>(new NullExpiryServiceDiagnostics());
        // Health probe — LDAP TCP probe is cross-platform; returns NotConfigured when LdapHostnames empty.
        builder.Services.AddSingleton<IAdConnectivityProbe, LdapTcpProbe>();
    }
    else if (effectiveProvider == WiringTarget.Ldap)
    {
        // Session factory per password-change request (no pooling — low frequency).
        builder.Services.AddSingleton<Func<ILdapSession>>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<PasswordChangeOptions>>().Value;
            // Belt-and-suspenders: PasswordChangeOptionsValidator already enforces non-empty
            // LdapHostnames when ProviderMode resolves to Ldap at startup. This defensive
            // check surfaces a clear, actionable error if the options are ever reloaded
            // into an invalid state before the factory fires.
            if (opts.LdapHostnames is null || opts.LdapHostnames.Length == 0)
            {
                throw new InvalidOperationException(
                    "PasswordChangeOptions.LdapHostnames must contain at least one hostname when ProviderMode=Ldap.");
            }
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            return () => new LdapSession(
                hostname: opts.LdapHostnames[0],
                port: opts.LdapPort,
                useLdaps: opts.LdapUseSsl,
                serviceAccountDn: opts.ServiceAccountDn,
                serviceAccountPassword: opts.ServiceAccountPassword,
                trustedThumbprints: opts.LdapTrustedCertificateThumbprints,
                logger: loggerFactory.CreateLogger<LdapSession>());
        });
        builder.Services.AddSingleton<LdapPasswordChangeProvider>();
        builder.Services.AddSingleton<LockoutPasswordChangeProvider>(sp =>
            new LockoutPasswordChangeProvider(
                sp.GetRequiredService<LdapPasswordChangeProvider>(),
                sp.GetRequiredService<IOptions<PasswordChangeOptions>>(),
                sp.GetRequiredService<ILogger<LockoutPasswordChangeProvider>>()));
        builder.Services.AddTransient<IEmailService, SmtpEmailService>();
        // Health probe — cross-platform LDAP TCP probe.
        builder.Services.AddSingleton<IAdConnectivityProbe, LdapTcpProbe>();

        if (expirySettings.Enabled)
        {
            builder.Services.AddSingleton<PasswordExpiryNotificationService>();
            builder.Services.AddHostedService(sp => sp.GetRequiredService<PasswordExpiryNotificationService>());
            builder.Services.AddSingleton<IExpiryServiceDiagnostics>(sp =>
                sp.GetRequiredService<PasswordExpiryNotificationService>());
        }
        else
        {
            builder.Services.AddSingleton<IExpiryServiceDiagnostics>(new NullExpiryServiceDiagnostics());
        }
    }
#if WINDOWS_PROVIDER
    else  // effectiveProvider == WiringTarget.Windows
    {
        builder.Services.AddSingleton<PassReset.PasswordProvider.IPrincipalContextFactory,
                                      PassReset.PasswordProvider.DefaultPrincipalContextFactory>();
        builder.Services.AddSingleton<PasswordChangeProvider>();
        builder.Services.AddSingleton<LockoutPasswordChangeProvider>(sp =>
            new LockoutPasswordChangeProvider(
                sp.GetRequiredService<PasswordChangeProvider>(),
                sp.GetRequiredService<IOptions<PasswordChangeOptions>>(),
                sp.GetRequiredService<ILogger<LockoutPasswordChangeProvider>>()));
        builder.Services.AddTransient<IEmailService, SmtpEmailService>();
        // Health probe — Windows domain-joined PrincipalContext check.
        builder.Services.AddSingleton<IAdConnectivityProbe, PassReset.PasswordProvider.DomainJoinedProbe>();

        if (expirySettings.Enabled)
        {
            // Register as singleton so both the hosted service runtime and the health
            // controller's IExpiryServiceDiagnostics dependency resolve the SAME instance.
            builder.Services.AddSingleton<PasswordExpiryNotificationService>();
            builder.Services.AddHostedService(sp => sp.GetRequiredService<PasswordExpiryNotificationService>());
            builder.Services.AddSingleton<IExpiryServiceDiagnostics>(sp =>
                sp.GetRequiredService<PasswordExpiryNotificationService>());
        }
        else
        {
            builder.Services.AddSingleton<IExpiryServiceDiagnostics>(new NullExpiryServiceDiagnostics());
        }
    }
#else
    else
    {
        throw new InvalidOperationException(
            "PasswordChangeOptions.ProviderMode resolved to Windows, but this build does not include the Windows provider. " +
            "Rebuild on Windows or set ProviderMode to Ldap.");
    }
#endif

    builder.Services.AddSingleton<IPasswordChangeProvider>(sp =>
        sp.GetRequiredService<LockoutPasswordChangeProvider>());
    builder.Services.AddSingleton<ILockoutDiagnostics>(sp =>
        sp.GetRequiredService<LockoutPasswordChangeProvider>());

    // ─── SIEM service ─────────────────────────────────────────────────────────────
    builder.Services.AddSingleton<ISiemService, SiemService>();

    // ─── AD password-policy cache (FEAT-002) ─────────────────────────────────────
    builder.Services.AddMemoryCache();
    builder.Services.AddSingleton<PasswordPolicyCache>();

    // ─── Rate limiting (built-in .NET 7+ API, no third-party dependency) ──────────
    // Policy names used by the [EnableRateLimiting] attributes on PasswordController.
    const string PasswordRateLimitPolicy = "password-fixed-window";
    const string PwnedCheckRateLimitPolicy = "pwned-check-window";

    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        options.OnRejected = (context, _) =>
        {
            var siem = context.HttpContext.RequestServices.GetService<ISiemService>();
            var ip   = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            siem?.LogEvent(SiemEventType.RateLimitExceeded, "unknown", ip);
            return ValueTask.CompletedTask;
        };

        options.AddPolicy(PasswordRateLimitPolicy, context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit          = 5,
                    Window               = TimeSpan.FromMinutes(5),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit           = 0,  // no queuing — reject immediately
                }));

        // FEAT-004: dedicated policy for the blur-triggered HIBP pre-check so a
        // user typing several candidate passwords does not exhaust the submit-time
        // 5-per-5-min budget. 20 per 5 min is enough for realistic exploration while
        // still throttling abuse of the server-side HIBP proxy.
        options.AddPolicy(PwnedCheckRateLimitPolicy, context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit          = 20,
                    Window               = TimeSpan.FromMinutes(5),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit           = 0,
                }));
    });

    // ─── MVC / API ────────────────────────────────────────────────────────────────
    builder.Services.AddControllers();

    // ─── Build app ────────────────────────────────────────────────────────────────
    var app = builder.Build();

    // ─── Request logging — one structured line per HTTP request ───────────────────
    app.UseSerilogRequestLogging();

    // ─── TraceId / SpanId enrichment — pushes W3C Activity identifiers onto
    //     Serilog's LogContext so every downstream log event correlates per request.
    app.UseMiddleware<TraceIdEnricherMiddleware>();

    // ─── Security headers — applied to every response before any other middleware ─
    app.Use(async (context, next) =>
    {
        var runtimeWeb = context.RequestServices.GetRequiredService<IOptions<WebSettings>>().Value;
        var headers = context.Response.Headers;
        headers["X-Frame-Options"]         = "DENY";
        headers["X-Content-Type-Options"]  = "nosniff";
        headers["Referrer-Policy"]         = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"]      = "geolocation=(), microphone=(), camera=()";
        headers["Content-Security-Policy"] =
            "default-src 'self'; " +
            "script-src 'self' https://www.google.com/recaptcha/ https://www.gstatic.com/recaptcha/; " +
            "style-src 'self' 'unsafe-inline'; " +
            "frame-src https://www.google.com/recaptcha/ https://recaptcha.google.com/recaptcha/; " +
            "img-src 'self' data:; " +
            "connect-src 'self'; " +
            "base-uri 'self'; " +
            "form-action 'self'; " +
            "object-src 'none'";

        if (runtimeWeb.EnableHttpsRedirect)
            headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";

        await next(context);
    });

    // ─── HTTPS redirect ───────────────────────────────────────────────────────────
    if (webSettings.EnableHttpsRedirect)
        app.UseHttpsRedirection();

    // ─── Static files and routing ─────────────────────────────────────────────────
    app.UseDefaultFiles();
    app.UseStaticFiles();

    // ─── Operator branding assets (FEAT-001) ─────────────────────────────────────
    // Served from C:\ProgramData\PassReset\brand\ by default — upgrade-safe path
    // owned by the operator, not by the app deploy directory.
    var brandRoot = clientSettings.Branding?.AssetRoot
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "PassReset", "brand");
    Directory.CreateDirectory(brandRoot);
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(brandRoot),
        RequestPath = "/brand",
        ServeUnknownFileTypes = false,
    });

    app.UseRouting();

    // ─── Rate limiting — must come after UseRouting so endpoint metadata is resolved ─
    app.UseRateLimiter();

    app.MapControllers();

    // SPA fallback — serves index.html for non-API, non-file routes so deep links work.
    app.MapFallbackToFile("index.html");

    app.Run();
    return 0;
}
catch (OptionsValidationException ex)
{
    // D-07: operator-actionable diagnosis path for misconfigured appsettings.
    // Write to Windows Application Event Log (source 'PassReset') so operators see
    // the validation failure in Event Viewer, not just a bare IIS 502. Source
    // registration is owned by Install-PassReset.ps1 (plan 08-04); missing source
    // is silently swallowed inside the helper. After logging, re-throw so the
    // ASP.NET Core module / WebApplicationFactory observes the original failure.
    StartupValidationFailureLogger.LogToEventLog(ex);
    Log.Fatal(ex, "PassReset configuration validation failed at startup: {Failures}",
        string.Join(" | ", ex.Failures ?? []));
    throw;
}
// NOTE: Do NOT add a broad `catch (Exception)` or a `finally { Log.CloseAndFlush(); }`
// here. WebApplicationFactory<Program> / HostFactoryResolver re-enters this top-level
// program across multiple tests in a single process and signals its handoff with an
// internal exception (StopTheHostException) that MUST propagate. A broad catch swallows
// that signal and breaks subsequent tests with "entry point exited without ever building
// an IHost". The host owns Serilog's lifetime via UseSerilog(); the process shutdown
// path flushes the logger — we do not need CloseAndFlush here.

// Marker type to allow WebApplicationFactory<Program> in test projects.
// Top-level programs generate an internal Program class; this makes it public.
public partial class Program { }

// Phase 11: compile-time wiring target resolved from PasswordChangeOptions.ProviderMode.
internal enum WiringTarget { Windows, Ldap }
