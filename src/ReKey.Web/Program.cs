using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using ReKey.Common;
using ReKey.PasswordProvider;
using ReKey.Web.Helpers;
using ReKey.Web.Models;
using ReKey.Web.Services;
// NoOpEmailService lives in ReKey.Web.Helpers (development no-op).
// SmtpEmailService and PasswordExpiryNotificationService live in ReKey.Web.Services.

var builder = WebApplication.CreateBuilder(args);

// ─── Configuration ────────────────────────────────────────────────────────────
builder.Services.Configure<ClientSettings>(
    builder.Configuration.GetSection(nameof(ClientSettings)));
builder.Services.Configure<WebSettings>(
    builder.Configuration.GetSection(nameof(WebSettings)));
builder.Services.Configure<SmtpSettings>(
    builder.Configuration.GetSection(nameof(SmtpSettings)));
builder.Services.Configure<EmailNotificationSettings>(
    builder.Configuration.GetSection(nameof(EmailNotificationSettings)));
builder.Services.Configure<PasswordExpiryNotificationSettings>(
    builder.Configuration.GetSection(nameof(PasswordExpiryNotificationSettings)));
builder.Services.Configure<PasswordChangeOptions>(
    builder.Configuration.GetSection(nameof(PasswordChangeOptions)));

// ─── Provider registration (runtime config flag, no compile-time conditionals) ─
var webSettings = builder.Configuration
    .GetSection(nameof(WebSettings))
    .Get<WebSettings>() ?? new WebSettings();

var expirySettings = builder.Configuration
    .GetSection(nameof(PasswordExpiryNotificationSettings))
    .Get<PasswordExpiryNotificationSettings>() ?? new PasswordExpiryNotificationSettings();

if (webSettings.UseDebugProvider)
{
    builder.Services.AddSingleton<IPasswordChangeProvider, DebugPasswordChangeProvider>();
    builder.Services.AddSingleton<IEmailService, NoOpEmailService>();
}
else
{
    builder.Services.AddSingleton<IPasswordChangeProvider, PasswordChangeProvider>();
    builder.Services.AddTransient<IEmailService, SmtpEmailService>();

    if (expirySettings.Enabled)
        builder.Services.AddHostedService<PasswordExpiryNotificationService>();
}

// ─── Rate limiting (built-in .NET 7+ API, no third-party dependency) ──────────
// Policy name used by the [EnableRateLimiting] attribute on PasswordController.
const string PasswordRateLimitPolicy = "password-fixed-window";

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddFixedWindowLimiter(PasswordRateLimitPolicy, limiterOptions =>
    {
        limiterOptions.PermitLimit          = 5;
        limiterOptions.Window               = TimeSpan.FromMinutes(5);
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit           = 0;  // no queuing — reject immediately
    });
});

// ─── MVC / API ────────────────────────────────────────────────────────────────
builder.Services.AddControllers();

// ─── Build app ────────────────────────────────────────────────────────────────
var app = builder.Build();

// ─── Security headers — applied to every response before any other middleware ─
app.Use(async (context, next) =>
{
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
        "connect-src 'self'";

    if (webSettings.EnableHttpsRedirect)
        headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";

    await next(context);
});

// ─── HTTPS redirect ───────────────────────────────────────────────────────────
if (webSettings.EnableHttpsRedirect)
    app.UseHttpsRedirection();

// ─── Static files and routing ─────────────────────────────────────────────────
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();

// ─── Rate limiting — must come after UseRouting so endpoint metadata is resolved ─
app.UseRateLimiter();

app.MapControllers();

app.Run();
