using System.DirectoryServices.Protocols;
using PassReset.Common;

namespace PassReset.PasswordProvider.Ldap;

/// <summary>
/// Maps AD LDAP <see cref="ResultCode"/> + Win32 <c>extendedError</c> DWORDs to
/// <see cref="ApiErrorCode"/> values consistent with the Windows provider's
/// <see cref="System.Runtime.InteropServices.COMException"/> mapping.
/// </summary>
/// <remarks>
/// Extended-error DWORDs come from the <c>dataXXXXXXXX</c> hex prefix in the
/// server-supplied <see cref="DirectoryResponse.ErrorMessage"/> on password-related
/// failures. The well-known Windows error codes here are documented at
/// <see href="https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes"/>.
/// </remarks>
public static class LdapErrorMapping
{
    // Well-known Win32 codes surfaced via AD's extendedError DWORD.
    private const uint ERROR_PASSWORD_RESTRICTION   = 0x0000052D;
    private const uint ERROR_ACCOUNT_DISABLED       = 0x00000533;
    private const uint ERROR_LOGON_TYPE_NOT_GRANTED = 0x00000534;
    private const uint ERROR_PASSWORD_MUST_CHANGE   = 0x00000773;
    private const uint ERROR_ACCOUNT_LOCKED_OUT     = 0x00000775;

    public static ApiErrorCode Map(ResultCode resultCode, uint extendedError)
    {
        // Extended-error takes precedence — it's more specific than the generic ResultCode.
        switch (extendedError)
        {
            case ERROR_PASSWORD_RESTRICTION:
                return ApiErrorCode.ComplexPassword;
            case ERROR_ACCOUNT_LOCKED_OUT:
                return ApiErrorCode.PortalLockout;
            case ERROR_ACCOUNT_DISABLED:
            case ERROR_LOGON_TYPE_NOT_GRANTED:
                return ApiErrorCode.ChangeNotPermitted;
            case ERROR_PASSWORD_MUST_CHANGE:
                return ApiErrorCode.PasswordTooRecentlyChanged;
        }

        return (int)resultCode switch
        {
            49 => ApiErrorCode.InvalidCredentials,       // InvalidCredentials
            32 => ApiErrorCode.UserNotFound,             // NoSuchObject
            50 => ApiErrorCode.ChangeNotPermitted,       // InsufficientAccessRights
            19 => ApiErrorCode.ComplexPassword,          // ConstraintViolation
            53 => ApiErrorCode.ChangeNotPermitted,       // UnwillingToPerform
            1  => ApiErrorCode.Generic,                  // OperationsError
            _  => ApiErrorCode.Generic,
        };
    }

    /// <summary>
    /// Parses the Win32 DWORD from an AD <see cref="DirectoryResponse.ErrorMessage"/> string of the
    /// form <c>0000052D: SvcErr: DSID-xxxxxxxx, problem 5003 (WILL_NOT_PERFORM), data 52d, ...</c>.
    /// Returns 0 when no <c>dataXXXX</c> token is present.
    /// </summary>
    public static uint ExtractExtendedError(string? errorMessage)
    {
        if (string.IsNullOrEmpty(errorMessage)) return 0;
        var marker = errorMessage.IndexOf("data ", StringComparison.OrdinalIgnoreCase);
        if (marker < 0) return 0;
        var span = errorMessage.AsSpan(marker + 5);
        // Read hex chars until non-hex.
        var end = 0;
        while (end < span.Length && Uri.IsHexDigit(span[end])) end++;
        if (end == 0) return 0;
        return uint.TryParse(span[..end], System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : 0;
    }
}
