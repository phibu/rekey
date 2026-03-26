using Microsoft.Extensions.Options;

namespace PassReset.PasswordProvider;

/// <summary>
/// Validates <see cref="PasswordChangeOptions"/> at application startup so that
/// mis-configuration is caught immediately with a clear error rather than producing
/// a cryptic LDAP socket error at runtime.
/// </summary>
internal sealed class PasswordChangeOptionsValidator : IValidateOptions<PasswordChangeOptions>
{
    public ValidateOptionsResult Validate(string? name, PasswordChangeOptions options)
    {
        if (options.UseAutomaticContext)
            return ValidateOptionsResult.Success;

        // Manual context requires at least one resolvable hostname.
        if (options.LdapHostnames.Length == 0
            || options.LdapHostnames.All(h => string.IsNullOrWhiteSpace(h)))
        {
            return ValidateOptionsResult.Fail(
                "PasswordChangeOptions.LdapHostnames must contain at least one non-empty hostname " +
                "when UseAutomaticContext is false.");
        }

        if (options.LdapPort <= 0 || options.LdapPort > 65535)
        {
            return ValidateOptionsResult.Fail(
                $"PasswordChangeOptions.LdapPort '{options.LdapPort}' is not a valid port number. " +
                "Use 636 for LDAPS (recommended) or 389 for plain LDAP.");
        }

        return ValidateOptionsResult.Success;
    }
}
