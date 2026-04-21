using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.DirectoryServices.ActiveDirectory;
using PassReset.Common;

namespace PassReset.PasswordProvider;

/// <inheritdoc />
/// <summary>
/// Windows Active Directory password change provider using System.DirectoryServices.
/// Requires a domain-joined Windows Server and the DirectoryServices runtime.
/// </summary>
public sealed class PasswordChangeProvider : IPasswordChangeProvider
{
    private readonly PasswordChangeOptions _options;
    private readonly ILogger<PasswordChangeProvider> _logger;
    private readonly PwnedPasswordChecker _pwnedChecker;
    private readonly IPrincipalContextFactory _contextFactory;
    private IdentityType _idType = IdentityType.UserPrincipalName;

    public PasswordChangeProvider(
        ILogger<PasswordChangeProvider> logger,
        IOptions<PasswordChangeOptions> options,
        PwnedPasswordChecker pwnedChecker,
        IPrincipalContextFactory contextFactory)
    {
        _logger  = logger;
        _options = options.Value;
        _pwnedChecker = pwnedChecker;
        _contextFactory = contextFactory;
        SetIdType();
    }

    /// <inheritdoc />
    public async Task<ApiErrorItem?> PerformPasswordChangeAsync(string username, string currentPassword, string newPassword)
    {
        using var flowScope = _logger.BeginScope(new { Step = "password-change-flow" });

        try
        {
            using var principalContext = AcquirePrincipalContext();

            // ─ Step: user-lookup ────────────────────────────────────────────────
            UserPrincipal? userPrincipal;
            using (_logger.BeginScope(new { Step = "user-lookup" }))
            {
                var lookupSw = Stopwatch.StartNew();
                _logger.LogDebug("user-lookup: start");
                try
                {
                    userPrincipal = FindUser(principalContext, username);
                }
                finally
                {
                    _logger.LogDebug("user-lookup: complete duration={ElapsedMs}", lookupSw.ElapsedMilliseconds);
                }
            }

            if (userPrincipal == null)
            {
                _logger.LogWarning("User principal ({Username}) not found", username);
                return new ApiErrorItem(ApiErrorCode.UserNotFound);
            }

            // ─ AD-context scope — only opened once the principal is resolved (ConnectedServer is null pre-bind).
            //   Properties flow into every log event below this point via Serilog scope inheritance.
            using var adContext = _logger.BeginScope(new Dictionary<string, object>
            {
                ["Domain"] = _options.DefaultDomain ?? "unknown",
                ["DomainController"] = principalContext.ConnectedServer ?? "unknown",
                ["IdentityType"] = _idType.ToString(),
                ["UserCannotChangePassword"] = userPrincipal.UserCannotChangePassword,
                ["LastPasswordSetUtc"] = userPrincipal.LastPasswordSet?.ToUniversalTime().ToString("o") ?? "null",
            });

            // Enforce AD minimum password length before touching AD
            var minPwdLength = AcquireDomainPasswordLength();
            if (newPassword.Length < minPwdLength)
            {
                _logger.LogError("New password is shorter than the AD minimum password length ({Min})", minPwdLength);
                return new ApiErrorItem(ApiErrorCode.ComplexPassword);
            }

            // Reject passwords found in public breach databases (async — does not block a thread pool thread)
            var pwnedResult = await _pwnedChecker.IsPwnedPasswordAsync(newPassword).ConfigureAwait(false);
            if (pwnedResult == true)
            {
                _logger.LogError("New password for {Username} is publicly known (HaveIBeenPwned)", username);
                return new ApiErrorItem(ApiErrorCode.PwnedPassword);
            }
            if (pwnedResult == null)
            {
                if (_options.FailOpenOnPwnedCheckUnavailable)
                {
                    _logger.LogWarning("HaveIBeenPwned API was unreachable for user {Username} — breach check skipped (fail-open)", username);
                }
                else
                {
                    _logger.LogWarning("HaveIBeenPwned API was unreachable for user {Username} — password change blocked", username);
                    return new ApiErrorItem(ApiErrorCode.PwnedPasswordCheckFailed);
                }
            }

            _logger.LogInformation("PerformPasswordChange for user {Username}", username);

            var groupItem = ValidateGroups(userPrincipal);
            if (groupItem != null)
                return groupItem;

            if (userPrincipal.UserCannotChangePassword)
            {
                _logger.LogWarning("User principal {Username} cannot change password (UserCannotChangePassword flag)", username);
                return new ApiErrorItem(ApiErrorCode.ChangeNotPermitted);
            }

            // Enforce minimum password age — users with must-change flag (LastPasswordSet == null) are exempt
            if (userPrincipal.LastPasswordSet != null)
            {
                var ageItem = CheckMinimumPasswordAge(userPrincipal);
                if (ageItem != null) return ageItem;
            }

            // Capture must-change-at-next-logon state BEFORE the password change
            var mustChangeFlag = IsMustChangePasswordFlagSet(userPrincipal);

            var identity = userPrincipal.UserPrincipalName ?? userPrincipal.SamAccountName
                ?? throw new InvalidOperationException($"User has neither UserPrincipalName nor SamAccountName (input: {username})");

            if (!ValidateUserCredentials(identity, currentPassword, principalContext))
            {
                _logger.LogWarning("Invalid current password for user {Username}", username);
                return new ApiErrorItem(ApiErrorCode.InvalidCredentials);
            }

            // STAB-004: defense-in-depth pre-check. Existing COMException catch at the bind
            // boundary remains the floor (D-05). This is the fast path (D-04).
            var preCheck = PreCheckMinPwdAge(username);
            if (preCheck != null) return preCheck;

            // ─ Step: change-password-internal ────────────────────────────────────
            using (_logger.BeginScope(new { Step = "change-password-internal" }))
            {
                var cpiSw = Stopwatch.StartNew();
                _logger.LogDebug("change-password-internal: start");
                try
                {
                    ChangePasswordInternal(currentPassword, newPassword, userPrincipal);
                }
                finally
                {
                    _logger.LogDebug("change-password-internal: complete duration={ElapsedMs}", cpiSw.ElapsedMilliseconds);
                }
            }

            // ─ Step: save ───────────────────────────────────────────────────────
            using (_logger.BeginScope(new { Step = "save" }))
            {
                var saveSw = Stopwatch.StartNew();
                _logger.LogDebug("save: start");
                try
                {
                    userPrincipal.Save();
                }
                finally
                {
                    _logger.LogDebug("save: complete duration={ElapsedMs}", saveSw.ElapsedMilliseconds);
                }
            }

            // Clear the "must change at next logon" flag only after a confirmed successful save
            if (_options.ClearMustChangePasswordFlag && mustChangeFlag)
                ClearMustChangeFlag(userPrincipal);

            _logger.LogInformation("Password changed successfully for user {Username}", username);
        }
        catch (PasswordException passwordEx)
        {
            var item = new ApiErrorItem(ApiErrorCode.ComplexPassword, passwordEx.Message);
            ExceptionChainLogger.LogExceptionChain(_logger, passwordEx,
                "Password complexity error for user {Username}", username);
            return item;
        }
        catch (PrincipalOperationException principalEx)
        {
            // Distinct targeted catch (D-02): default Serilog destructure, NOT the chain walker.
            // Response shape matches the existing generic catch below to preserve user-facing behavior.
            _logger.LogWarning(principalEx,
                "Principal operation failed for user {Username} hresult={HResult} errorCode={ErrorCode}",
                username, $"0x{principalEx.HResult:X8}", principalEx.ErrorCode);
            return new ApiErrorItem(ApiErrorCode.Generic, "An unexpected error occurred. Please contact IT Support.");
        }
        catch (Exception ex)
        {
            var item = ex is ApiErrorException apiError
                ? apiError.ToApiErrorItem()
                : new ApiErrorItem(ApiErrorCode.Generic, "An unexpected error occurred. Please contact IT Support.");

            if (ex is System.DirectoryServices.DirectoryServicesCOMException comEx)
            {
                ExceptionChainLogger.LogExceptionChain(_logger, comEx,
                    "Unexpected AD COM error for user {Username}", username);
            }
            else
            {
                _logger.LogError(ex, "Unexpected error during password change for user {Username}", username);
            }
            return item;
        }

        return null;
    }

    /// <inheritdoc />
    public string? GetUserEmail(string username)
    {
        try
        {
            using var ctx = AcquirePrincipalContext();
            var user = FindUser(ctx, username);
            if (user == null) return null;

            return _options.NotificationEmailStrategy switch
            {
                EmailAddressStrategy.UserPrincipalName     => user.UserPrincipalName,
                EmailAddressStrategy.SamAccountNameAtDomain => BuildSamAtDomain(user),
                EmailAddressStrategy.Custom                => BuildCustomEmail(user),
                _                                          => user.EmailAddress, // Mail (default)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve email for user {Username}", username);
            return null;
        }
    }

    private string? BuildSamAtDomain(UserPrincipal user)
    {
        var sam = user.SamAccountName;
        if (string.IsNullOrWhiteSpace(sam)) return null;

        var domain = !string.IsNullOrWhiteSpace(_options.NotificationEmailDomain)
            ? _options.NotificationEmailDomain
            : _options.DefaultDomain;

        return string.IsNullOrWhiteSpace(domain) ? null : $"{sam}@{domain}";
    }

    private string? BuildCustomEmail(UserPrincipal user)
    {
        if (string.IsNullOrWhiteSpace(_options.NotificationEmailTemplate)) return null;

        return _options.NotificationEmailTemplate
            .Replace("{samaccountname}",   user.SamAccountName      ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{userprincipalname}", user.UserPrincipalName   ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{mail}",             user.EmailAddress         ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{defaultdomain}",    _options.DefaultDomain,                   StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public IEnumerable<(string Username, string Email, DateTime? PasswordLastSet)> GetUsersInGroup(string groupName)
    {
        var results = new List<(string, string, DateTime?)>();

        try
        {
            using var ctx   = AcquirePrincipalContext();
            var group = GroupPrincipal.FindByIdentity(ctx, groupName);

            if (group == null)
            {
                _logger.LogWarning("AD group {GroupName} not found", groupName);
                return results;
            }

            foreach (var member in group.GetMembers(recursive: true))
            {
                if (member is UserPrincipal user && !string.IsNullOrWhiteSpace(user.EmailAddress))
                    results.Add((user.SamAccountName ?? user.Name, user.EmailAddress, user.LastPasswordSet));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate members of group {GroupName}", groupName);
        }

        return results;
    }

    /// <inheritdoc />
    public TimeSpan GetDomainMaxPasswordAge()
    {
        using var entry = AcquireDomainEntry();
        var rawValue = entry.Properties["maxPwdAge"].Value;

        // maxPwdAge is a negative 100-nanosecond interval; 0 means no expiry policy
        if (rawValue is long ticks && ticks != 0)
            return TimeSpan.FromTicks(Math.Abs(ticks));

        return TimeSpan.MaxValue;
    }

    /// <inheritdoc />
    public Task<PasswordPolicy?> GetEffectivePasswordPolicyAsync()
    {
        try
        {
            using var entry = AcquireDomainEntry();

            var minLen     = (int)(entry.Properties["minPwdLength"].Value     ?? 0);
            var pwdProps   = Convert.ToInt64(entry.Properties["pwdProperties"].Value ?? 0L);
            var historyLen = (int)(entry.Properties["pwdHistoryLength"].Value ?? 0);

            // Same shape as maxPwdAge — System.DirectoryServices yields a long for these
            // attributes (negative 100-nanosecond intervals).
            var minAgeTicks = entry.Properties["minPwdAge"].Value is long min ? Math.Abs(min) : 0L;
            var maxAgeTicks = entry.Properties["maxPwdAge"].Value is long max ? Math.Abs(max) : 0L;

            const bool DOMAIN_PASSWORD_COMPLEX = true; // for readability
            var requiresComplexity = (pwdProps & 0x1L) != 0L && DOMAIN_PASSWORD_COMPLEX;

            var minAgeDays = (int)TimeSpan.FromTicks(minAgeTicks).TotalDays;
            var maxAgeDays = (int)TimeSpan.FromTicks(maxAgeTicks).TotalDays;

            return Task.FromResult<PasswordPolicy?>(
                new PasswordPolicy(minLen, requiresComplexity, historyLen, minAgeDays, maxAgeDays));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query effective password policy from AD");
            return Task.FromResult<PasswordPolicy?>(null);
        }
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private bool ValidateUserCredentials(string upn, string currentPassword, PrincipalContext principalContext)
    {
        if (principalContext.ValidateCredentials(upn, currentPassword))
            return true;

        if (NativeMethods.LogonUser(upn, string.Empty, currentPassword,
                NativeMethods.LogonTypes.Network, NativeMethods.LogonProviders.Default, out var token))
        {
            token.Dispose();
            return true;
        }

        var errorCode = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        _logger.LogDebug("ValidateUserCredentials Win32 error code: {ErrorCode}", errorCode);

        // These codes mean the password is correct but expired / must-change — allow the change to proceed
        return errorCode is NativeMethods.ErrorPasswordMustChange or NativeMethods.ErrorPasswordExpired;
    }

    private string FixUsernameWithDomain(string username)
    {
        if (_idType != IdentityType.UserPrincipalName) return username;

        var parts  = username.Split('@', StringSplitOptions.RemoveEmptyEntries);
        var domain = parts.Length > 1 ? parts[1] : _options.DefaultDomain;

        return string.IsNullOrWhiteSpace(domain) || parts.Length > 1 ? username : $"{username}@{domain}";
    }

    private ApiErrorItem? ValidateGroups(UserPrincipal userPrincipal)
    {
        var hasAllowList = _options.AllowedAdGroups is { Count: > 0 };
        var hasBlockList = _options.RestrictedAdGroups is { Count: > 0 };

        if (!hasAllowList && !hasBlockList)
            return null; // No restrictions configured — permit all

        PrincipalSearchResult<Principal> groups;

        try
        {
            groups = userPrincipal.GetGroups();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(new EventId(887), ex,
                "GetGroups() failed for {User} — falling back to GetAuthorizationGroups()",
                userPrincipal.UserPrincipalName);
            try
            {
                groups = userPrincipal.GetAuthorizationGroups();
            }
            catch (Exception ex2)
            {
                _logger.LogError(new EventId(888), ex2,
                    "GetAuthorizationGroups() also failed for {User} — {Action} per AllowOnGroupCheckFailure={Flag}",
                    userPrincipal.UserPrincipalName,
                    _options.AllowOnGroupCheckFailure ? "allowing" : "denying",
                    _options.AllowOnGroupCheckFailure);
                return _options.AllowOnGroupCheckFailure
                    ? null
                    : new ApiErrorItem(ApiErrorCode.ChangeNotPermitted,
                        "Group membership check failed — password change denied for safety");
            }
        }

        var groupNames = groups.Select(g => g.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Block list takes priority
        if (hasBlockList && _options.RestrictedAdGroups!.Any(r => groupNames.Contains(r)))
        {
            _logger.LogWarning("User {User} is a member of a restricted group", userPrincipal.SamAccountName);
            return new ApiErrorItem(ApiErrorCode.ChangeNotPermitted, "User is in a restricted group");
        }

        // Allow list: user must belong to at least one allowed group
        if (hasAllowList && !_options.AllowedAdGroups!.Any(a => groupNames.Contains(a)))
        {
            _logger.LogWarning("User {User} is not in any allowed group", userPrincipal.SamAccountName);
            return new ApiErrorItem(ApiErrorCode.ChangeNotPermitted, "User is not in an allowed group");
        }

        return null;
    }

    private ApiErrorItem? CheckMinimumPasswordAge(UserPrincipal userPrincipal)
    {
        if (!_options.EnforceMinimumPasswordAge)
            return null;

        var minAge = AcquireDomainMinPasswordAge();
        if (minAge == TimeSpan.Zero)
            return null; // No minimum age policy configured

        var lastSet = userPrincipal.LastPasswordSet;
        if (lastSet == null)
            return null; // Must-change flag set — exempt from age check

        var elapsed = DateTime.UtcNow - lastSet.Value.ToUniversalTime();
        if (elapsed >= minAge)
            return null;

        var hoursRemaining = (int)Math.Ceiling((minAge - elapsed).TotalHours);
        _logger.LogWarning(
            "User {User} must wait {Hours} more hour(s) before changing password (minPwdAge)",
            userPrincipal.SamAccountName, hoursRemaining);

        return new ApiErrorItem(ApiErrorCode.PasswordTooYoung,
            $"Password cannot be changed for another {hoursRemaining} hour(s)");
    }

    private static bool IsMustChangePasswordFlagSet(UserPrincipal userPrincipal) =>
        userPrincipal.LastPasswordSet == null;

    private void ClearMustChangeFlag(UserPrincipal userPrincipal)
    {
        var entry = (DirectoryEntry)userPrincipal.GetUnderlyingObject();
        var prop  = entry.Properties["pwdLastSet"];

        if (prop == null)
        {
            _logger.LogWarning("pwdLastSet property not found for user {User}", userPrincipal.SamAccountName);
            return;
        }

        try
        {
            prop.Value = -1;
            entry.CommitChanges();
            _logger.LogInformation("Cleared must-change-password flag for user {User}", userPrincipal.SamAccountName);
        }
        catch (Exception ex)
        {
            // Non-fatal — password already changed. Log and continue so the user gets a success response.
            _logger.LogError(ex, "Failed to clear must-change-password flag for user {User}", userPrincipal.SamAccountName);
        }
    }

    private void ChangePasswordInternal(string currentPassword, string newPassword, AuthenticablePrincipal userPrincipal)
    {
        try
        {
            userPrincipal.ChangePassword(currentPassword, newPassword);
        }
        catch (System.Runtime.InteropServices.COMException comEx)
        {
            // BUG-002: classify well-known HResults BEFORE any SetPassword fallback.
            // Min-age rejection must never be routed through SetPassword (which bypasses history).
            const int E_ACCESSDENIED = unchecked((int)0x80070005);
            const int ERROR_DS_CONSTRAINT_VIOLATION = unchecked((int)0x8007202F);

            if (comEx.HResult == E_ACCESSDENIED || comEx.HResult == ERROR_DS_CONSTRAINT_VIOLATION)
            {
                ExceptionChainLogger.LogExceptionChain(_logger, comEx,
                    "AD rejected ChangePassword for {User} with HRESULT=0x{Hex:X8}; message={Message}. " +
                    "Treating as minimum-password-age violation. If this user IS allowed to change password, " +
                    "verify the service account has the 'Change Password' extended right.",
                    userPrincipal.SamAccountName,
                    comEx.HResult,
                    comEx.Message);

                throw new ApiErrorException(
                    "Your password was changed too recently. Please wait before trying again.",
                    ApiErrorCode.PasswordTooRecentlyChanged);
            }

            // COMException is thrown by System.DirectoryServices when the LDAP operation is
            // rejected at the protocol level — typically because the service account has
            // "Reset Password" rights but not "Change Password" rights on the target object.
            // SetPassword is an administrative reset and is only attempted with explicit credentials
            // when explicitly opted in, because it bypasses AD password history enforcement.
            if (_options.UseAutomaticContext || !_options.AllowSetPasswordFallback)
            {
                ExceptionChainLogger.LogExceptionChain(_logger, comEx,
                    "ChangePassword failed (HRESULT={HResult}); SetPassword fallback is {Status} " +
                    "(UseAutomaticContext={UseAutomaticContext}, AllowSetPasswordFallback={AllowSetPasswordFallback})",
                    comEx.HResult,
                    _options.AllowSetPasswordFallback ? "disabled (auto context)" : "disabled",
                    _options.UseAutomaticContext,
                    _options.AllowSetPasswordFallback);
                throw;
            }

            ExceptionChainLogger.LogExceptionChain(_logger, comEx,
                "ChangePassword failed (HRESULT={HResult}), falling back to SetPassword for user {User}. Note: SetPassword bypasses AD password history.",
                comEx.HResult, userPrincipal.Name);
            userPrincipal.SetPassword(newPassword);
            _logger.LogDebug("Password set via SetPassword fallback for user {User}", userPrincipal.Name);
        }
    }

    /// <summary>
    /// Resolves a <see cref="UserPrincipal"/> by trying each attribute in
    /// <see cref="PasswordChangeOptions.AllowedUsernameAttributes"/> in order.
    /// Falls back to the legacy <see cref="_idType"/> / <see cref="FixUsernameWithDomain"/> path
    /// when <see cref="PasswordChangeOptions.AllowedUsernameAttributes"/> is empty.
    /// </summary>
    private UserPrincipal? FindUser(PrincipalContext ctx, string input)
    {
        if (_options.AllowedUsernameAttributes.Length == 0)
        {
            var fixed_ = FixUsernameWithDomain(input);
            return _contextFactory.FindUser(ctx, _idType, fixed_);
        }

        foreach (var attr in _options.AllowedUsernameAttributes)
        {
            var user = TryFindByAttribute(ctx, attr.Trim().ToLowerInvariant(), input);
            if (user != null) return user;
        }

        return null;
    }

    private UserPrincipal? TryFindByAttribute(PrincipalContext ctx, string attr, string input)
    {
        return attr switch
        {
            "samaccountname" or "sam" => FindBySamAccountName(ctx, input),
            "userprincipalname" or "upn" => FindByUserPrincipalName(ctx, input),
            "mail" or "email" => FindByMail(ctx, input),
            _ => null,
        };
    }

    private UserPrincipal? FindBySamAccountName(PrincipalContext ctx, string input)
    {
        // Strip domain prefix/suffix in all three common formats:
        //   DOMAIN\jdoe  → jdoe
        //   jdoe@corp.com → jdoe
        //   jdoe          → jdoe (unchanged)
        var sam = input.Contains('\\') ? input[(input.IndexOf('\\') + 1)..] :
                  input.Contains('@')  ? input[..input.IndexOf('@')]          :
                  input;
        return _contextFactory.FindUser(ctx, IdentityType.SamAccountName, sam);
    }

    private UserPrincipal? FindByUserPrincipalName(PrincipalContext ctx, string input)
    {
        // Append DefaultDomain when the caller supplied a bare username (no @).
        var upn = !input.Contains('@') && !string.IsNullOrEmpty(_options.DefaultDomain)
            ? $"{input}@{_options.DefaultDomain}"
            : input;
        return _contextFactory.FindUser(ctx, IdentityType.UserPrincipalName, upn);
    }

    private static UserPrincipal? FindByMail(PrincipalContext ctx, string mail)
    {
        // mail is not a native IdentityType — use query-by-example via PrincipalSearcher.
        using var template = new UserPrincipal(ctx) { EmailAddress = mail };
        using var searcher = new PrincipalSearcher(template);
        return searcher.FindOne() as UserPrincipal;
    }

    private void SetIdType()
    {
        _idType = _options.IdTypeForUser?.Trim().ToLowerInvariant() switch
        {
            "distinguishedname" or "distinguished name" or "dn" => IdentityType.DistinguishedName,
            "globallyuniqueidentifier" or "globally unique identifier" or "guid" => IdentityType.Guid,
            "name" or "nm" => IdentityType.Name,
            "samaccountname" or "accountname" or "sam account name" or "sam account" or "sam" => IdentityType.SamAccountName,
            "securityidentifier" or "security identifier" or "securityid" or "secid" or "sid" => IdentityType.Sid,
            _ => IdentityType.UserPrincipalName,
        };
    }

    private PrincipalContext AcquirePrincipalContext()
    {
        if (_options.UseAutomaticContext)
        {
            _logger.LogDebug("Acquiring domain context via AutomaticContext");
            return _contextFactory.CreateDomainContext();
        }

        var server = $"{_options.LdapHostnames.First()}:{_options.LdapPort}";
        _logger.LogDebug("Acquiring domain context for {Server} (SSL={UseSsl})", server, _options.LdapUseSsl);

        var contextOptions = _options.LdapUseSsl
            ? ContextOptions.Negotiate | ContextOptions.SecureSocketLayer
            : ContextOptions.Negotiate;

        return _contextFactory.CreateDomainContext(
            server: server,
            container: null,
            options: contextOptions,
            username: _options.LdapUsername,
            password: _options.LdapPassword);
    }

    private DirectoryEntry AcquireDomainEntry()
    {
        if (_options.UseAutomaticContext)
            return Domain.GetCurrentDomain().GetDirectoryEntry();

        var scheme   = _options.LdapUseSsl ? "LDAPS" : "LDAP";
        var authType = _options.LdapUseSsl ? AuthenticationTypes.SecureSocketsLayer : AuthenticationTypes.Secure;
        return new DirectoryEntry(
            $"{scheme}://{_options.LdapHostnames.First()}:{_options.LdapPort}",
            _options.LdapUsername,
            _options.LdapPassword,
            authType);
    }

    private int AcquireDomainPasswordLength()
    {
        using var entry = AcquireDomainEntry();
        return entry.Properties["minPwdLength"].Value is int len ? len : 0;
    }

    private TimeSpan AcquireDomainMinPasswordAge()
    {
        using var entry  = AcquireDomainEntry();
        var rawValue = entry.Properties["minPwdAge"].Value;

        // minPwdAge is stored as a negative 100-nanosecond interval; 0 means no minimum age
        if (rawValue is long ticks && ticks != 0)
            return TimeSpan.FromTicks(Math.Abs(ticks));

        return TimeSpan.Zero;
    }

    /// <summary>
    /// STAB-004 pre-check. Returns non-null when the user's pwdLastSet is newer than the
    /// domain minPwdAge. Matches the existing service-account-with-fallback pattern
    /// (D-06) by calling AcquirePrincipalContext(). Never throws — returns null on any
    /// unreadable state so the existing COMException catch remains the
    /// defense-in-depth floor (D-05).
    /// </summary>
    private ApiErrorItem? PreCheckMinPwdAge(string username)
    {
        try
        {
            var minAge = AcquireDomainMinPasswordAge();
            if (minAge <= TimeSpan.Zero) return null;

            using var ctx = AcquirePrincipalContext();
            // WR-03: reuse the FindUser fallback chain so the pre-check resolves
            // the same user identities the main PerformPasswordChangeAsync path
            // does (sam / UPN / mail per AllowedUsernameAttributes, plus
            // DOMAIN\user and user@domain input forms).
            using var user = FindUser(ctx, username);
            if (user == null) return null;

            var lastSet = user.LastPasswordSet;
            if (lastSet == null) return null; // must-change-at-next-logon — exempt

            var result = EvaluateMinPwdAge(lastSet.Value, minAge, DateTime.UtcNow);
            if (result != null)
            {
                _logger.LogWarning(
                    "STAB-004 pre-check blocked consecutive change for {User}: lastSet={LastSet} minAge={MinAge}",
                    username, lastSet.Value.ToUniversalTime(), minAge);
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "STAB-004 pre-check failed for {User}; falling through to bind and catch path",
                username);
            return null;
        }
    }

    /// <summary>
    /// Pure logic extracted for unit testing. Evaluates whether <paramref name="lastSet"/>
    /// violates <paramref name="minAge"/> relative to <paramref name="now"/>.
    /// Returns null when the age requirement is satisfied.
    /// </summary>
    internal static ApiErrorItem? EvaluateMinPwdAge(DateTime lastSet, TimeSpan minAge, DateTime now)
    {
        var elapsed = now - lastSet.ToUniversalTime();
        if (elapsed >= minAge) return null;

        var remaining = minAge - elapsed;
        int remainingMinutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        int policyMinutes    = Math.Max(1, (int)Math.Ceiling(minAge.TotalMinutes));
        int elapsedMinutes   = Math.Max(0, (int)Math.Floor(elapsed.TotalMinutes));

        var message =
            $"Password was changed {elapsedMinutes} minute(s) ago; " +
            $"AD policy requires {policyMinutes} minute(s) between changes " +
            $"({remainingMinutes} minute(s) remaining).";

        return new ApiErrorItem(ApiErrorCode.PasswordTooRecentlyChanged, message);
    }
}
