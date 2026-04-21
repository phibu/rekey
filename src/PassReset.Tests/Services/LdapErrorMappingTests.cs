using System.DirectoryServices.Protocols;
using PassReset.Common;
using PassReset.PasswordProvider.Ldap;
using Xunit;

namespace PassReset.Tests.Services;

public class LdapErrorMappingTests
{
    [Theory]
    [InlineData(49, 0u, ApiErrorCode.InvalidCredentials)]                                      // InvalidCredentials
    [InlineData(32, 0u, ApiErrorCode.UserNotFound)]                                           // NoSuchObject
    [InlineData(50, 0u, ApiErrorCode.ChangeNotPermitted)]                                     // InsufficientAccessRights
    [InlineData(53, 0x0000052Du, ApiErrorCode.ComplexPassword)]                              // UnwillingToPerform + ERROR_PASSWORD_RESTRICTION
    [InlineData(19, 0x0000052Du, ApiErrorCode.ComplexPassword)]                              // ConstraintViolation + ERROR_PASSWORD_RESTRICTION
    [InlineData(53, 0x00000775u, ApiErrorCode.PortalLockout)]                                // UnwillingToPerform + ERROR_ACCOUNT_LOCKED_OUT
    [InlineData(53, 0x00000533u, ApiErrorCode.ChangeNotPermitted)]                           // UnwillingToPerform + ERROR_ACCOUNT_DISABLED
    [InlineData(53, 0x00000534u, ApiErrorCode.ChangeNotPermitted)]                           // UnwillingToPerform + ERROR_LOGON_TYPE_NOT_GRANTED
    [InlineData(53, 0x00000773u, ApiErrorCode.PasswordTooRecentlyChanged)]                   // UnwillingToPerform + ERROR_PASSWORD_MUST_CHANGE
    [InlineData(1, 0u, ApiErrorCode.Generic)]                                                // OperationsError
    public void Map_ReturnsExpectedCode(int resultCode, uint extendedError, ApiErrorCode expected)
    {
        var actual = LdapErrorMapping.Map((ResultCode)resultCode, extendedError);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Map_Unknown_ReturnsGeneric()
    {
        var actual = LdapErrorMapping.Map((ResultCode)999, 0u);
        Assert.Equal(ApiErrorCode.Generic, actual);
    }

    [Theory]
    [InlineData(null, 0u)]
    [InlineData("", 0u)]
    [InlineData("no data token here", 0u)]
    [InlineData("0000052D: SvcErr: DSID-031A122D, problem 5003 (WILL_NOT_PERFORM), data 52d, 0 pass through", 0x52Du)]
    [InlineData("0000052D: SvcErr: DSID-031A122D, problem 5003 (WILL_NOT_PERFORM), DATA 775, 0 pass through", 0x775u)]
    [InlineData("problem 5003 data , rest", 0u)]
    [InlineData("problem 5003 data FFFFFFFF, rest", 0xFFFFFFFFu)]
    public void ExtractExtendedError_ReturnsExpectedValue(string? errorMessage, uint expected)
    {
        var actual = LdapErrorMapping.ExtractExtendedError(errorMessage);
        Assert.Equal(expected, actual);
    }
}
