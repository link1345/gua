using System.Diagnostics;
using Gua.Core;

namespace Gua.Testing;

public enum GuaActionFailureKind
{
    Rejected,
    Failed,
    TimedOut,
    Cancelled,
}

public sealed class GuaActionException : Exception
{
    public GuaActionException(
        GuaActionFailureKind kind,
        ulong requestId,
        GuaActionType action,
        string? nodeId,
        GuaActionError error,
        string message,
        Exception? innerException = null) : base(message, innerException)
    {
        Kind = kind;
        RequestId = requestId;
        Action = action;
        NodeId = nodeId;
        Error = error;
    }

    public GuaActionFailureKind Kind { get; }
    public ulong RequestId { get; }
    public GuaActionType Action { get; }
    public string? NodeId { get; }
    public GuaActionError Error { get; }
}

public static class GuaActionCompletion
{
    public static TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(1);
    public static TimeSpan DefaultPollInterval { get; set; } = TimeSpan.FromMilliseconds(10);

    public static GuaActionEvent EnqueueAndWait(
        IGuaContext context,
        GuaActionRequest request,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null) =>
        EnqueueAndWaitAsync(context, request, timeout, pollInterval).GetAwaiter().GetResult();

    public static async Task<GuaActionEvent> EnqueueAndWaitAsync(
        IGuaContext context,
        GuaActionRequest request,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var limit = timeout ?? DefaultTimeout;
        var interval = pollInterval ?? DefaultPollInterval;
        if (limit < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(pollInterval));

        var error = context.EnqueueAction(request, out var requestId);
        if (error != GuaActionError.None)
        {
            throw Create(context, GuaActionFailureKind.Rejected, requestId, request.Action, request.NodeId, error,
                $"Gua action was rejected: requestId={requestId}, action={request.Action}, nodeId='{request.NodeId ?? "<focused>"}', error={error}.");
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (context.TryPollActionEvent(requestId, out var result))
                {
                    if (!result.Succeeded)
                    {
                        throw Create(context, GuaActionFailureKind.Failed, requestId, request.Action, request.NodeId, result.Error,
                            $"Gua action failed: requestId={requestId}, action={request.Action}, nodeId='{request.NodeId ?? "<focused>"}', error={result.Error}.");
                    }
                    return result;
                }
                if (stopwatch.Elapsed >= limit) break;
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            }
            while (true);
        }
        catch (OperationCanceledException cancellationError) when (cancellationToken.IsCancellationRequested)
        {
            throw Create(context, GuaActionFailureKind.Cancelled, requestId, request.Action, request.NodeId, GuaActionError.None,
                $"Gua action wait was cancelled: requestId={requestId}, action={request.Action}, nodeId='{request.NodeId ?? "<focused>"}'.", cancellationError);
        }

        throw Create(context, GuaActionFailureKind.TimedOut, requestId, request.Action, request.NodeId, GuaActionError.None,
            $"Timed out after {limit:g} waiting for Gua action: requestId={requestId}, action={request.Action}, nodeId='{request.NodeId ?? "<focused>"}'.");
    }

    private static GuaActionException Create(
        IGuaContext context, GuaActionFailureKind kind, ulong requestId, GuaActionType action,
        string? nodeId, GuaActionError error, string message, Exception? inner = null)
    {
        var suffix = GuaAssertions.DescribeSnapshot(context);
        return new GuaActionException(kind, requestId, action, nodeId, error, $"{message} {suffix}", inner);
    }
}
