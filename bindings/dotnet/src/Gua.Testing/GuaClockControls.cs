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

    public static Task<GuaClockStatus> InstallClockAsync(IGuaContext context, TimeSpan? initialTime = null, TimeSpan? step = null, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(InstallClock(context, initialTime, step)); }
    public static Task<GuaClockStatus> PauseClockAsync(IGuaContext context, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(PauseClock(context)); }
    public static Task<GuaClockStatus> RunClockForAsync(IGuaContext context, TimeSpan duration, TimeSpan? step = null, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(RunClockFor(context, duration, step)); }
    public static Task<GuaClockStatus> ResumeClockAsync(IGuaContext context, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(ResumeClock(context)); }

    private static IGuaClockContext Require(IGuaContext context) => context as IGuaClockContext
        ?? throw new NotSupportedException("This Gua context does not support virtual_clock_v1.");
    private static void Ensure(GuaClockResult result)
    { if (result != GuaClockResult.Ok) throw new InvalidOperationException($"Gua clock operation failed: {result}."); }
}
