using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PassReset.Web.Models;
using PassReset.Web.Services;

namespace PassReset.Tests.Windows.Services;

/// <summary>
/// Unit tests for the <see cref="IExpiryServiceDiagnostics"/> contract exposed by
/// <see cref="PasswordExpiryNotificationService"/>. Verifies enabled flag plumbing
/// and the atomic (Interlocked) tick storage contract documented in PATTERNS.md.
/// </summary>
public sealed class ExpiryServiceDiagnosticsTests
{
    private static PasswordExpiryNotificationService NewService(bool enabled)
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
            .BuildServiceProvider();

        var settings = new PasswordExpiryNotificationSettings { Enabled = enabled };
        var options  = Options.Create(settings);
        var logger   = NullLogger<PasswordExpiryNotificationService>.Instance;

        return new PasswordExpiryNotificationService(services, options, logger);
    }

    [Fact]
    public void IsEnabled_ReflectsSettingsEnabledFlag_WhenTrue()
    {
        var svc = NewService(enabled: true);
        IExpiryServiceDiagnostics diag = svc;
        Assert.True(diag.IsEnabled);
    }

    [Fact]
    public void IsEnabled_ReflectsSettingsEnabledFlag_WhenFalse()
    {
        var svc = NewService(enabled: false);
        IExpiryServiceDiagnostics diag = svc;
        Assert.False(diag.IsEnabled);
    }

    [Fact]
    public void LastTickUtc_IsNull_BeforeAnyTick()
    {
        var svc = NewService(enabled: true);
        IExpiryServiceDiagnostics diag = svc;
        Assert.Null(diag.LastTickUtc);
    }

    [Fact]
    public void LastTickUtc_IsNonNull_AfterSetLastTickForTests()
    {
        var svc    = NewService(enabled: true);
        var expect = DateTimeOffset.UtcNow;
        svc.SetLastTickForTests(expect);

        IExpiryServiceDiagnostics diag = svc;
        Assert.NotNull(diag.LastTickUtc);

        // Encoded via ticks, so should round-trip to the same tick count (UTC).
        Assert.Equal(expect.UtcTicks, diag.LastTickUtc!.Value.UtcTicks);
        Assert.Equal(TimeSpan.Zero,   diag.LastTickUtc!.Value.Offset);
    }

    [Fact]
    public void LastTickUtc_ConcurrentReadsDuringWrites_AreTornFreeAndThrowNothing()
    {
        // 100 readers + 1 writer flipping between two distant tick values — a torn
        // read (upper 32 bits of one value with lower 32 of the other) would produce
        // a DateTimeOffset outside [a, b]. Interlocked.Read guarantees this never
        // happens on any platform. Test runs ≤ 300ms in practice.
        var svc = NewService(enabled: true);

        var tsA = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var tsB = new DateTimeOffset(2030, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var tickA = tsA.UtcTicks;
        var tickB = tsB.UtcTicks;

        svc.SetLastTickForTests(tsA);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var writer = Task.Run(() =>
        {
            var flip = false;
            while (!cts.IsCancellationRequested)
            {
                svc.SetLastTickForTests(flip ? tsA : tsB);
                flip = !flip;
            }
        });

        Exception? captured = null;
        IExpiryServiceDiagnostics diag = svc;
        Parallel.For(0, 100, _ =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    var read = diag.LastTickUtc;
                    Assert.NotNull(read);
                    var ticks = read!.Value.UtcTicks;
                    Assert.True(ticks == tickA || ticks == tickB,
                        $"Torn read detected: got {ticks}, expected {tickA} or {tickB}");
                }
            }
            catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
            {
                Interlocked.CompareExchange(ref captured, ex, null);
            }
        });

        writer.Wait();
        Assert.Null(captured);
    }
}
