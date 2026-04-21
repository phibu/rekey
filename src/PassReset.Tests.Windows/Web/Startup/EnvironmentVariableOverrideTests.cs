using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PassReset.Web.Models;
using Xunit;

namespace PassReset.Tests.Windows.Web.Startup;

/// <summary>
/// WR-03: xUnit v3 parallelizes test classes by default. Because this class mutates
/// <see cref="EnvironmentVariableTarget.Process"/> state (a shared mutable global), any
/// concurrently-executing class that reads <c>SmtpSettings__Password</c> or
/// <c>ClientSettings__Recaptcha__PrivateKey</c> could observe the leaked value.
/// This collection definition disables parallelization for member classes so
/// env-var mutation is serialized without slowing the rest of the suite.
/// </summary>
[CollectionDefinition("EnvVarSerial", DisableParallelization = true)]
public class EnvVarSerialCollection
{
}

/// <summary>
/// STAB-017 — Proves that ASP.NET Core's default <c>AddEnvironmentVariables()</c>
/// host builder picks up the three in-scope secrets via the standard <c>__</c>
/// path delimiter, and that <c>[JsonIgnore]</c> on <c>ClientSettings.Recaptcha.PrivateKey</c>
/// holds regardless of where the value was sourced from (regression guard —
/// Pitfall 5 in 09-RESEARCH.md).
///
/// No production-code changes: this test is a documented contract, not a new feature.
///
/// WR-03: Belongs to the <c>EnvVarSerial</c> collection so process-scoped env-var
/// mutations cannot leak across classes running in parallel.
/// </summary>
[Collection("EnvVarSerial")]
public class EnvironmentVariableOverrideTests : IDisposable
{
    private readonly List<string> _envVarsSet = new();

    private void SetEnv(string name, string? value)
    {
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process);
        _envVarsSet.Add(name);
    }

    public void Dispose()
    {
        foreach (var name in _envVarsSet)
            Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.Process);
        GC.SuppressFinalize(this);
    }

    public sealed class EnvVarFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["WebSettings:UseDebugProvider"]                 = "true",
                    ["WebSettings:EnableHttpsRedirect"]              = "false",
                    ["SmtpSettings:Host"]                            = "smtp.example.com",
                    ["SmtpSettings:Port"]                            = "25",
                    ["SmtpSettings:Username"]                        = "smtp-user",
                    ["SmtpSettings:Password"]                        = "FromAppSettings",
                    ["ClientSettings:Recaptcha:Enabled"]             = "false",
                    ["ClientSettings:Recaptcha:PrivateKey"]          = "FromAppSettings-recaptcha",
                    ["SiemSettings:Syslog:Enabled"]                  = "false",
                    ["SiemSettings:AlertEmail:Enabled"]              = "false",
                    ["EmailNotificationSettings:Enabled"]            = "false",
                    ["PasswordExpiryNotificationSettings:Enabled"]   = "false",
                    ["PasswordChangeOptions:UseAutomaticContext"]    = "true",
                    ["PasswordChangeOptions:PortalLockoutThreshold"] = "0",
                    ["ClientSettings:MinimumDistance"]               = "0",
                });
                // Env var source is added by default via WebApplication.CreateBuilder — do NOT remove.
                config.AddEnvironmentVariables();
            });
        }
    }

    [Fact]
    public void EnvVar_SmtpSettingsPassword_OverridesAppsettings()
    {
        SetEnv("SmtpSettings__Password", "FromEnvVar");

        using var factory = new EnvVarFactory();
        var options = factory.Services.GetRequiredService<IOptions<SmtpSettings>>();

        Assert.Equal("FromEnvVar", options.Value.Password);
    }

    [Fact]
    public async Task EnvVar_RecaptchaPrivateKey_NotLeakedInGetResponse()
    {
        SetEnv("ClientSettings__Recaptcha__PrivateKey", "from-env-recaptcha-secret-do-not-leak");

        using var factory = new EnvVarFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/password");

        Assert.True(response.IsSuccessStatusCode, $"GET /api/password should succeed. Got {response.StatusCode}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("PrivateKey", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("from-env-recaptcha-secret-do-not-leak", body, StringComparison.Ordinal);
    }

    [Fact]
    public void EnvVar_Unset_SmtpPasswordFromAppsettings()
    {
        using var factory = new EnvVarFactory();
        var options = factory.Services.GetRequiredService<IOptions<SmtpSettings>>();

        Assert.Equal("FromAppSettings", options.Value.Password);
    }
}
