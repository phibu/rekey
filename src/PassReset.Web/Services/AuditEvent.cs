namespace PassReset.Web.Services;

/// <summary>
/// STAB-015 (D-10) allowlist DTO for audit events. No secret fields exist on this
/// type by design — compile-time redaction. Do NOT add Password, Token, PrivateKey,
/// Secret, or ApiKey properties; doing so violates STAB-015's redaction guarantee
/// and breaks the reflection test in AuditEventRedactionTests.
/// </summary>
/// <param name="EventType">Category of the security event (<see cref="SiemEventType"/>).</param>
/// <param name="Outcome">Human-readable outcome label (e.g. "Success", "Fail").</param>
/// <param name="Username">AD username or principal involved in the event.</param>
/// <param name="ClientIp">Optional remote client IP address.</param>
/// <param name="TraceId">Optional correlation/trace identifier for cross-log joining.</param>
/// <param name="Detail">Optional free-form detail (must not contain secrets).</param>
public sealed record AuditEvent(
    SiemEventType EventType,
    string Outcome,
    string Username,
    string? ClientIp = null,
    string? TraceId = null,
    string? Detail = null);
