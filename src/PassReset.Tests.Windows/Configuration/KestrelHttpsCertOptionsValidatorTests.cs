using Microsoft.Extensions.Options;
using PassReset.Web.Configuration;
using PassReset.Web.Models;
using PassReset.Web.Services.Hosting;

namespace PassReset.Tests.Windows.Configuration;

public sealed class KestrelHttpsCertOptionsValidatorTests
{
    private static ValidateOptionsResult Run(KestrelHttpsCertOptions opts, HostingMode mode) =>
        new KestrelHttpsCertOptionsValidator(() => mode).Validate(null, opts);

    [Fact]
    public void IisMode_BothNull_Passes()
    {
        // IIS terminates TLS; our cert options are ignored.
        var r = Run(new KestrelHttpsCertOptions(), HostingMode.Iis);
        Assert.True(r.Succeeded);
    }

    [Fact]
    public void ConsoleMode_BothNull_Passes()
    {
        // Console mode is dev / no-TLS; empty is acceptable.
        var r = Run(new KestrelHttpsCertOptions(), HostingMode.Console);
        Assert.True(r.Succeeded);
    }

    [Fact]
    public void ServiceMode_BothNull_Fails()
    {
        var r = Run(new KestrelHttpsCertOptions(), HostingMode.Service);
        Assert.False(r.Succeeded);
        Assert.Contains(r.Failures!, m => m.Contains("Thumbprint", StringComparison.OrdinalIgnoreCase)
                                       || m.Contains("PfxPath", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ServiceMode_ThumbprintOnly_Passes()
    {
        var r = Run(new KestrelHttpsCertOptions { Thumbprint = "ABCDEF" }, HostingMode.Service);
        Assert.True(r.Succeeded, string.Join("; ", r.Failures ?? []));
    }

    [Fact]
    public void ServiceMode_PfxOnly_Passes()
    {
        var r = Run(new KestrelHttpsCertOptions
        {
            PfxPath = Path.Combine(Path.GetTempPath(), "cert.pfx"),
            PfxPassword = "pw",
        }, HostingMode.Service);
        Assert.True(r.Succeeded, string.Join("; ", r.Failures ?? []));
    }

    [Fact]
    public void ServiceMode_BothSet_Fails()
    {
        var r = Run(new KestrelHttpsCertOptions
        {
            Thumbprint = "ABCDEF",
            PfxPath = Path.Combine(Path.GetTempPath(), "cert.pfx"),
        }, HostingMode.Service);
        Assert.False(r.Succeeded);
        Assert.Contains(r.Failures!, m => m.Contains("mutually exclusive", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ServiceMode_InvalidStoreLocation_Fails()
    {
        var r = Run(new KestrelHttpsCertOptions
        {
            Thumbprint = "ABCDEF",
            StoreLocation = "NotAStoreLocation",
        }, HostingMode.Service);
        Assert.False(r.Succeeded);
        Assert.Contains(r.Failures!, m => m.Contains("StoreLocation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ServiceMode_CurrentUserStore_Fails()
    {
        // Service identity makes CurrentUser store non-portable; reject explicitly.
        var r = Run(new KestrelHttpsCertOptions
        {
            Thumbprint = "ABCDEF",
            StoreLocation = "CurrentUser",
        }, HostingMode.Service);
        Assert.False(r.Succeeded);
        Assert.Contains(r.Failures!, m => m.Contains("CurrentUser", StringComparison.OrdinalIgnoreCase));
    }
}
