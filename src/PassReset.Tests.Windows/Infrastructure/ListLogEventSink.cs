using Serilog.Core;
using Serilog.Events;

namespace PassReset.Tests.Windows.Infrastructure;

/// <summary>
/// Handwritten in-memory <see cref="ILogEventSink"/> used by tests to capture
/// every emitted <see cref="LogEvent"/> for assertion. Avoids pulling in a
/// third-party Serilog test-sink package (CONTEXT.md D-03 — no new Serilog
/// packages unless strictly necessary).
/// </summary>
internal sealed class ListLogEventSink : ILogEventSink
{
    public List<LogEvent> Events { get; } = new();

    public void Emit(LogEvent logEvent) => Events.Add(logEvent);

    /// <summary>Rendered text of every captured event (message template bound to properties).</summary>
    public IEnumerable<string> AllRendered(IFormatProvider? fp = null) =>
        Events.Select(e => e.RenderMessage(fp));

    /// <summary>
    /// Flattens every property value across every captured event to a stream of
    /// primitives for sentinel-absence assertions. Walks
    /// <see cref="SequenceValue"/>, <see cref="StructureValue"/>,
    /// <see cref="DictionaryValue"/> recursively so strings nested inside
    /// structured properties (e.g. the <c>ExceptionChain</c> array) are still
    /// inspected.
    /// </summary>
    public IEnumerable<string> AllPropertyValues()
    {
        foreach (var evt in Events)
        {
            foreach (var kvp in evt.Properties)
            {
                foreach (var s in Flatten(kvp.Value))
                    yield return s;
            }

            // Attached exceptions also get walked — their ToString() contains message + stack.
            if (evt.Exception is not null)
                yield return evt.Exception.ToString();
        }
    }

    private static IEnumerable<string> Flatten(LogEventPropertyValue value)
    {
        switch (value)
        {
            case ScalarValue sv:
                yield return sv.Value?.ToString() ?? string.Empty;
                break;
            case SequenceValue seq:
                foreach (var el in seq.Elements)
                    foreach (var s in Flatten(el)) yield return s;
                break;
            case StructureValue str:
                foreach (var prop in str.Properties)
                    foreach (var s in Flatten(prop.Value)) yield return s;
                break;
            case DictionaryValue dict:
                foreach (var entry in dict.Elements)
                {
                    foreach (var s in Flatten(entry.Key)) yield return s;
                    foreach (var s in Flatten(entry.Value)) yield return s;
                }
                break;
            default:
                yield return value.ToString();
                break;
        }
    }
}
