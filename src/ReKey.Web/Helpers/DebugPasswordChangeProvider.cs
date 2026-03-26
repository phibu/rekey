using ReKey.Common;

namespace ReKey.Web.Helpers;

/// <summary>
/// No-op password change provider used in development.
/// Selected at runtime when WebSettings.UseDebugProvider is true.
/// Returns deterministic errors based on well-known test usernames,
/// allowing UI flows to be exercised without an Active Directory connection.
/// </summary>
internal sealed class DebugPasswordChangeProvider : IPasswordChangeProvider
{
    private readonly ILogger<DebugPasswordChangeProvider> _logger;

    public DebugPasswordChangeProvider(ILogger<DebugPasswordChangeProvider> logger)
    {
        _logger = logger;
    }

    public ApiErrorItem? PerformPasswordChange(string username, string currentPassword, string newPassword)
    {
        var localPart = username.Contains('@')
            ? username[..username.IndexOf('@')]
            : username;

        _logger.LogDebug("DebugPasswordChangeProvider: PerformPasswordChange called for user={User}", localPart);

        return localPart switch
        {
            "error"             => new ApiErrorItem(ApiErrorCode.Generic, "Simulated generic error"),
            "changeNotPermitted"=> new ApiErrorItem(ApiErrorCode.ChangeNotPermitted),
            "fieldMismatch"     => new ApiErrorItem(ApiErrorCode.FieldMismatch),
            "fieldRequired"     => new ApiErrorItem(ApiErrorCode.FieldRequired),
            "invalidCaptcha"    => new ApiErrorItem(ApiErrorCode.InvalidCaptcha),
            "invalidCredentials"=> new ApiErrorItem(ApiErrorCode.InvalidCredentials),
            "invalidDomain"     => new ApiErrorItem(ApiErrorCode.InvalidDomain),
            "userNotFound"      => new ApiErrorItem(ApiErrorCode.UserNotFound),
            "ldapProblem"       => new ApiErrorItem(ApiErrorCode.LdapProblem),
            "pwnedPassword"     => new ApiErrorItem(ApiErrorCode.PwnedPassword),
            "passwordTooYoung"  => new ApiErrorItem(ApiErrorCode.PasswordTooYoung),
            _                   => null   // success
        };
    }

    public string? GetUserEmail(string username) =>
        $"{username.Split('@')[0]}@debug.local";

    public IEnumerable<(string Username, string Email, DateTime? PasswordLastSet)> GetUsersInGroup(string groupName)
    {
        _logger.LogDebug("DebugPasswordChangeProvider: GetUsersInGroup called (returning empty)");
        return [];
    }

    public TimeSpan GetDomainMaxPasswordAge() => TimeSpan.FromDays(90);
}
