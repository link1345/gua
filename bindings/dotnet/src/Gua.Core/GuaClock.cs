namespace Gua.Core;

public enum GuaClockResult { Ok = 1, InvalidArgument = -1, NotInstalled = -2, InvalidState = -3, ExecutionLimit = -4 }

public sealed record GuaClockStatus(bool Installed, bool Paused, double NowMilliseconds,
    double DefaultStepMilliseconds, double PendingMilliseconds, ulong Generation)
{
    public TimeSpan Now => TimeSpan.FromMilliseconds(NowMilliseconds);
}

public readonly record struct GuaClockStep(TimeSpan Delta, bool FinalStep, ulong Generation);

public interface IGuaClockContext
{
    GuaClockResult InstallClock(TimeSpan? initialTime = null, TimeSpan? step = null);
    GuaClockResult PauseClock();
    GuaClockResult RunClockFor(TimeSpan duration, TimeSpan? step = null);
    GuaClockResult ResumeClock();
    GuaClockStatus GetClockStatus();
}

public sealed class GuaClock
{
    private const int CallbackLimit = 1_000_000;
    private readonly GuaContext context;
    private readonly List<Scheduled> scheduled = [];
    private long sequence;
    public GuaClock(GuaContext context) => this.context = context ?? throw new ArgumentNullException(nameof(context));
    public event Action<TimeSpan>? Tick;
    public GuaClockStatus Status => context.GetClockStatus();

    public void Install(TimeSpan? initialTime = null, TimeSpan? step = null) => Ensure(context.InstallClock(initialTime, step));
    public void Pause() => Ensure(context.PauseClock());
    public void Resume() => Ensure(context.ResumeClock());
    public void RunFor(TimeSpan duration, TimeSpan? step = null)
    {
        Ensure(context.RunClockFor(duration, step));
        DrainPendingSteps();
    }

    public IDisposable Schedule(TimeSpan delay, Action callback, TimeSpan? interval = null)
    {
        if (callback is null) throw new ArgumentNullException(nameof(callback));
        if (delay < TimeSpan.Zero || interval is { } repeat && repeat <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delay));
        var item = new Scheduled(++sequence, Status.NowMilliseconds + delay.TotalMilliseconds,
            interval?.TotalMilliseconds, callback);
        scheduled.Add(item);
        return new Cancellation(item);
    }

    public void DrainPendingSteps()
    {
        var callbacks = 0;
        while (context.TryConsumeClockStep(out var step))
        {
            var now = context.GetClockStatus().NowMilliseconds;
            while (scheduled.Where(item => !item.Cancelled && item.DueMs <= now)
                       .OrderBy(item => item.DueMs).ThenBy(item => item.Sequence).FirstOrDefault() is { } item)
            {
                if (++callbacks > CallbackLimit) throw new InvalidOperationException("Gua clock execution_limit.");
                if (item.Cancelled) continue;
                item.Callback();
                if (item.IntervalMs is { } interval) item.DueMs += interval;
                else item.Cancelled = true;
            }
            scheduled.RemoveAll(item => item.Cancelled);
            Tick?.Invoke(step.Delta);
        }
    }

    private static void Ensure(GuaClockResult result)
    {
        if (result != GuaClockResult.Ok) throw new InvalidOperationException($"Gua clock operation failed: {result}.");
    }
    private sealed class Scheduled(long sequence, double dueMs, double? intervalMs, Action callback)
    { public long Sequence { get; } = sequence; public double DueMs { get; set; } = dueMs; public double? IntervalMs { get; } = intervalMs; public Action Callback { get; } = callback; public bool Cancelled { get; set; } }
    private sealed class Cancellation(Scheduled item) : IDisposable { public void Dispose() => item.Cancelled = true; }
}
