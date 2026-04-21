using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using PassReset.Web.Models;

namespace PassReset.Tests.Windows.Web.Startup;

/// <summary>
/// End-to-end validation that <see cref="ValidateOnStart"/> wiring fails fast when
/// appsettings are invalid. Mirrors the existing <c>DebugFactory</c> pattern:
/// each test class owns a dedicated <see cref="WebApplicationFactory{TEntryPoint}"/>
/// subclass with <see cref="IWebHostBuilder.ConfigureAppConfiguration"/> overrides.
///
/// Why subclasses instead of inline <c>new WebApplicationFactory&lt;Program&gt;()</c>?
/// <c>HostFactoryResolver.HostingListener</c> uses a process-wide DiagnosticListener
/// to intercept <c>builder.Build()</c> inside top-level <c>Program.cs</c>. Multiple
/// inline factories can race with existing <c>DebugFactory</c> tests in the same
/// process and miss the intercept, producing "entry point did not build an IHost".
/// Subclassing with a dedicated <see cref="ConfigureWebHost"/> override keeps the
/// listener state deterministic.
/// </summary>
public class StartupValidationTests
{
    /// <summary>
    /// Factory for the invalid-PasswordChangeOptions scenario (empty LdapHostnames + out-of-range port).
    /// </summary>
    public sealed class InvalidPasswordChangeOptionsFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["WebSettings:UseDebugProvider"] = "true",
                    ["WebSettings:EnableHttpsRedirect"] = "false",
                    ["ClientSettings:Recaptcha:Enabled"] = "false",
                    ["SmtpSettings:Host"] = "",
                    ["SiemSettings:Syslog:Enabled"] = "false",
                    ["SiemSettings:AlertEmail:Enabled"] = "false",
                    ["EmailNotificationSettings:Enabled"] = "false",
                    ["PasswordExpiryNotificationSettings:Enabled"] = "false",
                    // Invalid combo — validator must trip at ValidateOnStart.
                    ["PasswordChangeOptions:UseAutomaticContext"] = "false",
                    ["PasswordChangeOptions:LdapHostnames:0"] = "",
                    ["PasswordChangeOptions:LdapPort"] = "99999",
                });
            });
        }
    }

    [Fact]
    public void Build_WithInvalidPasswordChangeOptions_ThrowsOptionsValidationException()
    {
        using var factory = new InvalidPasswordChangeOptionsFactory();

        var ex = Assert.ThrowsAny<Exception>(() =>
        {
            // Force the host to build — WebApplicationFactory defers until first client request.
            using var client = factory.CreateClient();
        });

        var chain = Flatten(ex).ToList();
        // Either the OptionsValidationException is in the chain, or its D-08 message content
        // survives in the flattened exception messages (WebApplicationFactory can re-wrap).
        Assert.True(
            chain.Any(e => e is OptionsValidationException)
            || chain.Any(e => (e.Message ?? string.Empty).Contains("PasswordChangeOptions.LdapHostnames")
                              || (e.Message ?? string.Empty).Contains("PasswordChangeOptions.LdapPort")),
            "Expected OptionsValidationException or D-08 message in exception chain. Got: "
                + string.Join(" | ", chain.Select(e => $"{e.GetType().Name}: {e.Message}")));
    }

    private static IEnumerable<Exception> Flatten(Exception ex)
    {
        var current = ex;
        while (current is not null)
        {
            yield return current;
            if (current is AggregateException agg)
            {
                foreach (var inner in agg.InnerExceptions)
                    foreach (var x in Flatten(inner))
                        yield return x;
            }
            current = current.InnerException;
        }
    }

    // ─── Per-validator unit tests (WR-01) ──────────────────────────────────────
    // Each of the six IValidateOptions validators registered by AddValidators() in
    // Program.cs gets one "valid config -> Success" test and one targeted
    // "missing-required-field -> Fail" test. These complement the in-process
    // WebApplicationFactory test above by covering individual validator logic
    // without spinning up the host.

    // --- 1. ClientSettingsValidator ---

    [Fact]
    public void ClientSettingsValidator_RecaptchaDisabled_ReturnsSuccess()
    {
        var validator = new ClientSettingsValidator();
        var options = new ClientSettings
        {
            Recaptcha = new Recaptcha { Enabled = false },
        };

        var result = validator.Validate(null, options);

        Assert.True(result.Succeeded, "Expected Success when Recaptcha is disabled.");
    }

    [Fact]
    public void ClientSettingsValidator_RecaptchaEnabledWithEmptyKeys_ReturnsFail()
    {
        var validator = new ClientSettingsValidator();
        var options = new ClientSettings
        {
            Recaptcha = new Recaptcha
            {
                Enabled = true,
                SiteKey = "",
                PrivateKey = "",
                ScoreThreshold = 0.5f,
            },
        };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed, "Expected Fail when SiteKey/PrivateKey empty.");
        Assert.Contains(result.Failures, f => f.Contains("SiteKey"));
        Assert.Contains(result.Failures, f => f.Contains("PrivateKey"));
    }

    // --- 2. WebSettingsValidator ---

    [Fact]
    public void WebSettingsValidator_DefaultSettings_ReturnsSuccess()
    {
        var validator = new WebSettingsValidator();
        var options = new WebSettings
        {
            EnableHttpsRedirect = true,
            UseDebugProvider = false,
        };

        var result = validator.Validate(null, options);

        Assert.True(result.Succeeded, "Expected Success for baseline WebSettings.");
    }

    [Fact]
    public void WebSettingsValidator_AlwaysSucceeds_NoTypeOnlyInvariants()
    {
        // Current validator intentionally has no type-only rules (UseDebugProvider -> IsDevelopment
        // requires IHostEnvironment and is asserted inline in Program.cs). This test pins that
        // contract so regressions are caught when new rules are added here.
        var validator = new WebSettingsValidator();
        var options = new WebSettings { EnableHttpsRedirect = false, UseDebugProvider = true };

        var result = validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    // --- 3. SmtpSettingsValidator ---

    [Fact]
    public void SmtpSettingsValidator_ValidAnonymousRelay_ReturnsSuccess()
    {
        var validator = new SmtpSettingsValidator();
        var options = new SmtpSettings
        {
            Host = "smtp.example.com",
            Port = 587,
            FromAddress = "noreply@example.com",
            Username = "",
            Password = "",
        };

        var result = validator.Validate(null, options);

        Assert.True(result.Succeeded, "Expected Success for anonymous relay.");
    }

    [Fact]
    public void SmtpSettingsValidator_UsernameWithoutPassword_ReturnsFail()
    {
        var validator = new SmtpSettingsValidator();
        var options = new SmtpSettings
        {
            Host = "smtp.example.com",
            Port = 587,
            FromAddress = "noreply@example.com",
            Username = "svc-mailer",
            Password = "",
        };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed, "Expected Fail on Username-without-Password XOR violation.");
        Assert.Contains(result.Failures, f => f.Contains("SmtpSettings.Password"));
    }

    // --- 4. SiemSettingsValidator ---

    [Fact]
    public void SiemSettingsValidator_ValidSyslog_ReturnsSuccess()
    {
        var validator = new SiemSettingsValidator();
        var options = new SiemSettings
        {
            Syslog = new SyslogSettings
            {
                Enabled = true,
                Host = "siem.example.com",
                Port = 514,
                Protocol = "UDP",
            },
            AlertEmail = new SiemAlertEmailSettings { Enabled = false },
        };

        var result = validator.Validate(null, options);

        Assert.True(result.Succeeded, "Expected Success for valid syslog config.");
    }

    [Fact]
    public void SiemSettingsValidator_SyslogEnabledWithEmptyHost_ReturnsFail()
    {
        var validator = new SiemSettingsValidator();
        var options = new SiemSettings
        {
            Syslog = new SyslogSettings
            {
                Enabled = true,
                Host = "",
                Port = 514,
                Protocol = "UDP",
            },
            AlertEmail = new SiemAlertEmailSettings { Enabled = false },
        };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed, "Expected Fail when syslog enabled with empty host.");
        Assert.Contains(result.Failures, f => f.Contains("SiemSettings.Syslog.Host"));
    }

    // --- 5. EmailNotificationSettingsValidator ---

    [Fact]
    public void EmailNotificationSettingsValidator_EnabledWithTemplates_ReturnsSuccess()
    {
        var validator = new EmailNotificationSettingsValidator();
        var options = new EmailNotificationSettings
        {
            Enabled = true,
            Subject = "Your password has been changed",
            BodyTemplate = "Hello {Username}, your password changed at {Timestamp}.",
        };

        var result = validator.Validate(null, options);

        Assert.True(result.Succeeded, "Expected Success when enabled with non-empty templates.");
    }

    [Fact]
    public void EmailNotificationSettingsValidator_EnabledWithEmptySubject_ReturnsFail()
    {
        var validator = new EmailNotificationSettingsValidator();
        var options = new EmailNotificationSettings
        {
            Enabled = true,
            Subject = "",
            BodyTemplate = "Hello {Username}.",
        };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed, "Expected Fail when Subject empty and Enabled=true.");
        Assert.Contains(result.Failures, f => f.Contains("EmailNotificationSettings.Subject"));
    }

    // --- 6. PasswordExpiryNotificationSettingsValidator ---

    [Fact]
    public void PasswordExpiryNotificationSettingsValidator_DisabledAlwaysSucceeds()
    {
        var validator = new PasswordExpiryNotificationSettingsValidator();
        var options = new PasswordExpiryNotificationSettings
        {
            Enabled = false,
            DaysBeforeExpiry = 0, // would fail if Enabled=true
            NotificationTimeUtc = "",
            PassResetUrl = "",
        };

        var result = validator.Validate(null, options);

        Assert.True(result.Succeeded, "Expected Success when disabled regardless of other fields.");
    }

    [Fact]
    public void PasswordExpiryNotificationSettingsValidator_EnabledWithInvalidDays_ReturnsFail()
    {
        var validator = new PasswordExpiryNotificationSettingsValidator();
        var options = new PasswordExpiryNotificationSettings
        {
            Enabled = true,
            DaysBeforeExpiry = 0,
            NotificationTimeUtc = "08:00",
            PassResetUrl = "https://passreset.example.com",
        };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed, "Expected Fail when DaysBeforeExpiry <= 0 and Enabled=true.");
        Assert.Contains(result.Failures, f => f.Contains("DaysBeforeExpiry"));
    }
}
