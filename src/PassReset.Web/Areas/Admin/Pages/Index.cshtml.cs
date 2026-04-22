using Microsoft.AspNetCore.Mvc.RazorPages;
using PassReset.Web.Services.Configuration;

namespace PassReset.Web.Areas.Admin.Pages;

public sealed class IndexModel : PageModel
{
    private readonly IAppSettingsEditor _editor;
    private readonly ISecretStore _secrets;

    public IndexModel(IAppSettingsEditor editor, ISecretStore secrets)
    {
        _editor = editor;
        _secrets = secrets;
    }

    public string LdapSummary { get; private set; } = "";
    public string SmtpSummary { get; private set; } = "";
    public string RecaptchaSummary { get; private set; } = "";
    public string GroupsSummary { get; private set; } = "";
    public string LocalPolicySummary { get; private set; } = "";
    public string SiemSummary { get; private set; } = "";

    public void OnGet()
    {
        var snap = _editor.Load();
        var bundle = _secrets.Load();

        LdapSummary = snap.PasswordChange.UseAutomaticContext
            ? "Automatic context (domain-joined)"
            : $"Service-account mode; hostnames: {snap.PasswordChange.LdapHostnames.Length}; password: {Mask(bundle.LdapPassword ?? bundle.ServiceAccountPassword)}";
        SmtpSummary = string.IsNullOrEmpty(snap.Smtp.Host)
            ? "Not configured"
            : $"{snap.Smtp.Host}:{snap.Smtp.Port}; password: {Mask(bundle.SmtpPassword)}";
        RecaptchaSummary = snap.Recaptcha.Enabled
            ? $"Enabled; key: {Mask(bundle.RecaptchaPrivateKey)}"
            : "Disabled";
        GroupsSummary = $"Allowed: {snap.Groups.AllowedAdGroups.Length}; Restricted: {snap.Groups.RestrictedAdGroups.Length}";
        LocalPolicySummary = snap.LocalPolicy.BannedWordsPath is null && snap.LocalPolicy.LocalPwnedPasswordsPath is null
            ? "Disabled"
            : $"Banned-words: {(snap.LocalPolicy.BannedWordsPath is null ? "off" : "on")}; Local pwned: {(snap.LocalPolicy.LocalPwnedPasswordsPath is null ? "off" : "on")}";
        SiemSummary = snap.Siem.Enabled ? $"Enabled ({snap.Siem.Host}:{snap.Siem.Port}, {snap.Siem.Protocol})" : "Disabled";
    }

    private static string Mask(string? value) => string.IsNullOrEmpty(value) ? "not set" : "set";
}
