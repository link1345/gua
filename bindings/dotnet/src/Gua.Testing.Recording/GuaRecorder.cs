using System.Diagnostics;
using System.Text.Json;
using Gua.Core;
using Gua.Testing;

namespace Gua.Testing.Recording;

public sealed class GuaRecorder
{
    private readonly IGuaContext _context;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<GuaRecordingStep> _steps = [];

    public GuaRecorder(IGuaContext context) => _context = context ?? throw new ArgumentNullException(nameof(context));

    public GuaRecording Recording
    {
        get { lock (_steps) return new(1, _steps.ToArray()); }
    }

    public Task<GuaActionEvent> ClickAsync(GuaRecordingTarget target, string? waitCondition = null,
        TimeSpan? timeout = null, TimeSpan? pollInterval = null, CancellationToken cancellationToken = default) =>
        RecordAsync(GuaRecordedAction.click, target, waitCondition: waitCondition, timeout: timeout,
            pollInterval: pollInterval, cancellationToken: cancellationToken);

    public Task<GuaActionEvent> FocusAsync(GuaRecordingTarget target, string? waitCondition = null,
        TimeSpan? timeout = null, TimeSpan? pollInterval = null, CancellationToken cancellationToken = default) =>
        RecordAsync(GuaRecordedAction.focus, target, waitCondition: waitCondition, timeout: timeout,
            pollInterval: pollInterval, cancellationToken: cancellationToken);

    public Task<GuaActionEvent> SetValueAsync(GuaRecordingTarget target, string value, bool sensitive = false,
        string? secretKey = null, string? waitCondition = null, TimeSpan? timeout = null,
        TimeSpan? pollInterval = null, CancellationToken cancellationToken = default) =>
        RecordAsync(GuaRecordedAction.set_value, target, value, sensitive, secretKey, waitCondition,
            timeout: timeout, pollInterval: pollInterval, cancellationToken: cancellationToken);

    public Task<GuaActionEvent> SetCheckedAsync(GuaRecordingTarget target, bool value, string? waitCondition = null,
        TimeSpan? timeout = null, TimeSpan? pollInterval = null, CancellationToken cancellationToken = default) =>
        RecordAsync(GuaRecordedAction.set_checked, target, value.ToString().ToLowerInvariant(), waitCondition: waitCondition,
            timeout: timeout, pollInterval: pollInterval, cancellationToken: cancellationToken);

    public Task<GuaActionEvent> SelectAsync(GuaRecordingTarget target, string value, string? waitCondition = null,
        TimeSpan? timeout = null, TimeSpan? pollInterval = null, CancellationToken cancellationToken = default) =>
        RecordAsync(GuaRecordedAction.select, target, value, waitCondition: waitCondition, timeout: timeout,
            pollInterval: pollInterval, cancellationToken: cancellationToken);

    public Task<GuaActionEvent> ScrollAsync(GuaRecordingTarget target, float deltaX, float deltaY, int scrollUnit = 0,
        string? waitCondition = null, TimeSpan? timeout = null, TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default) =>
        RecordAsync(GuaRecordedAction.scroll, target, waitCondition: waitCondition, deltaX: deltaX, deltaY: deltaY,
            scrollUnit: scrollUnit, timeout: timeout, pollInterval: pollInterval, cancellationToken: cancellationToken);

    public Task<GuaActionEvent> PressKeyAsync(string key, GuaRecordingTarget? target = null, uint modifiers = 0,
        string? waitCondition = null, TimeSpan? timeout = null, TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default) =>
        RecordAsync(GuaRecordedAction.press_key, target ?? new(CurrentFocus: true), key, waitCondition: waitCondition,
            modifiers: modifiers, timeout: timeout, pollInterval: pollInterval, cancellationToken: cancellationToken);

    private async Task<GuaActionEvent> RecordAsync(GuaRecordedAction action, GuaRecordingTarget target,
        string? value = null, bool sensitive = false, string? secretKey = null, string? waitCondition = null,
        float deltaX = 0, float deltaY = 0, int scrollUnit = 0, uint modifiers = 0,
        TimeSpan? timeout = null, TimeSpan? pollInterval = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!sensitive && secretKey is not null)
                throw new ArgumentException("secretKey can be supplied only for a sensitive value.", nameof(secretKey));
            var provisional = new GuaRecordingStep(action, _clock.ElapsedMilliseconds, 0, 0, sensitive,
                Target: target, WaitCondition: waitCondition, Value: sensitive ? null : value,
                SecretKey: sensitive ? secretKey : null, DeltaX: deltaX, DeltaY: deltaY,
                ScrollUnit: scrollUnit, Modifiers: modifiers);
            GuaRecordingFile.Validate(new GuaRecording(1, [provisional]));
            if (waitCondition is not null)
                await GuaRecordingRuntime.WaitAsync(
                    _context, waitCondition, timeout, pollInterval, cancellationToken).ConfigureAwait(false);
            var preRevision = ReadRevision(_context);
            var relative = _clock.ElapsedMilliseconds;
            var nodeId = GuaRecordingRuntime.Resolve(_context, target);
            var request = GuaRecordingRuntime.CreateRequest(provisional, nodeId, value);
            var completion = await GuaActionCompletion.EnqueueAndWaitAsync(
                _context, request, timeout, pollInterval, cancellationToken).ConfigureAwait(false);
            var postRevision = completion.Revision ?? ReadRevision(_context);
            var step = provisional with
            {
                RelativeMilliseconds = relative,
                PreRevision = preRevision,
                PostRevision = postRevision,
                RequestId = completion.RequestId,
                EventId = 0,
            };
            lock (_steps) _steps.Add(step);
            return completion;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static ulong ReadRevision(IGuaContext context)
    {
        using var document = JsonDocument.Parse(context.GetUiTreeJson());
        return document.RootElement.TryGetProperty("revision", out var value) && value.TryGetUInt64(out var revision)
            ? revision
            : 0;
    }
}
