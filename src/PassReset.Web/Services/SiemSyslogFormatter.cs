namespace PassReset.Web.Services;

/// <summary>
/// Pure RFC 5424 syslog packet formatter. No I/O. Testable in isolation.
/// Extracted from <see cref="SiemService"/> so the formatting logic can be exercised
/// without sockets or network configuration.
/// </summary>
public static class SiemSyslogFormatter
{
    /// <summary>
    /// Builds an RFC 5424 syslog line using the PassReset structured-data shape
    /// used by <see cref="SiemService"/>.
    /// </summary>
    public static string Format(
        DateTimeOffset timestampUtc,
        int facility,
        int severity,
        string hostname,
        string appName,
        string eventType,
        string username,
        string ipAddress,
        string? detail)
    {
        var priority = (facility * 8) + severity;
        var ts = timestampUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var detailPart = detail != null ? $" detail=\"{EscapeSd(detail)}\"" : string.Empty;

        return $"<{priority}>1 {ts} {hostname} {appName} - - - " +
               $"[PassReset@0 event=\"{eventType}\" user=\"{EscapeSd(username)}\" ip=\"{EscapeSd(ipAddress)}\"{detailPart}]";
    }

    /// <summary>
    /// STAB-015: RFC 5424 STRUCTURED-DATA emission for the <see cref="AuditEvent"/> DTO.
    /// Emits a single SD-ELEMENT with the configured SD-ID and SD-PARAMs
    /// <c>event</c>, <c>outcome</c>, <c>user</c>, and (when non-null) <c>ip</c>, <c>traceId</c>, <c>detail</c>.
    /// All values are escaped via <see cref="EscapeSd"/>.
    /// </summary>
    public static string Format(
        DateTimeOffset timestampUtc,
        int facility,
        int severity,
        string hostname,
        string appName,
        string sdId,
        AuditEvent evt)
    {
        var priority = (facility * 8) + severity;
        var ts = timestampUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        var sd = new System.Text.StringBuilder();
        sd.Append('[').Append(sdId);
        sd.Append(" event=\"").Append(EscapeSd(evt.EventType.ToString())).Append('"');
        sd.Append(" outcome=\"").Append(EscapeSd(evt.Outcome)).Append('"');
        sd.Append(" user=\"").Append(EscapeSd(evt.Username)).Append('"');
        if (!string.IsNullOrEmpty(evt.ClientIp))
            sd.Append(" ip=\"").Append(EscapeSd(evt.ClientIp)).Append('"');
        if (!string.IsNullOrEmpty(evt.TraceId))
            sd.Append(" traceId=\"").Append(EscapeSd(evt.TraceId)).Append('"');
        if (!string.IsNullOrEmpty(evt.Detail))
            sd.Append(" detail=\"").Append(EscapeSd(evt.Detail)).Append('"');
        sd.Append(']');

        return $"<{priority}>1 {ts} {hostname} {appName} - - - {sd}";
    }

    /// <summary>
    /// Escapes RFC 5424 SD-PARAM special characters (backslash, double-quote, closing bracket)
    /// and strips control characters (U+0000–U+001F, U+007F) to prevent syslog injection.
    /// </summary>
    public static string EscapeSd(string value)
    {
        var cleaned = StripControlChars(value);
        return cleaned.Replace("\\", "\\\\", StringComparison.Ordinal)
                      .Replace("\"", "\\\"", StringComparison.Ordinal)
                      .Replace("]",  "\\]",  StringComparison.Ordinal);
    }

    private static string StripControlChars(string input) =>
        string.Create(input.Length, input, static (span, src) =>
        {
            var pos = 0;
            foreach (var ch in src)
            {
                if (ch >= '\x20' && ch != '\x7F')
                    span[pos++] = ch;
            }
            span[pos..].Fill('\0');
        }).TrimEnd('\0');
}
