using Microsoft.Extensions.Logging;

namespace PassReset.Common.LocalPolicy;

/// <summary>
/// Decorator around <see cref="IPasswordChangeProvider"/> that enforces operator-managed
/// local password policy: banned-words substring match and offline HIBP SHA-1 lookup.
/// Both checks run before any AD round-trip. Rejections never log the banned term or
/// attempted password.
/// </summary>
public sealed class LocalPolicyPasswordChangeProvider : IPasswordChangeProvider
{
    private readonly IPasswordChangeProvider _inner;
    private readonly BannedWordsChecker _banned;
    private readonly LocalPwnedPasswordsChecker _pwned;
    private readonly ILogger<LocalPolicyPasswordChangeProvider> _log;

    public LocalPolicyPasswordChangeProvider(
        IPasswordChangeProvider inner,
        BannedWordsChecker banned,
        LocalPwnedPasswordsChecker pwned,
        ILogger<LocalPolicyPasswordChangeProvider> log)
    {
        _inner = inner;
        _banned = banned;
        _pwned = pwned;
        _log = log;
    }

    public async Task<ApiErrorItem?> PerformPasswordChangeAsync(
        string username, string currentPassword, string newPassword)
    {
        if (_banned.Matches(newPassword))
        {
            _log.LogInformation(
                "Banned-word rejection for {Username} (term redacted)", username);
            return new ApiErrorItem(ApiErrorCode.BannedWord,
                "This password is not allowed by local policy.")
            { FieldName = nameof(newPassword) };
        }

        if (await _pwned.ContainsAsync(newPassword))
        {
            _log.LogInformation(
                "Local-pwned rejection for {Username}", username);
            return new ApiErrorItem(ApiErrorCode.LocallyKnownPwned,
                "This password is not allowed by local policy.")
            { FieldName = nameof(newPassword) };
        }

        return await _inner.PerformPasswordChangeAsync(username, currentPassword, newPassword);
    }

    public string? GetUserEmail(string username) => _inner.GetUserEmail(username);

    public IEnumerable<(string Username, string Email, DateTime? PasswordLastSet)> GetUsersInGroup(string groupName) =>
        _inner.GetUsersInGroup(groupName);

    public TimeSpan GetDomainMaxPasswordAge() => _inner.GetDomainMaxPasswordAge();

    public Task<PasswordPolicy?> GetEffectivePasswordPolicyAsync() =>
        _inner.GetEffectivePasswordPolicyAsync();

    public int MeasureNewPasswordDistance(string currentPassword, string newPassword) =>
        _inner.MeasureNewPasswordDistance(currentPassword, newPassword);
}
