using System.DirectoryServices.AccountManagement;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PassReset.PasswordProvider;
using PassReset.Tests.Windows.Infrastructure;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace PassReset.Tests.Windows.PasswordProvider;

public sealed class ExceptionChainLoggerTests
{
    private static (ListLogEventSink sink, ILogger<PasswordChangeProvider> logger) BuildLogger()
    {
        var sink = new ListLogEventSink();
        var seriLogger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.FromLogContext()
            .WriteTo.Sink(sink)
            .CreateLogger();
        var factory = new SerilogLoggerFactory(seriLogger, dispose: true);
        return (sink, factory.CreateLogger<PasswordChangeProvider>());
    }

    [Fact]
    public void LogExceptionChain_SingleException_EmitsDepthZeroEntry()
    {
        var (sink, logger) = BuildLogger();
        var ex = new PasswordException("bad password") { HResult = unchecked((int)0x8007052D) };

        ExceptionChainLogger.LogExceptionChain(logger, ex, "complexity failure for {User}", "alice");

        Assert.Single(sink.Events);
        var evt = sink.Events[0];
        Assert.True(evt.Properties.ContainsKey("ExceptionChain"));

        var chain = Assert.IsType<SequenceValue>(evt.Properties["ExceptionChain"]);
        Assert.Single(chain.Elements);

        var entry = Assert.IsType<StructureValue>(chain.Elements[0]);
        var props = entry.Properties.ToDictionary(p => p.Name, p => (p.Value as ScalarValue)?.Value);
        Assert.Equal(0, Assert.IsType<int>(props["depth"]));
        Assert.Equal("PasswordException", props["type"]);
        Assert.Equal("0x8007052D", props["hresult"]);
        Assert.Equal("bad password", props["message"]);
    }

    [Fact]
    public void LogExceptionChain_NestedChain_EmitsAllDepthsInOrder()
    {
        var (sink, logger) = BuildLogger();

        var inner = new COMException("access denied") { HResult = unchecked((int)0x80070005) };
        var outer = new InvalidOperationException("outer wrapper", inner);

        ExceptionChainLogger.LogExceptionChain(logger, outer, "nested failure");

        var evt = Assert.Single(sink.Events);
        var chain = Assert.IsType<SequenceValue>(evt.Properties["ExceptionChain"]);
        Assert.Equal(2, chain.Elements.Count);

        var frame0 = Assert.IsType<StructureValue>(chain.Elements[0]);
        var props0 = frame0.Properties.ToDictionary(p => p.Name, p => (p.Value as ScalarValue)?.Value);
        Assert.Equal(0, props0["depth"]);
        Assert.Equal("InvalidOperationException", props0["type"]);

        var frame1 = Assert.IsType<StructureValue>(chain.Elements[1]);
        var props1 = frame1.Properties.ToDictionary(p => p.Name, p => (p.Value as ScalarValue)?.Value);
        Assert.Equal(1, props1["depth"]);
        Assert.Equal("COMException", props1["type"]);
        Assert.Equal("0x80070005", props1["hresult"]);
        Assert.Equal("access denied", props1["message"]);
    }

    [Fact]
    public void LogExceptionChain_PreservesTopLevelExceptionForStackTrace()
    {
        var (sink, logger) = BuildLogger();
        var ex = new PasswordException("boom");

        ExceptionChainLogger.LogExceptionChain(logger, ex, "failure");

        var evt = Assert.Single(sink.Events);
        Assert.Same(ex, evt.Exception);
        Assert.Equal(LogEventLevel.Warning, evt.Level);
    }
}
