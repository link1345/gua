namespace Gua.Core;

public enum GuaClockResult { Ok = 1, InvalidArgument = -1, NotInstalled = -2, InvalidState = -3, ExecutionLimit = -4 }

public sealed record GuaClockStatus(bool Installed, bool Paused, double NowMilliseconds,
    double DefaultStepMilliseconds, double PendingMilliseconds, ulong Generation)
{
    public TimeSpan Now => TimeSpan.FromMilliseconds(NowMilliseconds);
}

public readonly record struct GuaClockStep(TimeSpan Delta, bool FinalStep, ulong Generation);

public readonly record struct GuaClockDelta(double TotalMilliseconds)
{
    public double TotalSeconds => TotalMilliseconds / 1000.0;
    public TimeSpan TimeSpan => TimeSpan.FromMilliseconds(TotalMilliseconds);
    public static implicit operator TimeSpan(GuaClockDelta value) => value.TimeSpan;
}

public interface IGuaClockContext
{
    GuaClockResult InstallClock(TimeSpan? initialTime = null, TimeSpan? step = null);
    GuaClockResult PauseClock();
    GuaClockResult RunClockFor(TimeSpan duration, TimeSpan? step = null);
    GuaClockResult ResumeClock();
    GuaClockStatus GetClockStatus();
}

public interface IGuaAsyncClockContext
{
    Task<GuaClockResult> InstallClockAsync(TimeSpan? initialTime = null, TimeSpan? step = null, CancellationToken cancellationToken = default);
    Task<GuaClockResult> PauseClockAsync(CancellationToken cancellationToken = default);
    Task<GuaClockResult> RunClockForAsync(TimeSpan duration, TimeSpan? step = null, CancellationToken cancellationToken = default);
    Task<GuaClockResult> ResumeClockAsync(CancellationToken cancellationToken = default);
}

public sealed class GuaClock
{
    private const int CallbackLimit = 1_000_000;
    private readonly GuaContext context;
    private readonly List<Scheduled> scheduled = [];
    private long sequence;
    private bool draining;
    public GuaClock(GuaContext context) : this(context, registerWithContext: true) { }
    internal GuaClock(GuaContext context, bool registerWithContext)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        if (registerWithContext) context.RegisterClock(this);
    }
    public event Action<GuaClockDelta>? Tick;
    public GuaClockStatus Status
    {
        get
        {
            var status = context.GetClockStatus();
            ObserveStatus(status);
            return status;
        }
    }

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
        if (delay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delay));
        if (interval is { } repeat && repeat <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
        var status = Status;
        var bindOnInstall = !status.Installed;
        var delayMs = delay.TotalMilliseconds;
        var intervalMs = interval?.TotalMilliseconds;
        var dueMs = bindOnInstall ? delayMs : status.NowMilliseconds + delayMs;
        if (!bindOnInstall) ValidateRepresentableSchedule(status.NowMilliseconds, delayMs, dueMs, intervalMs);
        var item = new Scheduled(++sequence, status.Generation, dueMs,
            intervalMs, callback, bindOnInstall);
        scheduled.Add(item);
        return new Cancellation(this, item);
    }

    public void DrainPendingSteps()
    {
        if (draining) return;
        draining = true;
        try
        {
        BindPendingSchedules();
        var callbacks = 0;
        while (context.TryConsumeClockStep(out var step))
        {
            scheduled.RemoveAll(item => item.Generation != step.Generation);
            var now = context.GetClockStatus().NowMilliseconds;
            while (scheduled.Where(item => !item.Cancelled && item.DueMs <= now)
                       .OrderBy(item => item.DueMs).ThenBy(item => item.Sequence).FirstOrDefault() is { } item)
            {
                if (++callbacks > CallbackLimit)
                {
                    scheduled.ForEach(candidate => candidate.Cancelled = true);
                    scheduled.Clear();
                    throw new InvalidOperationException("Gua clock execution_limit.");
                }
                if (item.Cancelled) continue;
                scheduled.Remove(item);
                item.Callback?.Invoke();
                var generation = context.GetClockStatus().Generation;
                if (generation != step.Generation)
                {
                    item.Cancelled = true;
                    scheduled.RemoveAll(candidate => candidate.Generation != generation);
                    break;
                }
                if (item.IntervalMs is { } interval && !item.Cancelled)
                {
                    var nextDueMs = item.DueMs + interval;
                    if (double.IsFinite(nextDueMs) && nextDueMs > item.DueMs)
                    {
                        item.DueMs = nextDueMs;
                        scheduled.Add(item);
                    }
                    else item.Cancelled = true;
                }
                else item.Cancelled = true;
            }
            scheduled.RemoveAll(item => item.Cancelled);
            if (context.GetClockStatus().Generation != step.Generation) continue;
            if (Tick is not null)
                foreach (Action<GuaClockDelta> handler in Tick.GetInvocationList())
                {
                    handler(new GuaClockDelta(step.Delta.TotalMilliseconds));
                    if (context.GetClockStatus().Generation != step.Generation) break;
                }
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
            var dueMs = status.NowMilliseconds + item.DueMs;
            try { ValidateRepresentableSchedule(status.NowMilliseconds, item.DueMs, dueMs, item.IntervalMs); }
            catch (ArgumentOutOfRangeException)
            {
                Cancel(item);
                continue;
            }
            item.Generation = status.Generation;
            item.DueMs = dueMs;
            item.BindOnInstall = false;
        }
    }

    internal void ObserveStatus(GuaClockStatus status)
    {
        scheduled.RemoveAll(item => item.Cancelled || !status.Installed && item.Generation != status.Generation);
    }

    private static void Ensure(GuaClockResult result)
    {
        if (result != GuaClockResult.Ok) throw new InvalidOperationException($"Gua clock operation failed: {result}.");
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
    private void Cancel(Scheduled item)
    {
        item.Cancelled = true;
        item.Callback = null;
        scheduled.Remove(item);
    }
    private sealed class Scheduled(long sequence, ulong generation, double dueMs, double? intervalMs, Action callback, bool bindOnInstall)
    { public long Sequence { get; } = sequence; public ulong Generation { get; set; } = generation; public double DueMs { get; set; } = dueMs; public double? IntervalMs { get; } = intervalMs; public Action? Callback { get; set; } = callback; public bool BindOnInstall { get; set; } = bindOnInstall; public bool Cancelled { get; set; } }
    private sealed class Cancellation(GuaClock owner, Scheduled item) : IDisposable
    { private GuaClock? Owner { get; set; } = owner; public void Dispose() { Owner?.Cancel(item); Owner = null; } }
}
