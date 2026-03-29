namespace PassReset.Common;

/// <summary>
/// Represents an interface for a password change provider.
/// </summary>
public interface IPasswordChangeProvider
{
    /// <summary>
    /// Performs the password change using the credentials provided.
    /// </summary>
    /// <param name="username">The username.</param>
    /// <param name="currentPassword">The current password.</param>
    /// <param name="newPassword">The new password.</param>
    /// <returns>The API error item or null if the change password operation was successful.</returns>
    Task<ApiErrorItem?> PerformPasswordChangeAsync(string username, string currentPassword, string newPassword);

    /// <summary>
    /// Retrieves the email address for the specified user from the directory.
    /// Returns null if the user is not found or on error.
    /// </summary>
    string? GetUserEmail(string username);

    /// <summary>
    /// Returns user details for all members of the specified AD group (recursive).
    /// Used by the password expiry notification background service.
    /// </summary>
    IEnumerable<(string Username, string Email, DateTime? PasswordLastSet)> GetUsersInGroup(string groupName);

    /// <summary>
    /// Returns the domain maximum password age (maxPwdAge).
    /// Returns TimeSpan.MaxValue if the domain has no password expiry policy.
    /// </summary>
    TimeSpan GetDomainMaxPasswordAge();

    /// <summary>
    /// Computes the Levenshtein distance between two passwords.
    /// </summary>
    /// <param name="currentPassword">The current password.</param>
    /// <param name="newPassword">The new password.</param>
    /// <returns>The distance between the two strings.</returns>
    int MeasureNewPasswordDistance(string currentPassword, string newPassword)
    {
        var n = currentPassword.Length;
        var m = newPassword.Length;
        var d = new int[n + 1, m + 1];

        if (n == 0) return m;
        if (m == 0) return n;

        for (int i = 0; i <= n; d[i, 0] = i++) { }
        for (int j = 0; j <= m; d[0, j] = j++) { }

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (newPassword[j - 1] == currentPassword[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }
}
