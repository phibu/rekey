namespace ReKey.Common;

/// <inheritdoc />
/// <summary>
/// Special Exception to transport the ApiErrorItem.
/// </summary>
public class ApiErrorException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ApiErrorException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="errorCode">The error code.</param>
    public ApiErrorException(string message, ApiErrorCode errorCode = ApiErrorCode.Generic)
        : base(message) => ErrorCode = errorCode;

    /// <summary>Gets or sets the error code.</summary>
    public ApiErrorCode ErrorCode { get; }

    /// <inheritdoc />
    public override string Message => $"Error Code: {ErrorCode}\r\n{base.Message}";

    /// <summary>Converts to an API error item.</summary>
    public ApiErrorItem ToApiErrorItem() => new(ErrorCode, base.Message);
}
