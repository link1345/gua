using System.Runtime.InteropServices;
using Gua.Core;

namespace Gua.Runtime;

public sealed class GuaRuntimeClock
{
    private readonly GuaRuntime runtime;
    private readonly List<Scheduled> scheduled = [];
    private long sequence;
    internal GuaRuntimeClock(GuaRuntime runtime) => this.runtime = runtime;
    public event Action<TimeSpan>? Tick;
    public GuaClockStatus Status { get { var value = new Native.ClockStatus { StructSize = (uint)Marshal.SizeOf<Native.ClockStatus>() };
        if (Native.gua_runtime_clock_get_status(runtime.Handle, ref value) == 0) throw new InvalidOperationException("Failed to inspect Gua clock.");
        return new(value.Installed != 0, value.Paused != 0, value.NowMs, value.DefaultStepMs, value.PendingMs, value.Generation); } }
    public void Install(TimeSpan? initialTime = null, TimeSpan? step = null) => Check(Native.gua_runtime_clock_install(runtime.Handle,
        (initialTime ?? TimeSpan.Zero).TotalMilliseconds, (step ?? TimeSpan.FromSeconds(1.0 / 60.0)).TotalMilliseconds));
    public void Pause() => Check(Native.gua_runtime_clock_pause(runtime.Handle));
    public void Resume() => Check(Native.gua_runtime_clock_resume(runtime.Handle));
    public void RunFor(TimeSpan duration, TimeSpan? step = null)
    { Check(Native.gua_runtime_clock_run_for(runtime.Handle, duration.TotalMilliseconds, (step ?? TimeSpan.FromMilliseconds(Status.DefaultStepMilliseconds)).TotalMilliseconds)); Drain(); }
    public void Advance(TimeSpan unscaledDelta)
    { var status = Status; if (!status.Installed) return; if (status.PendingMilliseconds > 0) { Drain(); return; }
      if (status.Paused || unscaledDelta <= TimeSpan.Zero) return; Check(Native.gua_runtime_clock_advance(runtime.Handle, unscaledDelta.TotalMilliseconds)); Drain(); }
    public IDisposable Schedule(TimeSpan delay, Action callback, TimeSpan? interval = null)
    {
        if (callback is null) throw new ArgumentNullException(nameof(callback));
        if (delay < TimeSpan.Zero || interval is { } repeat && repeat <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delay));
        var status = Status;
        var bindOnInstall = !status.Installed;
        var item = new Scheduled(++sequence, status.Generation,
            bindOnInstall ? delay.TotalMilliseconds : status.NowMilliseconds + delay.TotalMilliseconds,
            interval?.TotalMilliseconds, callback, bindOnInstall); scheduled.Add(item); return new Cancel(item);
    }
    public void Drain()
    {
        BindPendingSchedules();
        var callbackCount = 0;
        var step = new Native.ClockStep { StructSize = (uint)Marshal.SizeOf<Native.ClockStep>() };
        while (Native.gua_runtime_clock_consume_step(runtime.Handle, ref step) != 0)
        {
            scheduled.RemoveAll(x => x.Generation != step.Generation);
            var now = Status.NowMilliseconds;
            while (scheduled.Where(x => !x.Cancelled && x.Due <= now).OrderBy(x => x.Due).ThenBy(x => x.Sequence).FirstOrDefault() is { } item)
            { if (++callbackCount > 1_000_000) throw new InvalidOperationException("Gua clock execution_limit.");
              if (item.Cancelled) continue; scheduled.Remove(item); item.Callback();
              if (item.Interval is { } interval && !item.Cancelled) { item.Due += interval; scheduled.Add(item); } else item.Cancelled = true; }
            scheduled.RemoveAll(x => x.Cancelled); Tick?.Invoke(TimeSpan.FromMilliseconds(step.DeltaMs));
            step = new Native.ClockStep { StructSize = (uint)Marshal.SizeOf<Native.ClockStep>() };
        }
    }
    private void BindPendingSchedules()
    {
        var status = Status;
        if (!status.Installed) return;
        scheduled.RemoveAll(item => item.BindOnInstall && status.Generation != item.Generation + 1);
        foreach (var item in scheduled.Where(item => item.BindOnInstall))
        {
            item.Generation = status.Generation;
            item.Due = status.NowMilliseconds + item.Due;
            item.BindOnInstall = false;
        }
    }
    private static void Check(int result) { if (result != 1) throw new InvalidOperationException($"Gua clock operation failed: {(GuaClockResult)result}."); }
    private sealed class Scheduled(long sequence, ulong generation, double due, double? interval, Action callback, bool bindOnInstall)
    { public long Sequence { get; } = sequence; public ulong Generation { get; set; } = generation; public double Due { get; set; } = due; public double? Interval { get; } = interval; public Action Callback { get; } = callback; public bool BindOnInstall { get; set; } = bindOnInstall; public bool Cancelled { get; set; } }
    private sealed class Cancel(Scheduled item) : IDisposable { public void Dispose() => item.Cancelled = true; }
}
