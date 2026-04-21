using PassReset.Common;
using PassReset.PasswordProvider;

namespace PassReset.Tests.PasswordProvider;

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
        if (!OperatingSystem.IsWindows()) return; // platform-gated assertion
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
}
