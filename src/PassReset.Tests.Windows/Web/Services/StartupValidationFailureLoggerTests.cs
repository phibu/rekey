using System.Reflection;
using Microsoft.Extensions.Options;

namespace PassReset.Tests.Windows.Web.Services;

/// <summary>
/// Nyquist gap-fill (STAB-011): <see cref="OptionsValidationException"/> logger contract.
/// The logger is best-effort — it must NEVER throw even when the Event Log source is
/// unregistered (the common case on dev boxes and CI runners). Any exception leaking
/// out would mask the original <see cref="OptionsValidationException"/> and leave the
/// operator without a 502 root-cause.
///
/// The production type <c>PassReset.Web.Services.StartupValidationFailureLogger</c> is
/// internal; this test invokes it via reflection to avoid an InternalsVisibleTo
/// production-code change for a test-only concern.
/// </summary>
public class StartupValidationFailureLoggerTests
{
    private static MethodInfo LogToEventLog()
    {
        var asm = typeof(PassReset.Web.Models.ClientSettingsValidator).Assembly;
        var type = asm.GetType("PassReset.Web.Services.StartupValidationFailureLogger", throwOnError: true)!;
        var method = type.GetMethod(
            "LogToEventLog",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return method!;
    }

    [Fact]
    public void LogToEventLog_WithTypicalFailures_DoesNotThrow()
    {
        var ex = new OptionsValidationException(
            optionsName: "PasswordChangeOptions",
            optionsType: typeof(object),
            failureMessages:
            [
                "PasswordChangeOptions.LdapHostnames must contain at least one non-empty hostname.",
                "PasswordChangeOptions.LdapPort '99999' is not a valid port number.",
            ]);

        var method = LogToEventLog();

        // On a dev box / CI runner the Event Log source 'PassReset' is not registered;
        // SourceExists returns false and the inner write is skipped. Must still return
        // cleanly — a leaking exception here would mask the original OptionsValidationException.
        var record = Record.Exception(() => method.Invoke(null, [ex]));
        Assert.Null(record);
    }

    [Fact]
    public void LogToEventLog_WithEmptyFailures_DoesNotThrow()
    {
        var ex = new OptionsValidationException(
            optionsName: "WebSettings",
            optionsType: typeof(object),
            failureMessages: Array.Empty<string>());

        var method = LogToEventLog();
        var record = Record.Exception(() => method.Invoke(null, [ex]));
        Assert.Null(record);
    }
}
