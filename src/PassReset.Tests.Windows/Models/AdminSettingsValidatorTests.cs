using Microsoft.Extensions.Options;
using PassReset.Web.Configuration;
using PassReset.Web.Models;

namespace PassReset.Tests.Windows.Models;

// NOTE: the test project targets net10.0-windows, so the non-Windows branch
// in AdminSettingsValidator (DataProtectionCertThumbprint required on Linux)
// cannot be exercised here. OperatingSystem.IsWindows() is always true.
public sealed class AdminSettingsValidatorTests
{
    private static AdminSettings Baseline() => new() { Enabled = true, LoopbackPort = 5010 };

    private static ValidateOptionsResult Run(AdminSettings opts) =>
        new AdminSettingsValidator().Validate(null, opts);

    [Fact]
    public void Defaults_Pass()
    {
        var r = Run(Baseline());
        Assert.True(r.Succeeded, string.Join("; ", r.Failures ?? []));
    }

    [Fact]
    public void Disabled_SkipsAllChecks_EvenIfOtherFieldsInvalid()
    {
        var r = Run(new AdminSettings { Enabled = false, LoopbackPort = 0 });
        Assert.True(r.Succeeded, string.Join("; ", r.Failures ?? []));
    }

    [Fact]
    public void Default_IsDisabled_AndPasses()
    {
        // Ensures opt-in default: a bare AdminSettings (feature flag unset) must validate.
        var r = Run(new AdminSettings());
        Assert.True(r.Succeeded, string.Join("; ", r.Failures ?? []));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(80)]
    [InlineData(1023)]
    [InlineData(65536)]
    [InlineData(70000)]
    public void LoopbackPort_OutOfRange_Fails(int port)
    {
        var opts = Baseline();
        opts.LoopbackPort = port;
        var r = Run(opts);
        Assert.False(r.Succeeded);
        Assert.Contains(r.Failures!, m => m.Contains("LoopbackPort", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(1024)]
    [InlineData(5010)]
    [InlineData(65535)]
    public void LoopbackPort_WithinRange_Passes(int port)
    {
        var opts = Baseline();
        opts.LoopbackPort = port;
        var r = Run(opts);
        Assert.True(r.Succeeded, string.Join("; ", r.Failures ?? []));
    }

    [Fact]
    public void KeyStorePath_Relative_Fails()
    {
        var opts = Baseline();
        opts.KeyStorePath = "relative/keys";
        var r = Run(opts);
        Assert.False(r.Succeeded);
        Assert.Contains(r.Failures!, m => m.Contains("KeyStorePath", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AppSettingsFilePath_Relative_Fails()
    {
        var opts = Baseline();
        opts.AppSettingsFilePath = "appsettings.json";
        var r = Run(opts);
        Assert.False(r.Succeeded);
        Assert.Contains(r.Failures!, m => m.Contains("AppSettingsFilePath", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SecretsFilePath_Relative_Fails()
    {
        var opts = Baseline();
        opts.SecretsFilePath = "secrets.dat";
        var r = Run(opts);
        Assert.False(r.Succeeded);
        Assert.Contains(r.Failures!, m => m.Contains("SecretsFilePath", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AbsolutePaths_Pass()
    {
        var opts = Baseline();
        opts.KeyStorePath = Path.Combine(Path.GetTempPath(), "keys");
        opts.AppSettingsFilePath = Path.Combine(Path.GetTempPath(), "appsettings.Production.json");
        opts.SecretsFilePath = Path.Combine(Path.GetTempPath(), "secrets.dat");
        var r = Run(opts);
        Assert.True(r.Succeeded, string.Join("; ", r.Failures ?? []));
    }
}
