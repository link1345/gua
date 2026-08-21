using Gua.Core;
using NUnit.Framework;

namespace Gua.Visual.Tests;

public sealed class ClockTests
{
    [Test]
    public void RunForExecutesSchedulesAndTicksWithoutWallTime()
    {
        using var context = new GuaContext();
        var clock = new GuaClock(context);
        var fired = new List<string>();
        var ticks = new List<double>();
        clock.Install(step: TimeSpan.FromMilliseconds(10));
        clock.Schedule(TimeSpan.FromMilliseconds(20), () => fired.Add("first"));
        clock.Schedule(TimeSpan.FromMilliseconds(20), () => fired.Add("second"));
        clock.Tick += delta => ticks.Add(delta.TotalMilliseconds);
        clock.Pause();
        clock.RunFor(TimeSpan.FromMilliseconds(25));
        Assert.That(fired, Is.EqualTo(new[] { "first", "second" }));
        Assert.That(ticks, Is.EqualTo(new[] { 10, 10, 5 }));
        Assert.That(clock.Status.NowMilliseconds, Is.EqualTo(25));
        Assert.That(clock.Status.Paused, Is.True);
    }

    [Test]
    public void NativeEngineTimersAreNotClaimedByClock()
    {
        using var context = new GuaContext();
        var clock = new GuaClock(context);
        clock.Install(); clock.Pause();
        Assert.That(clock.Status.Paused, Is.True);
        Assert.That(clock.Status, Is.TypeOf<GuaClockStatus>());
    }
}
