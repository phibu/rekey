using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using PassReset.Web.Middleware;
using Xunit;

namespace PassReset.Tests.Windows.Admin;

public sealed class LoopbackOnlyGuardTests
{
    [Fact]
    public async Task Invoke_LoopbackRemote_CallsNext()
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        var nextCalled = false;
        var sut = new LoopbackOnlyGuardMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, NullLogger<LoopbackOnlyGuardMiddleware>.Instance);

        await sut.InvokeAsync(ctx);

        Assert.True(nextCalled);
        Assert.NotEqual(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Invoke_NonLoopbackRemote_Returns404()
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.5");
        var nextCalled = false;
        var sut = new LoopbackOnlyGuardMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, NullLogger<LoopbackOnlyGuardMiddleware>.Instance);

        await sut.InvokeAsync(ctx);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Invoke_IPv6Loopback_CallsNext()
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.IPv6Loopback;
        var nextCalled = false;
        var sut = new LoopbackOnlyGuardMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, NullLogger<LoopbackOnlyGuardMiddleware>.Instance);

        await sut.InvokeAsync(ctx);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Invoke_NullRemote_Returns404()
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = null;
        var nextCalled = false;
        var sut = new LoopbackOnlyGuardMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, NullLogger<LoopbackOnlyGuardMiddleware>.Instance);

        await sut.InvokeAsync(ctx);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
    }
}
