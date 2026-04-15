using PassReset.Web.Services;

namespace PassReset.Tests.Web.Services;

public class SiemSyslogFormatterTests
{
    private static readonly DateTimeOffset FixedTs =
        new(2026, 4, 15, 12, 34, 56, 789, TimeSpan.Zero);

    [Fact]
    public void Format_ProducesRfc5424HeaderWithComputedPriority()
    {
        // facility 1, severity 4 → PRI = 12
        var line = SiemSyslogFormatter.Format(
            FixedTs, facility: 1, severity: 4,
            hostname: "host", appName: "PassReset",
            eventType: "InvalidCredentials",
            username: "alice", ipAddress: "10.0.0.1", detail: null);

        Assert.StartsWith("<12>1 2026-04-15T12:34:56.789Z host PassReset - - - ", line, StringComparison.Ordinal);
        Assert.Contains("event=\"InvalidCredentials\"", line, StringComparison.Ordinal);
        Assert.Contains("user=\"alice\"", line, StringComparison.Ordinal);
        Assert.Contains("ip=\"10.0.0.1\"", line, StringComparison.Ordinal);
        Assert.DoesNotContain("detail=", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_EmitsDetailWhenProvided()
    {
        var line = SiemSyslogFormatter.Format(
            FixedTs, facility: 1, severity: 5,
            hostname: "h", appName: "PassReset",
            eventType: "Generic",
            username: "u", ipAddress: "i",
            detail: "something happened");

        Assert.Contains("detail=\"something happened\"", line, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 5, 13)]
    [InlineData(1, 4, 12)]
    [InlineData(16, 6, 134)]
    public void Format_PriorityIsFacilityTimesEightPlusSeverity(int facility, int severity, int expectedPri)
    {
        var line = SiemSyslogFormatter.Format(
            FixedTs, facility, severity,
            hostname: "h", appName: "a",
            eventType: "Generic", username: "u", ipAddress: "i", detail: null);

        Assert.StartsWith($"<{expectedPri}>1 ", line, StringComparison.Ordinal);
    }

    [Fact]
    public void EscapeSd_EscapesBackslashQuoteAndClosingBracket()
    {
        Assert.Equal("\\\\", SiemSyslogFormatter.EscapeSd("\\"));
        Assert.Equal("\\\"", SiemSyslogFormatter.EscapeSd("\""));
        Assert.Equal("\\]", SiemSyslogFormatter.EscapeSd("]"));
        Assert.Equal("ok", SiemSyslogFormatter.EscapeSd("ok"));
    }

    [Fact]
    public void EscapeSd_StripsControlCharacters()
    {
        var dirty = "a\u0001b\u001Fc\u007Fd";
        var clean = SiemSyslogFormatter.EscapeSd(dirty);
        Assert.Equal("abcd", clean);
    }

    [Fact]
    public void Format_UserWithInjectedClosingBracketIsEscaped()
    {
        var line = SiemSyslogFormatter.Format(
            FixedTs, 1, 5, "h", "a", "Generic",
            username: "evil]injected", ipAddress: "i", detail: null);

        // The closing bracket must have been escaped so it cannot terminate the SD element early.
        Assert.Contains("user=\"evil\\]injected\"", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_IsDeterministicForFixedInputs()
    {
        var first = SiemSyslogFormatter.Format(
            FixedTs, 1, 5, "host", "PassReset", "PasswordChanged", "alice", "10.0.0.1", null);
        var second = SiemSyslogFormatter.Format(
            FixedTs, 1, 5, "host", "PassReset", "PasswordChanged", "alice", "10.0.0.1", null);

        Assert.Equal(first, second);
    }
}
