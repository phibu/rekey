using System.IO;
using System.Text.Json;

namespace PassReset.Tests.Web.Startup;

/// <summary>
/// Nyquist gap-fill (STAB-008): structural invariants on
/// <c>src/PassReset.Web/appsettings.schema.json</c>. These run in-process against the
/// checked-in schema artifact (not a copy) so drift between the schema and these
/// assertions fails the build alongside the CI <c>Test-Json</c> step in ci.yml.
/// </summary>
public class SchemaArtifactTests
{
    private static string SchemaPath
    {
        get
        {
            // Walk upward from the test binary dir until we find the solution file,
            // then resolve the schema path. Robust against Release/Debug/<tfm>/ layout.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PassReset.sln")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return Path.Combine(dir!.FullName, "PassReset.Web", "appsettings.schema.json");
        }
    }

    [Fact]
    public void Schema_File_Exists_At_Expected_Path()
    {
        Assert.True(File.Exists(SchemaPath), $"Schema not found at {SchemaPath}");
    }

    [Fact]
    public void Schema_Parses_As_Valid_Json()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(SchemaPath));
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    [Fact]
    public void Schema_Declares_Draft_2020_12()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(SchemaPath));
        Assert.True(doc.RootElement.TryGetProperty("$schema", out var dollarSchema),
            "Schema missing $schema field.");
        Assert.Equal(
            "https://json-schema.org/draft/2020-12/schema",
            dollarSchema.GetString());
    }

    [Fact]
    public void Schema_Declares_Required_Top_Level_Sections()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(SchemaPath));
        Assert.True(doc.RootElement.TryGetProperty("required", out var required));
        var names = required.EnumerateArray().Select(e => e.GetString()).ToList();

        foreach (var expected in new[]
                 {
                     "WebSettings", "PasswordChangeOptions", "SmtpSettings",
                     "SiemSettings", "ClientSettings",
                 })
        {
            Assert.Contains(expected, names);
        }
    }

    [Fact]
    public void Schema_Properties_Cover_All_Seven_Options_Classes()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(SchemaPath));
        Assert.True(doc.RootElement.TryGetProperty("properties", out var props));

        foreach (var section in new[]
                 {
                     "WebSettings", "PasswordChangeOptions", "SmtpSettings",
                     "SiemSettings", "ClientSettings",
                     "EmailNotificationSettings", "PasswordExpiryNotificationSettings",
                 })
        {
            Assert.True(props.TryGetProperty(section, out _),
                $"Schema properties missing section '{section}'.");
        }
    }

    [Fact]
    public void Schema_Does_Not_Use_Forbidden_Keywords()
    {
        // D-04 restricts keyword set to type / required / enum / pattern / minimum /
        // maximum / default / properties / items / additionalProperties so the schema
        // stays compatible with PowerShell Test-Json. These keywords must NOT appear.
        var raw = File.ReadAllText(SchemaPath);
        foreach (var forbidden in new[] { "\"if\"", "\"then\"", "\"else\"", "\"oneOf\"", "\"anyOf\"", "\"format\"" })
        {
            Assert.DoesNotContain(forbidden, raw);
        }
    }

    [Fact]
    public void Template_Validates_Against_Schema_Structurally()
    {
        // Defence-in-depth for STAB-008. CI enforces this via pwsh Test-Json;
        // this test confirms the template is at least parseable + covers every
        // required top-level section declared by the schema, catching drift even
        // on non-Windows dev machines where pwsh Test-Json isn't available.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PassReset.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var templatePath = Path.Combine(dir!.FullName, "PassReset.Web", "appsettings.Production.template.json");

        Assert.True(File.Exists(templatePath), $"Template not found at {templatePath}");

        using var template = JsonDocument.Parse(File.ReadAllText(templatePath));
        using var schema = JsonDocument.Parse(File.ReadAllText(SchemaPath));

        var schemaRequired = schema.RootElement.GetProperty("required")
            .EnumerateArray().Select(e => e.GetString()).ToList();

        foreach (var section in schemaRequired)
        {
            Assert.True(template.RootElement.TryGetProperty(section!, out _),
                $"Template missing required section '{section}' declared by schema.");
        }
    }

    [Fact]
    public void Template_Has_Zero_Json_Comments()
    {
        // STAB-007: template is pure JSON. A stray `//` comment line would fail
        // System.Text.Json parsing with JsonCommentHandling.Disallow (default).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PassReset.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var templatePath = Path.Combine(dir!.FullName, "PassReset.Web", "appsettings.Production.template.json");

        // Default JsonReaderOptions disallows comments — parsing succeeds only if pure JSON.
        using var doc = JsonDocument.Parse(File.ReadAllText(templatePath));
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }
}
