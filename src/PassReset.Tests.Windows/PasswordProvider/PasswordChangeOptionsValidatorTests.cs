using PassReset.Common;
using PassReset.PasswordProvider;

namespace PassReset.Tests.Windows.PasswordProvider;

public class PasswordChangeOptionsValidatorTests
{
    [Fact]
    public void LdapMode_MissingServiceAccountDn_Fails()
    {
        var sut = new PasswordChangeOptionsValidator();
        var opts = new PasswordChangeOptions
        {
            ProviderMode = ProviderMode.Ldap,
            ServiceAccountDn = "",
            ServiceAccountPassword = "pw",
            BaseDn = "DC=corp,DC=example,DC=com",
            LdapHostnames = new[] { "dc01.corp.example.com" },
            UseAutomaticContext = false,
            LdapPort = 636,
        };

        var result = sut.Validate(null, opts);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("ServiceAccountDn"));
    }

    [Fact]
    public void LdapMode_MissingBaseDn_Fails()
    {
        var sut = new PasswordChangeOptionsValidator();
        var opts = new PasswordChangeOptions
        {
            ProviderMode = ProviderMode.Ldap,
            ServiceAccountDn = "CN=svc,DC=corp,DC=example,DC=com",
            ServiceAccountPassword = "pw",
            BaseDn = "",
            LdapHostnames = new[] { "dc01.corp.example.com" },
            UseAutomaticContext = false,
            LdapPort = 636,
        };

        var result = sut.Validate(null, opts);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("BaseDn"));
    }

    [Fact]
    public void LdapMode_AllFieldsPresent_Succeeds()
    {
        var sut = new PasswordChangeOptionsValidator();
        var opts = new PasswordChangeOptions
        {
            ProviderMode = ProviderMode.Ldap,
            ServiceAccountDn = "CN=svc,DC=corp,DC=example,DC=com",
            ServiceAccountPassword = "pw",
            BaseDn = "DC=corp,DC=example,DC=com",
            LdapHostnames = new[] { "dc01.corp.example.com" },
            UseAutomaticContext = false,
            LdapPort = 636,
        };

        var result = sut.Validate(null, opts);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void WindowsMode_OnWindows_Succeeds()
    {
        var sut = new PasswordChangeOptionsValidator();
        var opts = new PasswordChangeOptions
        {
            ProviderMode = ProviderMode.Windows,
            UseAutomaticContext = true,
            LdapPort = 636,
        };

        var result = sut.Validate(null, opts);

        Assert.True(result.Succeeded);
    }

    // ── LocalPolicy validation ────────────────────────────────────────────────

    private static PasswordChangeOptions BuildValidOptions() => new()
    {
        ProviderMode = ProviderMode.Windows,
        UseAutomaticContext = true,
        LdapPort = 636,
    };

    [Fact]
    public void BannedWordsPathSetButMissing_Fails()
    {
        var sut = new PasswordChangeOptionsValidator();
        var opts = BuildValidOptions();
        opts.LocalPolicy.BannedWordsPath = Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N") + ".txt");

        var result = sut.Validate(null, opts);

        Assert.False(result.Succeeded, $"expected failure, got: {string.Join("; ", result.Failures ?? [])}");
        Assert.Contains(result.Failures!, m => m.Contains("BannedWordsPath", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LocalPwnedPasswordsPathSetButMissing_Fails()
    {
        var sut = new PasswordChangeOptionsValidator();
        var opts = BuildValidOptions();
        opts.LocalPolicy.LocalPwnedPasswordsPath = Path.Combine(Path.GetTempPath(), "missing-dir-" + Guid.NewGuid().ToString("N"));

        var result = sut.Validate(null, opts);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, m => m.Contains("LocalPwnedPasswordsPath", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LocalPwnedPasswordsPathExistsButEmptyDir_Fails()
    {
        var dir = Path.Combine(Path.GetTempPath(), "empty-corpus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var sut = new PasswordChangeOptionsValidator();
            var opts = BuildValidOptions();
            opts.LocalPolicy.LocalPwnedPasswordsPath = dir;

            var result = sut.Validate(null, opts);

            Assert.False(result.Succeeded);
            Assert.Contains(result.Failures!, m =>
                m.Contains("LocalPwnedPasswordsPath", StringComparison.OrdinalIgnoreCase));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void LocalPwnedPasswordsPathWithValidPrefixFile_Passes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ok-corpus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "ABCDE.txt"), "0000000000000000000000000000000000A:1\n");
        try
        {
            var sut = new PasswordChangeOptionsValidator();
            var opts = BuildValidOptions();
            opts.LocalPolicy.LocalPwnedPasswordsPath = dir;

            var result = sut.Validate(null, opts);

            Assert.True(result.Succeeded, $"expected success, got: {string.Join("; ", result.Failures ?? [])}");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void MinBannedTermLengthZero_Fails()
    {
        var sut = new PasswordChangeOptionsValidator();
        var opts = BuildValidOptions();
        opts.LocalPolicy.MinBannedTermLength = 0;

        var result = sut.Validate(null, opts);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, m => m.Contains("MinBannedTermLength", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LocalPolicyAllDefaults_Passes()
    {
        var sut = new PasswordChangeOptionsValidator();
        var opts = BuildValidOptions();
        // LocalPolicy defaults: both paths null, MinBannedTermLength = 4

        var result = sut.Validate(null, opts);

        Assert.True(result.Succeeded, $"expected success, got: {string.Join("; ", result.Failures ?? [])}");
    }
}
