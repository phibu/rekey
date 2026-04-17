using System.Reflection;
using System.Text.RegularExpressions;
using PassReset.Web.Services;

namespace PassReset.Tests.Web.Services;

/// <summary>
/// STAB-015 (D-10): Reflection-based proofs that <see cref="AuditEvent"/> is a
/// compile-time allowlist DTO. Adding a property whose name matches secret-shaped
/// tokens — or adding/removing any property — intentionally breaks these tests.
/// </summary>
public class AuditEventRedactionTests
{
    [Fact]
    public void AuditEvent_HasNoSecretLookingProperties()
    {
        var forbidden = new Regex(
            "password|token|secret|privatekey|apikey",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        var leaks = typeof(AuditEvent)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => forbidden.IsMatch(p.Name))
            .Select(p => p.Name)
            .ToList();

        Assert.Empty(leaks);
    }

    [Fact]
    public void AuditEvent_AllowlistedFieldsOnly()
    {
        var expected = new[] { "EventType", "Outcome", "Username", "ClientIp", "TraceId", "Detail" };

        var actual = typeof(AuditEvent)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract") // synthesized by records
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            expected.OrderBy(n => n, StringComparer.Ordinal).ToArray(),
            actual);
    }
}
