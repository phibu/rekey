using System.ComponentModel.DataAnnotations;
using PassReset.Common;
using PassReset.Web.Models;

namespace PassReset.Tests.Web.Models;

public class ChangePasswordModelValidationTests
{
    private static List<ValidationResult> Validate(ChangePasswordModel m)
    {
        var ctx = new ValidationContext(m);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(m, ctx, results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void AllFieldsPresentAndMatching_IsValid()
    {
        var m = new ChangePasswordModel
        {
            Username = "alice",
            CurrentPassword = "old",
            NewPassword = "NewPass1!",
            NewPasswordVerify = "NewPass1!",
        };

        Assert.Empty(Validate(m));
    }

    [Fact]
    public void MissingUsername_FailsWithFieldRequired()
    {
        var m = new ChangePasswordModel
        {
            Username = string.Empty,
            CurrentPassword = "old",
            NewPassword = "NewPass1!",
            NewPasswordVerify = "NewPass1!",
        };

        var results = Validate(m);
        Assert.Contains(results, r =>
            r.MemberNames.Contains(nameof(ChangePasswordModel.Username))
            && r.ErrorMessage == nameof(ApiErrorCode.FieldRequired));
    }

    [Fact]
    public void MismatchedVerify_FailsWithFieldMismatch()
    {
        var m = new ChangePasswordModel
        {
            Username = "alice",
            CurrentPassword = "old",
            NewPassword = "NewPass1!",
            NewPasswordVerify = "Different!",
        };

        var results = Validate(m);
        Assert.Contains(results, r =>
            r.MemberNames.Contains(nameof(ChangePasswordModel.NewPasswordVerify))
            && r.ErrorMessage == nameof(ApiErrorCode.FieldMismatch));
    }

    [Fact]
    public void UsernameTooLong_FailsMaxLength()
    {
        var m = new ChangePasswordModel
        {
            Username = new string('a', 257),
            CurrentPassword = "old",
            NewPassword = "NewPass1!",
            NewPasswordVerify = "NewPass1!",
        };

        Assert.Contains(Validate(m), r =>
            r.MemberNames.Contains(nameof(ChangePasswordModel.Username)));
    }

    [Fact]
    public void AllRequiredFieldsMissing_ReportsAllThree()
    {
        var m = new ChangePasswordModel();
        var results = Validate(m);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(ChangePasswordModel.Username)));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(ChangePasswordModel.CurrentPassword)));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(ChangePasswordModel.NewPassword)));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(ChangePasswordModel.NewPasswordVerify)));
    }

    [Fact]
    public void RecaptchaIsOptional()
    {
        var m = new ChangePasswordModel
        {
            Username = "alice",
            CurrentPassword = "old",
            NewPassword = "NewPass1!",
            NewPasswordVerify = "NewPass1!",
            Recaptcha = string.Empty,
        };

        Assert.Empty(Validate(m));
    }
}
