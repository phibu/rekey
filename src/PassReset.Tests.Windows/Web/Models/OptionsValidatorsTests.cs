using Microsoft.Extensions.Options;
using PassReset.Common;
using PassReset.PasswordProvider;
using PassReset.Web.Models;

namespace PassReset.Tests.Windows.Web.Models;

/// <summary>
/// Unit tests for every <see cref="IValidateOptions{TOptions}"/> validator in
/// <see cref="PassReset.Web.Models"/> plus the pre-existing one in
/// <see cref="PassReset.PasswordProvider"/>. Each validator must conform to D-08
/// message format and must not leak secret values.
/// </summary>
public class OptionsValidatorsTests
{
    private const string RemediationSuffix =
        ". Edit appsettings.Production.json or run Install-PassReset.ps1 -Reconfigure.";

    private const string SecretMarker = "test-secret-value-12345";

    private static void AssertD08Message(string failure, string path)
    {
        Assert.Contains(path, failure);
        Assert.Contains("(got \"", failure);
        Assert.EndsWith(RemediationSuffix, failure);
    }

    // ───────────────────────── ClientSettingsValidator ─────────────────────────

    [Fact]
    public void ClientSettings_WhenRecaptchaDisabled_ReturnsSuccess()
    {
        var v = new ClientSettingsValidator();
        var opts = new ClientSettings { Recaptcha = new Recaptcha { Enabled = false } };
        Assert.True(v.Validate(null, opts).Succeeded);
    }

    [Fact]
    public void ClientSettings_WhenRecaptchaNull_ReturnsSuccess()
    {
        var v = new ClientSettingsValidator();
        var opts = new ClientSettings();
        Assert.True(v.Validate(null, opts).Succeeded);
    }

    [Fact]
    public void ClientSettings_WhenRecaptchaEnabled_AndSiteKeyEmpty_FailsWithD08()
    {
        var v = new ClientSettingsValidator();
        var opts = new ClientSettings
        {
            Recaptcha = new Recaptcha { Enabled = true, SiteKey = "", PrivateKey = SecretMarker, ScoreThreshold = 0.5f }
        };
        var result = v.Validate(null, opts);
        Assert.True(result.Failed);
        var text = string.Join("\n", result.Failures ?? []);
        Assert.Contains("ClientSettings.Recaptcha.SiteKey", text);
        Assert.Contains(RemediationSuffix, text);
    }

    [Fact]
    public void ClientSettings_WhenRecaptchaEnabled_AndPrivateKeyEmpty_FailsWithD08()
    {
        var v = new ClientSettingsValidator();
        var opts = new ClientSettings
        {
            Recaptcha = new Recaptcha { Enabled = true, SiteKey = "s", PrivateKey = "", ScoreThreshold = 0.5f }
        };
        var result = v.Validate(null, opts);
        Assert.True(result.Failed);
        var text = string.Join("\n", result.Failures ?? []);
        Assert.Contains("ClientSettings.Recaptcha.PrivateKey", text);
        Assert.Contains(RemediationSuffix, text);
    }

    [Fact]
    public void ClientSettings_PrivateKeyFailure_DoesNotLeakSecret()
    {
        var v = new ClientSettingsValidator();
        var opts = new ClientSettings
        {
            Recaptcha = new Recaptcha
            {
                Enabled = true,
                SiteKey = "sitekey",
                PrivateKey = SecretMarker,
                ScoreThreshold = 2.0f, // out of range: triggers failure, but secret must not appear in message
            }
        };
        var result = v.Validate(null, opts);
        Assert.True(result.Failed);
        foreach (var f in result.Failures ?? [])
            Assert.DoesNotContain(SecretMarker, f);
    }

    [Fact]
    public void ClientSettings_WhenScoreOutOfRange_Fails()
    {
        var v = new ClientSettingsValidator();
        var opts = new ClientSettings
        {
            Recaptcha = new Recaptcha { Enabled = true, SiteKey = "s", PrivateKey = "p", ScoreThreshold = 1.5f }
        };
        var result = v.Validate(null, opts);
        Assert.True(result.Failed);
        var text = string.Join("\n", result.Failures ?? []);
        Assert.Contains("ClientSettings.Recaptcha.ScoreThreshold", text);
        AssertD08Message(text, "ClientSettings.Recaptcha.ScoreThreshold");
    }

    // ─────────────────────────── WebSettingsValidator ──────────────────────────

    [Fact]
    public void WebSettings_AlwaysSucceeds()
    {
        var v = new WebSettingsValidator();
        Assert.True(v.Validate(null, new WebSettings()).Succeeded);
        Assert.True(v.Validate(null, new WebSettings { UseDebugProvider = true }).Succeeded);
        Assert.True(v.Validate(null, new WebSettings { EnableHttpsRedirect = true }).Succeeded);
    }

    // ─────────────────────────── SmtpSettingsValidator ─────────────────────────

    [Fact]
    public void Smtp_WhenHostEmpty_ReturnsSuccess()
    {
        var v = new SmtpSettingsValidator();
        Assert.True(v.Validate(null, new SmtpSettings()).Succeeded);
    }

    [Fact]
    public void Smtp_WhenPortOutOfRange_Fails()
    {
        var v = new SmtpSettingsValidator();
        var opts = new SmtpSettings { Host = "relay", Port = 0, FromAddress = "a@b.c" };
        var result = v.Validate(null, opts);
        Assert.True(result.Failed);
        AssertD08Message(string.Join("\n", result.Failures ?? []), "SmtpSettings.Port");
    }

    [Fact]
    public void Smtp_WhenFromAddressMalformed_Fails()
    {
        var v = new SmtpSettingsValidator();
        var opts = new SmtpSettings { Host = "relay", Port = 587, FromAddress = "no-at-sign" };
        var result = v.Validate(null, opts);
        Assert.True(result.Failed);
        AssertD08Message(string.Join("\n", result.Failures ?? []), "SmtpSettings.FromAddress");
    }

    [Fact]
    public void Smtp_WhenUsernameSetButPasswordEmpty_Fails()
    {
        var v = new SmtpSettingsValidator();
        var opts = new SmtpSettings
        {
            Host = "relay",
            Port = 587,
            FromAddress = "a@b.c",
            Username = "user",
            Password = "",
        };
        var result = v.Validate(null, opts);
        Assert.True(result.Failed);
        var text = string.Join("\n", result.Failures ?? []);
        Assert.Contains("SmtpSettings.Password", text);
    }

    [Fact]
    public void Smtp_PasswordFailure_DoesNotLeakSecret()
    {
        var v = new SmtpSettingsValidator();
        var opts = new SmtpSettings
        {
            Host = "relay",
            Port = 99999,
            FromAddress = "a@b.c",
            Username = "",
            Password = SecretMarker,
        };
        var result = v.Validate(null, opts);
        Assert.True(result.Failed);
        foreach (var f in result.Failures ?? [])
            Assert.DoesNotContain(SecretMarker, f);
    }

    [Fact]
    public void Smtp_WhenAllValid_ReturnsSuccess()
    {
        var v = new SmtpSettingsValidator();
        var opts = new SmtpSettings
        {
            Host = "relay",
            Port = 587,
            FromAddress = "postmaster@example.com",
            Username = "user",
            Password = "p",
        };
        Assert.True(v.Validate(null, opts).Succeeded);
    }

    // ─────────────────────────── SiemSettingsValidator ─────────────────────────

    [Fact]
    public void Siem_AllDisabled_Succeeds()
    {
        var v = new SiemSettingsValidator();
        Assert.True(v.Validate(null, new SiemSettings()).Succeeded);
    }

    [Fact]
    public void Siem_SyslogEnabled_HostEmpty_Fails()
    {
        var v = new SiemSettingsValidator();
        var opts = new SiemSettings
        {
            Syslog = new SyslogSettings { Enabled = true, Host = "", Port = 514, Protocol = "UDP" }
        };
        var result = v.Validate(null, opts);
        Assert.True(result.Failed);
        AssertD08Message(string.Join("\n", result.Failures ?? []), "SiemSettings.Syslog.Host");
    }

    [Fact]
    public void Siem_SyslogEnabled_PortOutOfRange_Fails()
    {
        var v = new SiemSettingsValidator();
        var opts = new SiemSettings
        {
            Syslog = new SyslogSettings { Enabled = true, Host = "s", Port = 0, Protocol = "UDP" }
        };
        var result = v.Validate(null, opts);
        Assert.True(result.Failed);
        AssertD08Message(string.Join("\n", result.Failures ?? []), "SiemSettings.Syslog.Port");
    }

    [Fact]
    public void Siem_SyslogEnabled_InvalidProtocol_Fails()
    {
        var v = new SiemSettingsValidator();
        var opts = new SiemSettings
        {
            Syslog = new SyslogSettings { Enabled = true, Host = "s", Port = 514, Protocol = "SCTP" }
        };
        var result = v.Validate(null, opts);
        Assert.True(result.Failed);
        AssertD08Message(string.Join("\n", result.Failures ?? []), "SiemSettings.Syslog.Protocol");
    }

    [Fact]
    public void Siem_AlertEmailEnabled_NoRecipients_Fails()
    {
        var v = new SiemSettingsValidator();
        var opts = new SiemSettings
        {
            AlertEmail = new SiemAlertEmailSettings { Enabled = true, Recipients = [] }
        };
        var result = v.Validate(null, opts);
        Assert.True(result.Failed);
        AssertD08Message(string.Join("\n", result.Failures ?? []), "SiemSettings.AlertEmail.Recipients");
    }

    [Fact]
    public void Siem_AlertEmailEnabled_MalformedRecipient_Fails()
    {
        var v = new SiemSettingsValidator();
        var opts = new SiemSettings
        {
            AlertEmail = new SiemAlertEmailSettings { Enabled = true, Recipients = ["not-an-email"] }
        };
        var result = v.Validate(null, opts);
        Assert.True(result.Failed);
        AssertD08Message(string.Join("\n", result.Failures ?? []), "SiemSettings.AlertEmail.Recipients");
    }

    [Fact]
    public void Siem_InvalidAlertOnEvent_Fails()
    {
        var v = new SiemSettingsValidator();
        var opts = new SiemSettings
        {
            AlertEmail = new SiemAlertEmailSettings
            {
                Enabled = true,
                Recipients = ["ops@example.com"],
                AlertOnEvents = ["NotARealEvent"],
            }
        };
        var result = v.Validate(null, opts);
        Assert.True(result.Failed);
        AssertD08Message(string.Join("\n", result.Failures ?? []), "SiemSettings.AlertEmail.AlertOnEvents");
    }

    [Fact]
    public void Siem_AllValid_Succeeds()
    {
        var v = new SiemSettingsValidator();
        var opts = new SiemSettings
        {
            Syslog = new SyslogSettings { Enabled = true, Host = "s", Port = 514, Protocol = "TCP" },
            AlertEmail = new SiemAlertEmailSettings
            {
                Enabled = true,
                Recipients = ["ops@example.com"],
                AlertOnEvents = ["PortalLockout"],
            }
        };
        Assert.True(v.Validate(null, opts).Succeeded);
    }

    // ────────────────── EmailNotificationSettingsValidator ─────────────────────

    [Fact]
    public void EmailNotif_Disabled_Succeeds()
    {
        var v = new EmailNotificationSettingsValidator();
        Assert.True(v.Validate(null, new EmailNotificationSettings()).Succeeded);
    }

    [Fact]
    public void EmailNotif_Enabled_EmptySubject_Fails()
    {
        var v = new EmailNotificationSettingsValidator();
        var opts = new EmailNotificationSettings { Enabled = true, Subject = "", BodyTemplate = "x" };
        var result = v.Validate(null, opts);
        Assert.True(result.Failed);
        AssertD08Message(string.Join("\n", result.Failures ?? []), "EmailNotificationSettings.Subject");
    }

    [Fact]
    public void EmailNotif_Enabled_EmptyBody_Fails()
    {
        var v = new EmailNotificationSettingsValidator();
        var opts = new EmailNotificationSettings { Enabled = true, Subject = "x", BodyTemplate = "" };
        var result = v.Validate(null, opts);
        Assert.True(result.Failed);
        AssertD08Message(string.Join("\n", result.Failures ?? []), "EmailNotificationSettings.BodyTemplate");
    }

    // ────────────────── PasswordExpiryNotificationSettingsValidator ────────────

    [Fact]
    public void Expiry_Disabled_Succeeds()
    {
        var v = new PasswordExpiryNotificationSettingsValidator();
        Assert.True(v.Validate(null, new PasswordExpiryNotificationSettings()).Succeeded);
    }

    [Fact]
    public void Expiry_Enabled_ZeroDays_Fails()
    {
        var v = new PasswordExpiryNotificationSettingsValidator();
        var opts = new PasswordExpiryNotificationSettings
        {
            Enabled = true,
            DaysBeforeExpiry = 0,
            NotificationTimeUtc = "08:00",
            PassResetUrl = "https://passreset.example.com",
        };
        var result = v.Validate(null, opts);
        Assert.True(result.Failed);
        AssertD08Message(string.Join("\n", result.Failures ?? []), "PasswordExpiryNotificationSettings.DaysBeforeExpiry");
    }

    [Fact]
    public void Expiry_Enabled_MalformedTime_Fails()
    {
        var v = new PasswordExpiryNotificationSettingsValidator();
        var opts = new PasswordExpiryNotificationSettings
        {
            Enabled = true,
            DaysBeforeExpiry = 14,
            NotificationTimeUtc = "0800",
            PassResetUrl = "https://passreset.example.com",
        };
        var result = v.Validate(null, opts);
        Assert.True(result.Failed);
        AssertD08Message(string.Join("\n", result.Failures ?? []), "PasswordExpiryNotificationSettings.NotificationTimeUtc");
    }

    [Fact]
    public void Expiry_Enabled_EmptyUrl_Fails()
    {
        var v = new PasswordExpiryNotificationSettingsValidator();
        var opts = new PasswordExpiryNotificationSettings
        {
            Enabled = true,
            DaysBeforeExpiry = 14,
            NotificationTimeUtc = "08:00",
            PassResetUrl = "",
        };
        var result = v.Validate(null, opts);
        Assert.True(result.Failed);
        AssertD08Message(string.Join("\n", result.Failures ?? []), "PasswordExpiryNotificationSettings.PassResetUrl");
    }

    [Fact]
    public void Expiry_Enabled_InsecureUrl_Fails()
    {
        var v = new PasswordExpiryNotificationSettingsValidator();
        var opts = new PasswordExpiryNotificationSettings
        {
            Enabled = true,
            DaysBeforeExpiry = 14,
            NotificationTimeUtc = "08:00",
            PassResetUrl = "http://passreset.example.com",
        };
        var result = v.Validate(null, opts);
        Assert.True(result.Failed);
        AssertD08Message(string.Join("\n", result.Failures ?? []), "PasswordExpiryNotificationSettings.PassResetUrl");
    }

    [Fact]
    public void Expiry_AllValid_Succeeds()
    {
        var v = new PasswordExpiryNotificationSettingsValidator();
        var opts = new PasswordExpiryNotificationSettings
        {
            Enabled = true,
            DaysBeforeExpiry = 14,
            NotificationTimeUtc = "08:00",
            PassResetUrl = "https://passreset.example.com",
        };
        Assert.True(v.Validate(null, opts).Succeeded);
    }

    // ────────────── PasswordChangeOptionsValidator (D-08 upgrade) ──────────────

    [Fact]
    public void PasswordChangeOptions_AutoContext_Succeeds()
    {
        var v = new PasswordChangeOptionsValidator();
        Assert.True(v.Validate(null, new PasswordChangeOptions { UseAutomaticContext = true }).Succeeded);
    }

    [Fact]
    public void PasswordChangeOptions_ManualContext_EmptyHostnames_FailsWithD08Suffix()
    {
        var v = new PasswordChangeOptionsValidator();
        var opts = new PasswordChangeOptions { UseAutomaticContext = false, LdapHostnames = [] };
        var result = v.Validate(null, opts);
        Assert.True(result.Failed);
        var text = string.Join("\n", result.Failures ?? []);
        Assert.Contains("PasswordChangeOptions.LdapHostnames", text);
        Assert.Contains(RemediationSuffix, text);
    }

    [Fact]
    public void PasswordChangeOptions_BadPort_FailsWithD08Suffix()
    {
        var v = new PasswordChangeOptionsValidator();
        var opts = new PasswordChangeOptions
        {
            UseAutomaticContext = false,
            LdapHostnames = ["dc1"],
            LdapPort = 99999,
        };
        var result = v.Validate(null, opts);
        Assert.True(result.Failed);
        var text = string.Join("\n", result.Failures ?? []);
        Assert.Contains("PasswordChangeOptions.LdapPort", text);
        Assert.Contains(RemediationSuffix, text);
    }
}
