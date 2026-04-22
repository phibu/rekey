using System.Text.Json;
using System.Text.Json.Nodes;
using PassReset.Common;

namespace PassReset.Web.Services.Configuration;

/// <summary>
/// Reads and writes <c>appsettings.Production.json</c> using <see cref="JsonNode"/>
/// (specifically <see cref="JsonObject"/>) to preserve top-level key insertion order
/// and unmanaged keys. Only the sections enumerated in <see cref="AppSettingsSnapshot"/>
/// are mutated by <see cref="Save"/>; everything else passes through untouched.
/// </summary>
internal sealed class AppSettingsEditor : IAppSettingsEditor
{
    private static readonly JsonSerializerOptions ReadOpts = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions WriteOpts = new()
    {
        WriteIndented = true,
    };

    private static readonly JsonNodeOptions NodeOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly string _path;

    public AppSettingsEditor(string path)
    {
        _path = path;
    }

    public AppSettingsSnapshot Load()
    {
        if (!File.Exists(_path))
        {
            return Defaults();
        }

        var text = File.ReadAllText(_path);
        var root = JsonNode.Parse(text, NodeOpts, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        }) as JsonObject;

        if (root is null) return Defaults();

        return new AppSettingsSnapshot(
            PasswordChange: ReadPasswordChange(root),
            Smtp: ReadSmtp(root),
            Recaptcha: ReadRecaptcha(root),
            Siem: ReadSiem(root),
            Groups: ReadGroups(root),
            LocalPolicy: ReadLocalPolicy(root));
    }

    public void Save(AppSettingsSnapshot snapshot)
    {
        JsonObject root;
        if (File.Exists(_path))
        {
            var text = File.ReadAllText(_path);
            root = JsonNode.Parse(text, NodeOpts, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }) as JsonObject ?? new JsonObject();
        }
        else
        {
            root = new JsonObject();
        }

        WritePasswordChange(root, snapshot.PasswordChange, snapshot.Groups, snapshot.LocalPolicy);
        WriteSmtp(root, snapshot.Smtp);
        WriteRecaptcha(root, snapshot.Recaptcha);
        WriteSiem(root, snapshot.Siem);

        var json = root.ToJsonString(WriteOpts);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, _path, overwrite: true);
    }

    private static AppSettingsSnapshot Defaults() => new(
        PasswordChange: new PasswordChangeSection(
            UseAutomaticContext: true,
            ProviderMode: ProviderMode.Auto,
            LdapHostnames: [],
            LdapPort: 636,
            LdapUseSsl: true,
            BaseDn: "",
            ServiceAccountDn: "",
            DefaultDomain: ""),
        Smtp: new SmtpSection("", 25, "", "", true),
        Recaptcha: new RecaptchaPublicSection(false, ""),
        Siem: new SiemSyslogSection(false, "", 514, "Udp"),
        Groups: new GroupsSection([], []),
        LocalPolicy: new LocalPolicySection(null, null, 4));

    // ── Read helpers ────────────────────────────────────────────────────────────

    private static PasswordChangeSection ReadPasswordChange(JsonObject root)
    {
        var pc = root["PasswordChangeOptions"]?.AsObject();
        if (pc is null) return Defaults().PasswordChange;
        return new PasswordChangeSection(
            UseAutomaticContext: pc["UseAutomaticContext"]?.GetValue<bool>() ?? true,
            ProviderMode: ParseProviderMode(pc["ProviderMode"]?.GetValue<string>()),
            LdapHostnames: (pc["LdapHostnames"]?.AsArray().Select(n => n!.GetValue<string>()).ToArray()) ?? [],
            LdapPort: pc["LdapPort"]?.GetValue<int>() ?? 636,
            LdapUseSsl: pc["LdapUseSsl"]?.GetValue<bool>() ?? true,
            BaseDn: pc["BaseDn"]?.GetValue<string>() ?? "",
            ServiceAccountDn: pc["ServiceAccountDn"]?.GetValue<string>() ?? "",
            DefaultDomain: pc["DefaultDomain"]?.GetValue<string>() ?? "");
    }

    private static GroupsSection ReadGroups(JsonObject root)
    {
        var pc = root["PasswordChangeOptions"]?.AsObject();
        return new GroupsSection(
            AllowedAdGroups: (pc?["AllowedAdGroups"]?.AsArray().Select(n => n!.GetValue<string>()).ToArray()) ?? [],
            RestrictedAdGroups: (pc?["RestrictedAdGroups"]?.AsArray().Select(n => n!.GetValue<string>()).ToArray()) ?? []);
    }

    private static LocalPolicySection ReadLocalPolicy(JsonObject root)
    {
        var lp = root["PasswordChangeOptions"]?["LocalPolicy"]?.AsObject();
        return new LocalPolicySection(
            BannedWordsPath: lp?["BannedWordsPath"]?.GetValue<string>(),
            LocalPwnedPasswordsPath: lp?["LocalPwnedPasswordsPath"]?.GetValue<string>(),
            MinBannedTermLength: lp?["MinBannedTermLength"]?.GetValue<int>() ?? 4);
    }

    private static SmtpSection ReadSmtp(JsonObject root)
    {
        var s = root["SmtpSettings"]?.AsObject();
        if (s is null) return Defaults().Smtp;
        return new SmtpSection(
            Host: s["Host"]?.GetValue<string>() ?? "",
            Port: s["Port"]?.GetValue<int>() ?? 25,
            Username: s["Username"]?.GetValue<string>() ?? "",
            FromAddress: s["FromAddress"]?.GetValue<string>() ?? "",
            UseStartTls: s["UseStartTls"]?.GetValue<bool>() ?? true);
    }

    private static RecaptchaPublicSection ReadRecaptcha(JsonObject root)
    {
        var r = root["ClientSettings"]?["Recaptcha"]?.AsObject();
        return new RecaptchaPublicSection(
            Enabled: r?["Enabled"]?.GetValue<bool>() ?? false,
            SiteKey: r?["SiteKey"]?.GetValue<string>() ?? "");
    }

    private static SiemSyslogSection ReadSiem(JsonObject root)
    {
        var s = root["SiemSettings"]?["Syslog"]?.AsObject();
        return new SiemSyslogSection(
            Enabled: s?["Enabled"]?.GetValue<bool>() ?? false,
            Host: s?["Host"]?.GetValue<string>() ?? "",
            Port: s?["Port"]?.GetValue<int>() ?? 514,
            Protocol: s?["Protocol"]?.GetValue<string>() ?? "Udp");
    }

    private static ProviderMode ParseProviderMode(string? s) =>
        Enum.TryParse<ProviderMode>(s, ignoreCase: true, out var m) ? m : ProviderMode.Auto;

    // ── Write helpers ───────────────────────────────────────────────────────────

    private static JsonObject GetOrCreate(JsonObject parent, string key)
    {
        if (parent[key] is JsonObject existing) return existing;
        var fresh = new JsonObject();
        parent[key] = fresh;
        return fresh;
    }

    private static void WritePasswordChange(JsonObject root, PasswordChangeSection pc, GroupsSection groups, LocalPolicySection lp)
    {
        var node = GetOrCreate(root, "PasswordChangeOptions");
        node["UseAutomaticContext"] = pc.UseAutomaticContext;
        node["ProviderMode"] = pc.ProviderMode.ToString();
        node["LdapHostnames"] = new JsonArray(pc.LdapHostnames.Select(h => (JsonNode)h).ToArray());
        node["LdapPort"] = pc.LdapPort;
        node["LdapUseSsl"] = pc.LdapUseSsl;
        node["BaseDn"] = pc.BaseDn;
        node["ServiceAccountDn"] = pc.ServiceAccountDn;
        node["DefaultDomain"] = pc.DefaultDomain;
        node["AllowedAdGroups"] = new JsonArray(groups.AllowedAdGroups.Select(g => (JsonNode)g).ToArray());
        node["RestrictedAdGroups"] = new JsonArray(groups.RestrictedAdGroups.Select(g => (JsonNode)g).ToArray());

        var localPolicy = GetOrCreate(node, "LocalPolicy");
        localPolicy["BannedWordsPath"] = lp.BannedWordsPath;
        localPolicy["LocalPwnedPasswordsPath"] = lp.LocalPwnedPasswordsPath;
        localPolicy["MinBannedTermLength"] = lp.MinBannedTermLength;
    }

    private static void WriteSmtp(JsonObject root, SmtpSection s)
    {
        var node = GetOrCreate(root, "SmtpSettings");
        node["Host"] = s.Host;
        node["Port"] = s.Port;
        node["Username"] = s.Username;
        node["FromAddress"] = s.FromAddress;
        node["UseStartTls"] = s.UseStartTls;
    }

    private static void WriteRecaptcha(JsonObject root, RecaptchaPublicSection r)
    {
        var client = GetOrCreate(root, "ClientSettings");
        var recaptcha = GetOrCreate(client, "Recaptcha");
        recaptcha["Enabled"] = r.Enabled;
        recaptcha["SiteKey"] = r.SiteKey;
    }

    private static void WriteSiem(JsonObject root, SiemSyslogSection s)
    {
        var siem = GetOrCreate(root, "SiemSettings");
        var syslog = GetOrCreate(siem, "Syslog");
        syslog["Enabled"] = s.Enabled;
        syslog["Host"] = s.Host;
        syslog["Port"] = s.Port;
        syslog["Protocol"] = s.Protocol;
    }
}
