using Gua.Core;

namespace Gua.Testing;

public sealed class GuaTestSession
{
    private readonly IGuaContext _context;

    public GuaTestSession(IGuaContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public GuaContextStatus Inspect() => _context.GetContextStatus();

    public void AssertClean()
    {
        var status = Inspect();
        if (status.IsClean) return;
        throw new GuaAssertionException(
            $"Gua test session {status.SessionEpoch} is dirty: pending={status.PendingRequestCount}, " +
            $"inFlight={status.InFlightRequestCount}, events={status.UnconsumedEventCount}, " +
            $"firstRequest={status.FirstPendingAction}:{status.FirstPendingNodeId}, " +
            $"firstEvent={status.FirstEventAction}:{status.FirstEventNodeId}. Payload values are redacted.");
    }

    public GuaResetReport Reset(GuaResetOptions? options = null)
    {
        options ??= new GuaResetOptions();
        var status = Inspect();
        var resolved = options with { ExpectedSessionEpoch = options.ExpectedSessionEpoch ?? status.SessionEpoch };
        var report = _context.Reset(resolved);
        if (report.Result == GuaResetResult.Dirty)
        {
            throw new GuaAssertionException(
                $"Strict Gua reset rejected dirty session {report.SessionEpoch}: " +
                $"pending={report.PendingRequestCount}, inFlight={report.InFlightRequestCount}, " +
                $"events={report.UnconsumedEventCount}, firstRequest={report.FirstPendingAction}:{report.FirstPendingNodeId}, " +
                $"firstEvent={report.FirstEventAction}:{report.FirstEventNodeId}. No state was discarded; payload values are redacted.");
        }
        if (report.Result == GuaResetResult.StaleEpoch)
        {
            throw new InvalidOperationException(
                $"Gua reset rejected stale session epoch {resolved.ExpectedSessionEpoch}; current epoch is {report.SessionEpoch}.");
        }
        return report;
    }

    public Task<GuaResetReport> ResetAsync(GuaResetOptions? options = null, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Reset(options), cancellationToken);
    }
}
