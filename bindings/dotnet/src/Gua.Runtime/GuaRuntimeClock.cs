using System.Runtime.InteropServices;
using Gua.Core;

namespace Gua.Runtime;

public sealed class GuaRuntimeClock
{
    private const int CallbackLimit = 1_000_000;
    private readonly GuaRuntime runtime;
    private readonly List<Scheduled> scheduled = [];
    private long sequence;
    private bool draining;
    private ulong? reportedAutomaticExecutionLimitGeneration;
    internal GuaRuntimeClock(GuaRuntime runtime) => this.runtime = runtime;
    public event Action<GuaClockDelta>? Tick;
    public event Action<Exception>? CallbackFailed;
    public GuaClockStatus Status { get { var value = new Native.ClockStatus { StructSize = (uint)Marshal.SizeOf<Native.ClockStatus>() };
        if (Native.gua_runtime_clock_get_status(runtime.Handle, ref value) == 0) throw new InvalidOperationException("Failed to inspect Gua clock.");
        return new(value.Installed != 0, value.Paused != 0, value.NowMs, value.DefaultStepMs, value.PendingMs, value.Generation); } }
    public void Install(TimeSpan? initialTime = null, TimeSpan? step = null) => Check(Native.gua_runtime_clock_install(runtime.Handle,
        initialTime?.TotalMilliseconds ?? 0.0, step?.TotalMilliseconds ?? 1000.0 / 60.0));
    public void Pause() => Check(Native.gua_runtime_clock_pause(runtime.Handle));
    public void Resume() => Check(Native.gua_runtime_clock_resume(runtime.Handle));
    public void RunFor(TimeSpan duration, TimeSpan? step = null)
    { Check(Native.gua_runtime_clock_run_for(runtime.Handle, duration.TotalMilliseconds,
        step?.TotalMilliseconds ?? Status.DefaultStepMilliseconds)); Drain(); }
    public void Advance(TimeSpan unscaledDelta)
    { var status = Status; PurgeInactive(status); if (!status.Installed) { reportedAutomaticExecutionLimitGeneration = null; return; } if (status.PendingMilliseconds > 0) { Drain(); return; }
      if (status.Paused || unscaledDelta <= TimeSpan.Zero) { reportedAutomaticExecutionLimitGeneration = null; return; }
      var result = Native.gua_runtime_clock_advance(runtime.Handle, unscaledDelta.TotalMilliseconds);
      if (result != (int)GuaClockResult.Ok)
      {
          var latest = Status;
          if ((GuaClockResult)result == GuaClockResult.InvalidState && latest.Installed && latest.Paused) return;
          if ((GuaClockResult)result == GuaClockResult.ExecutionLimit)
          {
              if (reportedAutomaticExecutionLimitGeneration != latest.Generation)
              {
                  reportedAutomaticExecutionLimitGeneration = latest.Generation;
                  ReportCallbackFailure(new InvalidOperationException("Gua clock automatic advance exceeded execution_limit; the host frame will continue without advancing the clock."));
              }
              return;
          }
          Check(result);
      }
      reportedAutomaticExecutionLimitGeneration = null;
      Drain(); }
    public IDisposable Schedule(TimeSpan delay, Action callback, TimeSpan? interval = null)
    {
        if (callback is null) throw new ArgumentNullException(nameof(callback));
        if (delay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delay));
        if (interval is { } repeat && repeat <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
        var status = Status;
        var bindOnInstall = !status.Installed;
        var delayMs = delay.TotalMilliseconds;
        var intervalMs = interval?.TotalMilliseconds;
        var due = bindOnInstall ? delayMs : status.NowMilliseconds + delayMs;
        if (!bindOnInstall) ValidateRepresentableSchedule(status.NowMilliseconds, delayMs, due, intervalMs);
        var item = new Scheduled(++sequence, status.Generation, due,
            intervalMs, callback, bindOnInstall); scheduled.Add(item); return new Cancel(this, item);
    }
    public void Drain()
    {
        if (draining) return;
        draining = true;
        try
        {
        BindPendingSchedules();
        var callbackCount = 0;
        var step = new Native.ClockStep { StructSize = (uint)Marshal.SizeOf<Native.ClockStep>() };
        while (Native.gua_runtime_clock_consume_step(runtime.Handle, ref step) != 0)
        {
            scheduled.RemoveAll(x => x.Generation != step.Generation);
            var now = Status.NowMilliseconds;
            while (scheduled.Where(x => !x.Cancelled && x.Due <= now).OrderBy(x => x.Due).ThenBy(x => x.Sequence).FirstOrDefault() is { } item)
            { if (++callbackCount > CallbackLimit)
              { scheduled.ForEach(candidate => candidate.Cancelled = true); scheduled.Clear();
                throw new InvalidOperationException("Gua clock execution_limit."); }
              if (item.Cancelled) continue; scheduled.Remove(item);
              try { item.Callback?.Invoke(); }
              catch (Exception error) { ReportCallbackFailure(error); }
              var generation = Status.Generation;
              if (generation != step.Generation)
              { item.Cancelled = true; scheduled.RemoveAll(candidate => candidate.Generation != generation); break; }
              if (item.Interval is { } interval && !item.Cancelled)
              {
                  var nextDue = item.Due + interval;
                  if (double.IsFinite(nextDue) && nextDue > item.Due) { item.Due = nextDue; scheduled.Add(item); }
                  else
                  {
                      item.Cancelled = true;
                      ReportCallbackFailure(new InvalidOperationException("Gua clock interval cannot advance its representable deadline."));
                  }
              }
              else item.Cancelled = true; }
            scheduled.RemoveAll(x => x.Cancelled);
            if (Status.Generation != step.Generation)
            {
                step = new Native.ClockStep { StructSize = (uint)Marshal.SizeOf<Native.ClockStep>() };
                continue;
            }
            if (Tick is not null)
                foreach (Action<GuaClockDelta> handler in Tick.GetInvocationList())
                {
                    try { handler(new GuaClockDelta(step.DeltaMs)); }
                    catch (Exception error) { ReportCallbackFailure(error); }
                    if (Status.Generation != step.Generation) break;
                }
            step = new Native.ClockStep { StructSize = (uint)Marshal.SizeOf<Native.ClockStep>() };
        }
        }
        finally { draining = false; }
    }
    private void BindPendingSchedules()
    {
        var status = Status;
        if (!status.Installed) return;
        scheduled.RemoveAll(item => item.BindOnInstall && status.Generation != item.Generation + 1);
        foreach (var item in scheduled.Where(item => item.BindOnInstall).ToArray())
        {
            var due = status.NowMilliseconds + item.Due;
            try { ValidateRepresentableSchedule(status.NowMilliseconds, item.Due, due, item.Interval); }
            catch (ArgumentOutOfRangeException error)
            {
                CancelSchedule(item);
                ReportCallbackFailure(error);
                continue;
            }
            item.Generation = status.Generation;
            item.Due = due;
            item.BindOnInstall = false;
        }
    }
    private void PurgeInactive(GuaClockStatus status)
    {
        scheduled.RemoveAll(item => item.Cancelled || !status.Installed && item.Generation != status.Generation);
    }
    private void CancelSchedule(Scheduled item)
    {
        item.Cancelled = true;
        item.Callback = null;
        scheduled.Remove(item);
    }
    private void ReportCallbackFailure(Exception error)
    {
        if (CallbackFailed is null) return;
        foreach (Action<Exception> handler in CallbackFailed.GetInvocationList())
        {
            try { handler(error); }
            catch { }
        }
    }
    private static void ValidateRepresentableSchedule(double now, double delay, double due, double? interval)
    {
        if (!double.IsFinite(due) || delay > 0 && due <= now)
            throw new ArgumentOutOfRangeException(nameof(delay), "The delay cannot advance the current Gua clock timeline.");
        if (interval is { } repeat)
        {
            var nextDue = due + repeat;
            if (!double.IsFinite(nextDue) || nextDue <= due)
                throw new ArgumentOutOfRangeException(nameof(interval), "The interval cannot advance its representable Gua clock deadline.");
        }
    }
    private static void Check(int result) { if (result != 1) throw new InvalidOperationException($"Gua clock operation failed: {(GuaClockResult)result}."); }
    private sealed class Scheduled(long sequence, ulong generation, double due, double? interval, Action callback, bool bindOnInstall)
    { public long Sequence { get; } = sequence; public ulong Generation { get; set; } = generation; public double Due { get; set; } = due; public double? Interval { get; } = interval; public Action? Callback { get; set; } = callback; public bool BindOnInstall { get; set; } = bindOnInstall; public bool Cancelled { get; set; } }
    private sealed class Cancel(GuaRuntimeClock owner, Scheduled item) : IDisposable
    { private GuaRuntimeClock? Owner { get; set; } = owner; public void Dispose() { Owner?.CancelSchedule(item); Owner = null; } }
}
