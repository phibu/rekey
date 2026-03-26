using Microsoft.AspNetCore.Mvc.ModelBinding;
using ReKey.Common;

namespace ReKey.Web.Models;

/// <summary>
/// Represents a generic response from a REST API call.
/// </summary>
public class ApiResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ApiResult"/> class.
    /// </summary>
    /// <param name="payload">The payload.</param>
    public ApiResult(object? payload = null)
    {
        Errors = [];
        Payload = payload;
    }

    /// <summary>Gets the errors list.</summary>
    public List<ApiErrorItem> Errors { get; }

    /// <summary>Gets the payload.</summary>
    public object? Payload { get; }

    /// <summary>Creates a generic invalid request response.</summary>
    public static ApiResult InvalidRequest()
    {
        var result = new ApiResult("Invalid Request");
        result.Errors.Add(new ApiErrorItem(ApiErrorCode.Generic, "Invalid Request"));
        return result;
    }

    /// <summary>Creates an invalid captcha response.</summary>
    public static ApiResult InvalidCaptcha()
    {
        var result = new ApiResult("Invalid Recaptcha");
        result.Errors.Add(new ApiErrorItem(ApiErrorCode.InvalidCaptcha));
        return result;
    }

    /// <summary>Creates an ApiResult populated from ModelState errors.</summary>
    public static ApiResult FromModelStateErrors(ModelStateDictionary modelState)
    {
        var result = new ApiResult();

        foreach (var (key, value) in modelState.Where(x => x.Value!.Errors.Any()))
        {
            var error = value!.Errors.First();

            switch (error.ErrorMessage)
            {
                case nameof(ApiErrorCode.FieldRequired):
                    result.AddFieldRequiredValidationError(key);
                    break;
                case nameof(ApiErrorCode.FieldMismatch):
                    result.AddFieldMismatchValidationError(key);
                    break;
                default:
                    result.AddGenericFieldValidationError(key, error.ErrorMessage);
                    break;
            }
        }

        return result;
    }

    private void AddFieldRequiredValidationError(string fieldName) =>
        Errors.Add(new ApiErrorItem(ApiErrorCode.FieldRequired, nameof(ApiErrorCode.FieldRequired)) { FieldName = fieldName });

    private void AddFieldMismatchValidationError(string fieldName) =>
        Errors.Add(new ApiErrorItem(ApiErrorCode.FieldMismatch, nameof(ApiErrorCode.FieldMismatch)) { FieldName = fieldName });

    private void AddGenericFieldValidationError(string fieldName, string message) =>
        Errors.Add(new ApiErrorItem(ApiErrorCode.Generic, message) { FieldName = fieldName });
}
