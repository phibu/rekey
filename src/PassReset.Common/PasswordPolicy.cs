namespace PassReset.Common;

/// <summary>
/// Effective default-domain password policy as advertised by AD.
/// MinAgeDays / MaxAgeDays may be 0 when no policy is enforced.
/// </summary>
public sealed record PasswordPolicy(
    int MinLength,
    bool RequiresComplexity,
    int HistoryLength,
    int MinAgeDays,
    int MaxAgeDays);
