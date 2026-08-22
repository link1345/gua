using Gua.Core;

namespace Gua.Testing;

public static class GuaClockControls
{
    public static GuaClockStatus InstallClock(IGuaContext context, TimeSpan? initialTime = null, TimeSpan? step = null)
    { var clock = Require(context); Ensure(clock.InstallClock(initialTime, step)); return clock.GetClockStatus(); }
    public static GuaClockStatus PauseClock(IGuaContext context)
    { var clock = Require(context); Ensure(clock.PauseClock()); return clock.GetClockStatus(); }
    public static GuaClockStatus RunClockFor(IGuaContext context, TimeSpan duration, TimeSpan? step = null)
    { var clock = Require(context); Ensure(clock.RunClockFor(duration, step)); return clock.GetClockStatus(); }
    public static GuaClockStatus ResumeClock(IGuaContext context)
    { var clock = Require(context); Ensure(clock.ResumeClock()); return clock.GetClockStatus(); }
    public static GuaClockStatus GetClockStatus(IGuaContext context) => Require(context).GetClockStatus();

    public static async Task<GuaClockStatus> InstallClockAsync(IGuaContext context, TimeSpan? initialTime = null, TimeSpan? step = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context is IGuaAsyncClockContext asyncClock)
        {
            Ensure(await asyncClock.InstallClockAsync(initialTime, step, cancellationToken).ConfigureAwait(false));
            return Require(context).GetClockStatus();
        }
        return InstallClock(context, initialTime, step);
    }
    public static async Task<GuaClockStatus> PauseClockAsync(IGuaContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context is IGuaAsyncClockContext asyncClock)
        {
            Ensure(await asyncClock.PauseClockAsync(cancellationToken).ConfigureAwait(false));
            return Require(context).GetClockStatus();
        }
        return PauseClock(context);
    }
    public static async Task<GuaClockStatus> RunClockForAsync(IGuaContext context, TimeSpan duration, TimeSpan? step = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context is IGuaAsyncClockContext asyncClock)
        {
            Ensure(await asyncClock.RunClockForAsync(duration, step, cancellationToken).ConfigureAwait(false));
            return Require(context).GetClockStatus();
        }
        return RunClockFor(context, duration, step);
    }
    public static async Task<GuaClockStatus> ResumeClockAsync(IGuaContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context is IGuaAsyncClockContext asyncClock)
        {
            Ensure(await asyncClock.ResumeClockAsync(cancellationToken).ConfigureAwait(false));
            return Require(context).GetClockStatus();
        }
        return ResumeClock(context);
    }

    private static IGuaClockContext Require(IGuaContext context) => context as IGuaClockContext
        ?? throw new NotSupportedException("This Gua context does not support virtual_clock_v1.");
    private static void Ensure(GuaClockResult result)
    { if (result != GuaClockResult.Ok) throw new InvalidOperationException($"Gua clock operation failed: {result}."); }
}
