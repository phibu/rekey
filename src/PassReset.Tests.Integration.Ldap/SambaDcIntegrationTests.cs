using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PassReset.Common;
using PassReset.PasswordProvider.Ldap;
using Xunit;

namespace PassReset.Tests.Integration.Ldap;

/// <summary>
/// End-to-end smoke test against a real LDAP server (Samba AD DC in CI).
/// Gated via PASSRESET_INTEGRATION_LDAP=1. Runs as a no-op (skip) locally.
/// </summary>
public class SambaDcIntegrationTests
{
    public static bool Enabled =>
        Environment.GetEnvironmentVariable("PASSRESET_INTEGRATION_LDAP") == "1";

    [Fact]
    public async Task EndToEnd_ChangePassword_ReturnsNull()
    {
        Assert.SkipUnless(Enabled,
            "Integration test disabled. Set PASSRESET_INTEGRATION_LDAP=1 and provide " +
            "appsettings.IntegrationTest.json (or PASSRESET_* env vars) to enable.");

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.IntegrationTest.json", optional: true)
            .AddEnvironmentVariables(prefix: "PASSRESET_")
            .Build();

        var opts = new PasswordChangeOptions();
        config.GetSection("PasswordChangeOptions").Bind(opts);

        var session = new LdapSession(
            hostname: opts.LdapHostnames[0],
            port: opts.LdapPort,
            useLdaps: opts.LdapUseSsl,
            serviceAccountDn: opts.ServiceAccountDn,
            serviceAccountPassword: opts.ServiceAccountPassword,
            trustedThumbprints: opts.LdapTrustedCertificateThumbprints,
            logger: NullLogger<LdapSession>.Instance);

        var provider = new LdapPasswordChangeProvider(
            Options.Create(opts),
            NullLogger<LdapPasswordChangeProvider>.Instance,
            () => session);

        var testUser = Environment.GetEnvironmentVariable("PASSRESET_TEST_USERNAME") ?? "testuser";
        var oldPw    = Environment.GetEnvironmentVariable("PASSRESET_TEST_OLD_PASSWORD") ?? "OldPass1!";
        var newPw    = Environment.GetEnvironmentVariable("PASSRESET_TEST_NEW_PASSWORD") ?? "NewPass1!";

        var result = await provider.PerformPasswordChangeAsync(testUser, oldPw, newPw);

        Assert.Null(result);
    }
}
