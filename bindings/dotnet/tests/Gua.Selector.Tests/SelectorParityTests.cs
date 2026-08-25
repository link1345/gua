using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using Gua.Core;
using Gua.Runtime;
using Gua.Testing;
using Gua.Testing.Godot;
using NUnit.Framework;

namespace Gua.Selector.Tests;

[TestFixture]
public sealed class SelectorParityTests
{
    [Test]
    public void LocalClockControlsDrainTheContextsOwnedClock()
    {
        using var context = new GuaContext();
        var clock = new GuaClock(context);
        var callbacks = 0;
        var ticks = new List<double>();
        clock.Schedule(TimeSpan.FromMilliseconds(20), () => callbacks++);
        clock.Tick += delta => ticks.Add(delta.TotalMilliseconds);

        GuaClockControls.InstallClock(context, step: TimeSpan.FromMilliseconds(10));
        GuaClockControls.PauseClock(context);
        var status = GuaClockControls.RunClockFor(context, TimeSpan.FromMilliseconds(25));

        Assert.Multiple(() =>
        {
            Assert.That(status.NowMilliseconds, Is.EqualTo(25));
            Assert.That(status.PendingMilliseconds, Is.Zero);
            Assert.That(callbacks, Is.EqualTo(1));
            Assert.That(ticks, Is.EqualTo(new[] { 10.0, 10.0, 5.0 }));
            Assert.That(context.Clock, Is.SameAs(clock));
        });

        var captured = ScheduleCapturedCallback(clock);
        Assert.That(context.Reset().Result, Is.EqualTo(GuaResetResult.Succeeded));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var resetStatus = GuaClockControls.GetClockStatus(context);
        Assert.Multiple(() =>
        {
            Assert.That(resetStatus.Installed, Is.False);
            Assert.That(resetStatus.NowMilliseconds, Is.Zero);
            Assert.That(resetStatus.PendingMilliseconds, Is.Zero);
            Assert.That(resetStatus.DefaultStepMilliseconds, Is.EqualTo(1000.0 / 60.0));
            Assert.That(captured.IsAlive, Is.False, "Clock reset must release stale callback closures immediately.");
            Assert.That((uint)GuaResetTargets.Default, Is.EqualTo(15));
            Assert.That((uint)GuaResetTargets.SessionDefault, Is.EqualTo(79));
            Assert.That((uint)GuaResetTargets.All, Is.EqualTo(63));
            Assert.That((uint)GuaResetTargets.AllWithClock, Is.EqualTo(127));
        });

        clock.Install(step: TimeSpan.FromMilliseconds(10));
        var explicitGeneration = clock.Status.Generation;
        Assert.That(context.Reset(new GuaResetOptions(GuaResetTargets.Default)).Result, Is.EqualTo(GuaResetResult.Succeeded));
        Assert.That(clock.Status.Installed, Is.True);
        Assert.That(clock.Status.Generation, Is.EqualTo(explicitGeneration));
    }

    [Test]
    public void ContextRejectsASecondManagedClock()
    {
        using var context = new GuaContext();
        var clock = new GuaClock(context);

        Assert.That(context.Clock, Is.SameAs(clock));
        Assert.That(() => new GuaClock(context),
            Throws.InvalidOperationException.With.Message.Contains("only one GuaClock"));
        Assert.That(context.Clock, Is.SameAs(clock));
    }

    private static WeakReference ScheduleCapturedCallback(GuaClock clock)
    {
        var captured = new object();
        var reference = new WeakReference(captured);
        clock.Schedule(TimeSpan.FromHours(1), () => GC.KeepAlive(captured));
        return reference;
    }

    [Test]
    public void SchedulesCreatedBeforeInstallationBindToTheFirstClockGeneration()
    {
        using var context = new GuaContext();
        var clock = new GuaClock(context);
        var coreCallbackCount = 0;
        clock.Schedule(TimeSpan.FromMilliseconds(20), () => coreCallbackCount++);
        clock.Install(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(10));
        clock.Pause();
        clock.RunFor(TimeSpan.FromMilliseconds(25));

        using var runtime = new GuaRuntime();
        var runtimeCallbackCount = 0;
        runtime.Clock.Schedule(TimeSpan.FromMilliseconds(20), () => runtimeCallbackCount++);
        runtime.Clock.Install(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(10));
        runtime.Clock.Pause();
        runtime.Clock.RunFor(TimeSpan.FromMilliseconds(25));

        using var resetContext = new GuaContext();
        var resetClock = new GuaClock(resetContext);
        var staleCallbackCount = 0;
        resetClock.Schedule(TimeSpan.FromMilliseconds(20), () => staleCallbackCount++);
        Assert.That(resetContext.Reset().Result, Is.EqualTo(GuaResetResult.Succeeded));
        resetClock.Install(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(10));
        resetClock.Pause();
        resetClock.RunFor(TimeSpan.FromMilliseconds(25));

        Assert.Multiple(() =>
        {
            Assert.That(coreCallbackCount, Is.EqualTo(1));
            Assert.That(clock.Status.NowMilliseconds, Is.EqualTo(125));
            Assert.That(runtimeCallbackCount, Is.EqualTo(1));
            Assert.That(runtime.Clock.Status.NowMilliseconds, Is.EqualTo(125));
            Assert.That(staleCallbackCount, Is.Zero);
        });
    }

    [Test]
    public void ClockCallbacksCanRunNestedAdvancesWithoutReenteringTheSameSchedule()
    {
        using var context = new GuaContext();
        var clock = new GuaClock(context);
        clock.Install(step: TimeSpan.FromMilliseconds(10));
        clock.Pause();
        var callbacks = 0;
        var coreTicks = new List<double>();
        clock.Tick += tick => coreTicks.Add(tick.TotalMilliseconds);
        clock.Schedule(TimeSpan.FromMilliseconds(10), () =>
        {
            callbacks++;
            clock.RunFor(TimeSpan.FromMilliseconds(5));
        });
        clock.RunFor(TimeSpan.FromMilliseconds(10));
        Assert.Multiple(() =>
        {
            Assert.That(callbacks, Is.EqualTo(1));
            Assert.That(clock.Status.NowMilliseconds, Is.EqualTo(15));
            Assert.That(coreTicks, Is.EqualTo(new[] { 10.0, 5.0 }));
        });

        using var runtime = new GuaRuntime();
        runtime.Clock.Install(step: TimeSpan.FromMilliseconds(10));
        runtime.Clock.Pause();
        var runtimeCallbacks = 0;
        var runtimeTicks = new List<double>();
        runtime.Clock.Tick += tick => runtimeTicks.Add(tick.TotalMilliseconds);
        runtime.Clock.Schedule(TimeSpan.FromMilliseconds(10), () =>
        {
            runtimeCallbacks++;
            runtime.Clock.RunFor(TimeSpan.FromMilliseconds(5));
        });
        runtime.Clock.RunFor(TimeSpan.FromMilliseconds(10));
        Assert.Multiple(() =>
        {
            Assert.That(runtimeCallbacks, Is.EqualTo(1));
            Assert.That(runtimeTicks, Is.EqualTo(new[] { 10.0, 5.0 }));
        });
    }

    [Test]
    public void ClockResetInsideRepeatingCallbackDropsTheStaleGeneration()
    {
        using var context = new GuaContext();
        var clock = new GuaClock(context);
        clock.Install(step: TimeSpan.FromMilliseconds(10));
        clock.Pause();
        var callbacks = 0;
        var coreTicks = 0;
        clock.Tick += _ => coreTicks++;
        clock.Schedule(TimeSpan.FromMilliseconds(1), () =>
        {
            callbacks++;
            context.Reset();
        }, TimeSpan.FromMilliseconds(1));

        clock.RunFor(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10));
        Assert.That(callbacks, Is.EqualTo(1));
        Assert.That(coreTicks, Is.Zero, "A reset callback must suppress the stale generation's tick.");
        clock.Install(step: TimeSpan.FromMilliseconds(10));
        clock.Pause();
        clock.RunFor(TimeSpan.FromMilliseconds(10));
        Assert.That(callbacks, Is.EqualTo(1));

        var port = ReservePort();
        using var runtime = new GuaRuntime();
        runtime.BeginFrame("fixture"); runtime.EndFrame();
        Assert.That(runtime.StartInspectorBridge(port), Is.True);
        try
        {
            using var remote = new GuaWebSocketContext($"ws://127.0.0.1:{port}", TimeSpan.FromSeconds(2));
            remote.WaitUntilAvailable(TimeSpan.FromSeconds(2));
            runtime.Clock.Install(step: TimeSpan.FromMilliseconds(10));
            runtime.Clock.Pause();
            var runtimeCallbacks = 0;
            var runtimeTicks = 0;
            runtime.Clock.Tick += _ => runtimeTicks++;
            runtime.Clock.Schedule(TimeSpan.FromMilliseconds(1), () =>
            {
                runtimeCallbacks++;
                remote.Reset();
            }, TimeSpan.FromMilliseconds(1));

            runtime.Clock.RunFor(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10));
            Assert.That(runtimeCallbacks, Is.EqualTo(1));
            Assert.That(runtimeTicks, Is.Zero, "A remote reset callback must suppress the stale generation's tick.");
            runtime.Clock.Install(step: TimeSpan.FromMilliseconds(10));
            runtime.Clock.Pause();
            runtime.Clock.RunFor(TimeSpan.FromMilliseconds(10));
            Assert.That(runtimeCallbacks, Is.EqualTo(1));
        }
        finally
        {
            runtime.StopInspectorBridge();
        }
    }

    [Test]
    public void ClockExecutionLimitRetiresRemainingSchedules()
    {
        using var context = new GuaContext();
        var clock = new GuaClock(context);
        clock.Install(step: TimeSpan.FromMilliseconds(101));
        clock.Pause();
        var callbacks = 0;
        clock.Schedule(TimeSpan.Zero, () => callbacks++, TimeSpan.FromTicks(1));
        Assert.That(() => clock.RunFor(TimeSpan.FromMilliseconds(101)),
            Throws.InvalidOperationException.With.Message.Contains("execution_limit"));
        Assert.That(callbacks, Is.EqualTo(1_000_000));
        Assert.That(() => clock.RunFor(TimeSpan.FromMilliseconds(1)), Throws.Nothing);
        Assert.That(callbacks, Is.EqualTo(1_000_000));

        using var runtime = new GuaRuntime();
        runtime.Clock.Install(step: TimeSpan.FromMilliseconds(101));
        runtime.Clock.Pause();
        var runtimeCallbacks = 0;
        runtime.Clock.Schedule(TimeSpan.Zero, () => runtimeCallbacks++, TimeSpan.FromTicks(1));
        Assert.That(() => runtime.Clock.RunFor(TimeSpan.FromMilliseconds(101)),
            Throws.InvalidOperationException.With.Message.Contains("execution_limit"));
        Assert.That(runtimeCallbacks, Is.EqualTo(1_000_000));
        Assert.That(() => runtime.Clock.RunFor(TimeSpan.FromMilliseconds(1)), Throws.Nothing);
        Assert.That(runtimeCallbacks, Is.EqualTo(1_000_000));
    }

    [Test]
    public void RuntimeClockIsolatesCallbackFailuresAndPurgesInactiveSchedules()
    {
        using var runtime = new GuaRuntime();
        runtime.Clock.Install(step: TimeSpan.FromMilliseconds(10));
        runtime.Clock.Pause();
        var callbacks = new List<string>();
        var callbackFailures = new List<Exception>();
        runtime.Clock.CallbackFailed += error => callbackFailures.Add(error);
        runtime.Clock.Schedule(TimeSpan.FromMilliseconds(10), () => throw new InvalidOperationException("boom"));
        runtime.Clock.Schedule(TimeSpan.FromMilliseconds(10), () => callbacks.Add("second"));
        runtime.Clock.Tick += _ => throw new ApplicationException("tick-boom");
        runtime.Clock.Tick += _ => callbacks.Add("tick");

        Assert.That(() => runtime.Clock.RunFor(TimeSpan.FromMilliseconds(10)), Throws.Nothing);
        Assert.Multiple(() =>
        {
            Assert.That(callbackFailures, Has.Count.EqualTo(2));
            Assert.That(callbackFailures[0], Is.TypeOf<InvalidOperationException>());
            Assert.That(callbackFailures[1], Is.TypeOf<ApplicationException>());
            Assert.That(callbacks, Is.EqualTo(new[] { "second", "tick" }));
            Assert.That(ScheduledCount(runtime.Clock), Is.Zero);
        });

        var cancelled = runtime.Clock.Schedule(TimeSpan.FromSeconds(1), () => callbacks.Add("cancelled"));
        Assert.That(ScheduledCount(runtime.Clock), Is.EqualTo(1));
        cancelled.Dispose();
        Assert.That(ScheduledCount(runtime.Clock), Is.Zero,
            "Disposal must release the callback without waiting for another clock step.");

        var port = ReservePort();
        runtime.BeginFrame("fixture"); runtime.EndFrame();
        Assert.That(runtime.StartInspectorBridge(port), Is.True);
        try
        {
            using var remote = new GuaWebSocketContext($"ws://127.0.0.1:{port}", TimeSpan.FromSeconds(2));
            remote.WaitUntilAvailable(TimeSpan.FromSeconds(2));
            runtime.Clock.Schedule(TimeSpan.FromSeconds(1), () => callbacks.Add("stale"));
            remote.Reset();
            runtime.Clock.Advance(TimeSpan.Zero);
            Assert.That(ScheduledCount(runtime.Clock), Is.Zero,
                "An uninstalled clock must purge schedules from the reset generation.");
        }
        finally
        {
            runtime.StopInspectorBridge();
        }
    }

    [Test]
    public async Task RuntimeClockPublishesSubTimeSpanTickPrecision()
    {
        var port = ReservePort();
        using var runtime = new GuaRuntime();
        runtime.EnableVirtualClockAdapter();
        runtime.BeginFrame("fixture"); runtime.EndFrame();
        Assert.That(runtime.StartInspectorBridge(port), Is.True);
        try
        {
            var preciseTicks = new List<double>();
            runtime.Clock.Tick += tick => preciseTicks.Add(tick.TotalMilliseconds);
            using var socket = new ClientWebSocket();
            await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}"), CancellationToken.None);
            await SendRawCommandAsync(socket,
                "{\"id\":1,\"type\":\"clock_install\",\"initialTimeMs\":0,\"stepMs\":0.00001}");
            await SendRawCommandAsync(socket, "{\"id\":2,\"type\":\"clock_pause\"}");
            await SendRawCommandAsync(socket,
                "{\"id\":3,\"type\":\"clock_run_for\",\"durationMs\":0.00002,\"stepMs\":0.00001}");

            runtime.Clock.Drain();

            Assert.That(preciseTicks, Is.EqualTo(new[] { 0.00001, 0.00001 }));
            Assert.That(runtime.Clock.Status.NowMilliseconds, Is.EqualTo(0.00002).Within(1e-12));
        }
        finally
        {
            runtime.StopInspectorBridge();
        }
    }

    [Test]
    public void RemoteTreeDeserializesProtocolBoundsAndNestedState()
    {
        const string json = """
            {"screen":"fixture","revision":4,"nodes":[{"id":"remember","role":"checkbox","label":"Remember","visible":true,"enabled":true,"bounds":{"x":10,"y":20,"w":30,"h":40},"state":{"focused":false,"checked":true,"selected":false},"actions":["click"]}]}
            """;
        var tree = JsonSerializer.Deserialize<GuaRemoteTree>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var node = tree!.Nodes.Single();
        Assert.Multiple(() =>
        {
            Assert.That(node.Bounds.X, Is.EqualTo(10));
            Assert.That(node.Bounds.Y, Is.EqualTo(20));
            Assert.That(node.Bounds.Width, Is.EqualTo(30));
            Assert.That(node.Bounds.Height, Is.EqualTo(40));
            Assert.That(node.Focused, Is.False);
            Assert.That(node.Checked, Is.True);
            Assert.That(node.Selected, Is.False);
        });
    }

    [Test]
    public void RuntimeJsonCopiesRemainValidWhileVersionSizeChanges()
    {
        using var runtime = new GuaRuntime();
        var writer = Task.Run(() =>
        {
            for (var index = 0; index < 10_000; index++)
                runtime.SetAdapterVersion("unity", index % 2 == 0 ? "1" : new string('2', 1000));
        });
        for (var index = 0; index < 10_000; index++)
            using (JsonDocument.Parse(runtime.GetVersionJson())) { }
        writer.GetAwaiter().GetResult();
    }

    [Test]
    public void VersionModelReportsCapabilitiesAndCompatibilityFailures()
    {
        using var context = new GuaContext();
        var version = context.GetVersion();
        Assert.That(version.GodotPluginVersion, Is.Null);
        Assert.That(version.Capabilities, Does.Contain("version_v1"));
        Assert.DoesNotThrow(() => version.EnsureCompatible("2", 1, ["version_v1"]));
        var error = Assert.Throws<GuaCompatibilityException>(() =>
            version.EnsureCompatible("999", 999, ["missing_capability"]));
        Assert.That(error!.Message, Does.Contain("actual protocol=2"));
        Assert.That(error.MissingCapabilities, Does.Contain("missing_capability"));
    }

    [Test]
    public async Task WaitExpectationRetainsSnapshotAndCountWaitPollsLatestFrames()
    {
        using var context = new GuaContext();
        context.BeginFrame("first");
        context.RegisterNode(new GuaNodeDescriptor("item-1", "listitem", "One", new GuaBounds(0, 0, 1, 1)));
        context.EndFrame();

        var retained = GuaAssertions.WaitForId(context, "item-1");
        var firstFrame = retained.Snapshot.FrameSequence;
        Assert.That(firstFrame, Is.Not.Null);
        context.BeginFrame("second");
        context.RegisterNode(new GuaNodeDescriptor("item-1", "listitem", "One", new GuaBounds(0, 0, 1, 1), Enabled: false));
        context.RegisterNode(new GuaNodeDescriptor("item-2", "listitem", "Two", new GuaBounds(0, 1, 1, 1)));
        context.EndFrame();

        retained.ToExist().ToBeEnabled();
        Assert.That(retained.Snapshot.FrameSequence, Is.EqualTo(firstFrame));
        retained.Refresh().ToBeDisabled();
        Assert.That(retained.Snapshot.FrameSequence, Is.GreaterThan(firstFrame!.Value));

        await GuaAssertions.Query(context).ByRole("listitem")
            .WaitForCountAsync(count => count >= 2, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(5));
        GuaAssertions.Query(context).ByRole("missing").WaitForCount(0, TimeSpan.FromSeconds(1));
    }

    [Test]
    public async Task DetailedSemanticStatePreservesObservedZeroAndWaitsByPredicate()
    {
        using var context = new GuaContext();
        context.BeginFrame("details");
        context.RegisterNode(new GuaNodeDescriptor("editor", "textbox", "Editor", new GuaBounds(0, 0, 1, 1),
            CaretPosition: 0, SelectionStart: 0, SelectionEnd: 0, ScrollX: 0, ScrollY: 12,
            ScrollMaxX: 0, ScrollMaxY: 100, RangeValue: 5, RangeMin: 0, RangeMax: 10, SelectedIndex: 0));
        context.EndFrame();

        var state = await GuaAssertions.WaitForStateAsync(context, "editor", node =>
            node.CaretPosition == 0 && node.ScrollY == 12 && node.RangeValue == 5 && node.SelectedIndex == 0);
        Assert.Multiple(() =>
        {
            Assert.That(state.Snapshot.CaretPosition, Is.Zero);
            Assert.That(state.Snapshot.SelectionStart, Is.Zero);
            Assert.That(state.Snapshot.ScrollMaxY, Is.EqualTo(100));
            Assert.That(state.Snapshot.RangeMin, Is.Zero);
        });
    }

    [Test]
    public async Task V2LocatorAndActionCompletionPreserveUnrelatedEvents()
    {
        using var context = new GuaContext();
        context.BeginFrame("form");
        context.RegisterNode(new GuaNodeDescriptor("form", "panel", "Form", new GuaBounds(0, 0, 10, 10)));
        context.RegisterNode(new GuaNodeDescriptor("name", "textbox", "Name", new GuaBounds(0, 0, 1, 1),
            ParentId: "form", Text: "User", Value: "alice", Focused: true));
        context.RegisterNode(new GuaNodeDescriptor("remember", "checkbox", "Remember", new GuaBounds(0, 1, 1, 1),
            ParentId: "form", Checked: false));
        context.EndFrame();

        var located = GuaAssertions.Query(context).ByRole("textbox").Within("form").ByValue("alice").WhereFocused().ByAction("set_value").Get();
        Assert.That(located.Id, Is.EqualTo("name"));
        Assert.That(GuaAssertions.Query(context).WhereChecked(false).Get().Id, Is.EqualTo("remember"));

        var completion = located.SetValueAsync("bob", timeout: TimeSpan.FromSeconds(2));
        Assert.That(context.TryConsumeAction(GuaActionType.SetValue, "name", out var request), Is.True);
        var otherId = located.Focus();
        Assert.That(context.TryConsumeAction(GuaActionType.Focus, "name", out var other), Is.True);
        Assert.That(context.EmitActionResult(new GuaActionEvent(otherId, GuaActionType.Focus, true, GuaActionError.None, "name", "", false)), Is.True);
        Assert.That(context.EmitActionResult(new GuaActionEvent(request.RequestId, GuaActionType.SetValue, true, GuaActionError.None, "name", "bob", false)), Is.True);

        var result = await completion;
        Assert.Multiple(() =>
        {
            Assert.That(result.RequestId, Is.EqualTo(request.RequestId));
            Assert.That(result.Action, Is.EqualTo(GuaActionType.SetValue));
            Assert.That(result.SessionEpoch, Is.EqualTo(1));
            Assert.That(result.FrameSequence, Is.EqualTo(1));
            Assert.That(result.Revision, Is.EqualTo(1));
            Assert.That(context.TryPollActionEvent(otherId, out var preserved), Is.True);
            Assert.That(preserved.Action, Is.EqualTo(GuaActionType.Focus));
        });
    }

    [Test]
    public void TypedActionReportsRejectionDetails()
    {
        using var context = new GuaContext();
        context.BeginFrame("form");
        context.RegisterNode(new GuaNodeDescriptor("disabled", "textbox", "Disabled", new GuaBounds(0, 0, 1, 1), Enabled: false));
        context.EndFrame();

        var error = Assert.Throws<GuaActionException>(() =>
            GuaAssertions.GetById(context, "disabled").SetValueAndWait("value"));
        Assert.Multiple(() =>
        {
            Assert.That(error!.Kind, Is.EqualTo(GuaActionFailureKind.Rejected));
            Assert.That(error.Error, Is.EqualTo(GuaActionError.Disabled));
            Assert.That(error.Message, Does.Contain("requestId=0").And.Contain("screen='form'").And.Contain("frameSequence="));
        });
    }

    [Test]
    public void LocalAndRemoteContextsUseTheSameNativeSelectorEvaluator()
    {
        var port = ReservePort();
        var runtime = Native.gua_runtime_create();
        Assert.That(runtime, Is.Not.EqualTo(nint.Zero));
        try
        {
            Native.gua_runtime_begin_frame(runtime, "fixture");
            Native.gua_runtime_register_node(runtime, "save-a", "button", "Save", new GuaBounds(0, 0, 1, 1), 1, 1);
            Native.gua_runtime_register_node(runtime, "save-b", "button", "Save as", new GuaBounds(0, 0, 1, 1), 1, 1);
            Native.gua_runtime_end_frame(runtime);
            Assert.That(Native.gua_runtime_start_inspector_bridge(runtime, port), Is.EqualTo(1));

            using var local = new GuaContext();
            local.BeginFrame("fixture");
            local.RegisterNode("save-a", "button", "Save", new GuaBounds(0, 0, 1, 1));
            local.RegisterNode("save-b", "button", "Save as", new GuaBounds(0, 0, 1, 1));
            local.EndFrame();

            using var remote = new GuaRemoteContext($"ws://127.0.0.1:{port}", TimeSpan.FromSeconds(2));
            remote.WaitUntilAvailable(TimeSpan.FromSeconds(2));
            GuaAssertions.WaitForVisible(remote, "save-a", pollInterval: TimeSpan.FromMilliseconds(5));
            GuaAssertions.WaitForHidden(remote, "missing", pollInterval: TimeSpan.FromMilliseconds(5));
            var selector = new GuaSelector(Role: "button", Name: "^Save", NameMatch: GuaMatchMode.Regex);
            var remoteResult = remote.Query(selector);
            var localResult = local.Query(selector);
            Assert.That(remoteResult.Matches.Select(match => match.Id),
                Is.EqualTo(localResult.Matches.Select(match => match.Id)));
            var remoteVersion = remote.GetVersion();
            var localVersion = local.GetVersion();
            Assert.Multiple(() =>
            {
                Assert.That(remoteVersion.ProtocolSchemaVersion, Is.EqualTo(localVersion.ProtocolSchemaVersion));
                Assert.That(remoteVersion.AbiVersion, Is.EqualTo(localVersion.AbiVersion));
                Assert.That(remoteVersion.BuildId, Is.EqualTo(localVersion.BuildId));
                Assert.That(remoteVersion.Capabilities, Does.Not.Contain("virtual_clock_v1"));
                Assert.That(localVersion.Capabilities, Does.Contain("virtual_clock_v1"));
            });
            Assert.That(remoteVersion.Capabilities, Does.Contain("version_v1"));
            Assert.That(() => remote.GetClockStatus(),
                Throws.InvalidOperationException.With.Message.Contains("unsupported"));
            Assert.That(() => remote.InstallClock(),
                Throws.InvalidOperationException.With.Message.Contains("unsupported"));

            using var sharedRemote = new GuaWebSocketContext($"ws://127.0.0.1:{port}", TimeSpan.FromSeconds(2));
            sharedRemote.WaitUntilAvailable(TimeSpan.FromSeconds(2));
            Assert.That(sharedRemote.EnqueueClick("save-a"), Is.True);
            Assert.That(Native.gua_runtime_consume_click_request(runtime, "save-a"), Is.EqualTo(1));
            Assert.That(Native.gua_runtime_emit_click(runtime, "save-a"), Is.EqualTo(1));
            Assert.That(sharedRemote.TryPollEvent(out var legacyEvent), Is.True);
            Assert.That(legacyEvent.Type, Is.EqualTo(GuaEventType.Click));
            Assert.That(legacyEvent.NodeId, Is.EqualTo("save-a"));

            var invalid = new GuaSelector(Text: "[", TextMatch: GuaMatchMode.Regex);
            Assert.Multiple(() =>
            {
                Assert.That(local.Query(invalid).Valid, Is.False);
                Assert.That(remote.Query(invalid).Valid, Is.False);
            });
            using var diagnostics = JsonDocument.Parse(remote.GetDiagnosticsJson());
            Assert.Multiple(() =>
            {
                Assert.That(diagnostics.RootElement.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
                Assert.That(diagnostics.RootElement.GetProperty("uiTree").GetProperty("screen").GetString(), Is.EqualTo("fixture"));
                Assert.That(diagnostics.RootElement.GetProperty("screenshot").ValueKind, Is.EqualTo(JsonValueKind.Null));
                Assert.That(diagnostics.RootElement.GetProperty("version").GetProperty("abiVersion").GetInt32(), Is.EqualTo(1));
            });
        }
        finally
        {
            Native.gua_runtime_stop_inspector_bridge(runtime);
            Native.gua_runtime_destroy(runtime);
        }
    }

    [Test]
    public async Task GodotRemoteClockWaitsForTheHostFrameAfterCoreStepsAreConsumed()
    {
        var port = ReservePort();
        using var runtime = new GuaRuntime();
        runtime.EnableVirtualClockAdapter();
        runtime.BeginFrame("fixture");
        runtime.EndFrame();
        Assert.That(runtime.StartInspectorBridge(port), Is.True);
        try
        {
            using var remote = new GuaRemoteContext($"ws://127.0.0.1:{port}", TimeSpan.FromSeconds(2));
            using var secondRemote = new GuaRemoteContext($"ws://127.0.0.1:{port}", TimeSpan.FromSeconds(2));
            remote.WaitUntilAvailable(TimeSpan.FromSeconds(2));
            secondRemote.WaitUntilAvailable(TimeSpan.FromSeconds(2));
            Assert.That(remote.GetVersion().Capabilities, Does.Contain("virtual_clock_v1"));
            GuaClockControls.InstallClock(remote, step: TimeSpan.FromMilliseconds(10));
            GuaClockControls.PauseClock(remote);

            var runFor = Task.Run(() => GuaClockControls.RunClockFor(remote, TimeSpan.FromMilliseconds(25)));
            Assert.That(SpinWait.SpinUntil(() => runtime.Clock.Status.PendingMilliseconds > 0, TimeSpan.FromSeconds(2)), Is.True);
            runtime.Clock.Advance(TimeSpan.Zero);
            await Task.Delay(50);
            Assert.That(runFor.IsCompleted, Is.False, "Core pendingMs=0 must not complete run_for before the adapter publishes its frame.");

            var secondRunFor = Task.Run(() => GuaClockControls.RunClockFor(secondRemote, TimeSpan.FromMilliseconds(100)));
            Assert.That(SpinWait.SpinUntil(() => runtime.Clock.Status.PendingMilliseconds > 0, TimeSpan.FromSeconds(2)), Is.True);
            runtime.BeginFrame("fixture");
            runtime.EndFrame();
            var status = await runFor.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.That(status.NowMilliseconds, Is.EqualTo(25));
            Assert.That(secondRunFor.IsCompleted, Is.False,
                "Run A must complete from its own operation sequence without waiting for run B, while run B remains pending.");

            runtime.Clock.Advance(TimeSpan.Zero);
            runtime.BeginFrame("fixture"); runtime.EndFrame();
            var secondStatus = await secondRunFor.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.That(secondStatus.NowMilliseconds, Is.EqualTo(125));

            var fastRunFor = Task.Run(() => GuaClockControls.RunClockFor(remote, TimeSpan.FromMilliseconds(5)));
            Assert.That(SpinWait.SpinUntil(() => runtime.Clock.Status.PendingMilliseconds > 0, TimeSpan.FromSeconds(2)), Is.True);
            runtime.Clock.Advance(TimeSpan.Zero);
            runtime.BeginFrame("fixture"); runtime.EndFrame();
            var fastStatus = await fastRunFor.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.That(fastStatus.NowMilliseconds, Is.EqualTo(130),
                "The correlated completion frame may be published before the client receives clock_run_for's response.");
        }
        finally
        {
            runtime.StopInspectorBridge();
        }
    }

    [Test]
    public async Task RemoteRunForRejectsExplicitZeroStepsAndHonorsCancellation()
    {
        var port = ReservePort();
        using var runtime = new GuaRuntime();
        runtime.EnableVirtualClockAdapter();
        runtime.BeginFrame("fixture"); runtime.EndFrame();
        Assert.That(runtime.StartInspectorBridge(port), Is.True);
        try
        {
            using var remote = new GuaRemoteContext($"ws://127.0.0.1:{port}", TimeSpan.FromSeconds(2));
            remote.WaitUntilAvailable(TimeSpan.FromSeconds(2));
            Assert.That(() => remote.InstallClock(step: TimeSpan.Zero), Throws.InvalidOperationException);
            remote.InstallClock(step: TimeSpan.FromMilliseconds(10));
            remote.PauseClock();
            Assert.That(() => remote.RunClockFor(TimeSpan.FromMilliseconds(10), TimeSpan.Zero), Throws.InvalidOperationException);

            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            var stopwatch = Stopwatch.StartNew();
            Assert.That(async () =>
                await GuaClockControls.RunClockForAsync(remote, TimeSpan.FromMilliseconds(25), cancellationToken: cancellation.Token),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1)));
        }
        finally { runtime.StopInspectorBridge(); }
    }

    [Test]
    public async Task RemotePauseWaitsForAnInFlightRunningStepAndItsHostFrame()
    {
        var port = ReservePort();
        var runtime = Native.gua_runtime_create();
        Assert.That(runtime, Is.Not.EqualTo(nint.Zero));
        try
        {
            Native.gua_runtime_set_virtual_clock_enabled(runtime, 1);
            Native.gua_runtime_begin_frame(runtime, "fixture"); Native.gua_runtime_end_frame(runtime);
            Assert.That(Native.gua_runtime_clock_install(runtime, 0, 10), Is.EqualTo(1));
            Assert.That(Native.gua_runtime_clock_advance(runtime, 10), Is.EqualTo(1));
            Assert.That(Native.gua_runtime_start_inspector_bridge(runtime, port), Is.EqualTo(1));
            using var remote = new GuaRemoteContext($"ws://127.0.0.1:{port}", TimeSpan.FromSeconds(2));
            remote.WaitUntilAvailable(TimeSpan.FromSeconds(2));

            var pause = Task.Run(remote.PauseClock);
            await Task.Delay(30);
            Assert.That(pause.IsCompleted, Is.False);
            var step = new NativeClockStep { StructSize = (uint)Marshal.SizeOf<NativeClockStep>() };
            Assert.That(Native.gua_runtime_clock_consume_step(runtime, ref step), Is.EqualTo(1));
            await Task.Delay(30);
            Assert.That(pause.IsCompleted, Is.False, "Pause must wait for host frame publication, not only core step consumption.");
            Native.gua_runtime_begin_frame(runtime, "fixture"); Native.gua_runtime_end_frame(runtime);
            Assert.That(await pause.WaitAsync(TimeSpan.FromSeconds(2)), Is.EqualTo(GuaClockResult.Ok));

            Assert.That(Native.gua_runtime_clock_resume(runtime), Is.EqualTo(1));
            Assert.That(Native.gua_runtime_clock_advance(runtime, 10), Is.EqualTo(1));
            step = new NativeClockStep { StructSize = (uint)Marshal.SizeOf<NativeClockStep>() };
            Assert.That(Native.gua_runtime_clock_consume_step(runtime, ref step), Is.EqualTo(1));

            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            var stopwatch = Stopwatch.StartNew();
            var latePause = GuaClockControls.PauseClockAsync(remote, cancellationToken: cancellation.Token);
            Assert.That(async () => await latePause, Throws.InstanceOf<OperationCanceledException>());
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1)),
                "Asynchronous pause must observe cancellation while the consumed running step awaits its host frame.");
            Native.gua_runtime_begin_frame(runtime, "fixture"); Native.gua_runtime_end_frame(runtime);
            await Task.Delay(30);
        }
        finally
        {
            Native.gua_runtime_stop_inspector_bridge(runtime);
            Native.gua_runtime_destroy(runtime);
        }
    }

    [Test]
    public async Task SharedRemotePauseUsesTheBridgeResponseDeadline()
    {
        var port = ReservePort();
        var runtime = Native.gua_runtime_create();
        Assert.That(runtime, Is.Not.EqualTo(nint.Zero));
        try
        {
            Native.gua_runtime_set_virtual_clock_enabled(runtime, 1);
            Native.gua_runtime_begin_frame(runtime, "fixture"); Native.gua_runtime_end_frame(runtime);
            Assert.That(Native.gua_runtime_clock_install(runtime, 0, 10), Is.EqualTo(1));
            Assert.That(Native.gua_runtime_clock_advance(runtime, 10), Is.EqualTo(1));
            Assert.That(Native.gua_runtime_start_inspector_bridge(runtime, port), Is.EqualTo(1));
            using var remote = new GuaWebSocketContext($"ws://127.0.0.1:{port}", TimeSpan.FromMilliseconds(500));
            remote.WaitUntilAvailable(TimeSpan.FromSeconds(2));

            var pause = Task.Run(remote.PauseClock);
            await Task.Delay(600);
            Assert.That(pause.IsCompleted, Is.False,
                "Pause must use the bridge's response deadline instead of the shorter general request timeout.");

            var step = new NativeClockStep { StructSize = (uint)Marshal.SizeOf<NativeClockStep>() };
            Assert.That(Native.gua_runtime_clock_consume_step(runtime, ref step), Is.EqualTo(1));
            Native.gua_runtime_begin_frame(runtime, "fixture"); Native.gua_runtime_end_frame(runtime);
            Assert.That(await pause.WaitAsync(TimeSpan.FromSeconds(2)), Is.EqualTo(GuaClockResult.Ok));
        }
        finally
        {
            Native.gua_runtime_stop_inspector_bridge(runtime);
            Native.gua_runtime_destroy(runtime);
        }
    }

    [Test]
    public async Task StoppingBridgeCancelsAnInFlightRemotePauseWait()
    {
        var port = ReservePort();
        var runtime = Native.gua_runtime_create();
        Assert.That(runtime, Is.Not.EqualTo(nint.Zero));
        try
        {
            Native.gua_runtime_set_virtual_clock_enabled(runtime, 1);
            Native.gua_runtime_begin_frame(runtime, "fixture"); Native.gua_runtime_end_frame(runtime);
            Assert.That(Native.gua_runtime_clock_install(runtime, 0, 10), Is.EqualTo(1));
            Assert.That(Native.gua_runtime_clock_advance(runtime, 10), Is.EqualTo(1));
            Assert.That(Native.gua_runtime_start_inspector_bridge(runtime, port), Is.EqualTo(1));
            using var remote = new GuaRemoteContext($"ws://127.0.0.1:{port}", TimeSpan.FromSeconds(12));
            remote.WaitUntilAvailable(TimeSpan.FromSeconds(2));

            var pause = Task.Run(() =>
            {
                try { remote.PauseClock(); }
                catch (Exception) { }
            });
            await Task.Delay(50);
            Assert.That(pause.IsCompleted, Is.False, "The fixture must enter the in-flight pause wait before shutdown.");

            var stopwatch = Stopwatch.StartNew();
            Native.gua_runtime_stop_inspector_bridge(runtime);
            stopwatch.Stop();
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)),
                "Bridge shutdown must cancel the pause handler instead of waiting for its 10-second deadline.");
            await pause.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            Native.gua_runtime_stop_inspector_bridge(runtime);
            Native.gua_runtime_destroy(runtime);
        }
    }

    [Test]
    public async Task RemoteContextsSerializeConcurrentAsyncWebSocketRequests()
    {
        var port = ReservePort();
        using var runtime = new GuaRuntime();
        runtime.EnableVirtualClockAdapter();
        runtime.Clock.Install(step: TimeSpan.FromMilliseconds(10));
        runtime.Clock.Pause();
        runtime.BeginFrame("fixture"); runtime.EndFrame();
        Assert.That(runtime.StartInspectorBridge(port), Is.True);
        try
        {
            using var godotRemote = new GuaRemoteContext($"ws://127.0.0.1:{port}", TimeSpan.FromSeconds(2));
            godotRemote.WaitUntilAvailable(TimeSpan.FromSeconds(2));
            var godotRequests = Enumerable.Range(0, 8).Select(_ => godotRemote.PauseClockAsync()).ToArray();
            Assert.That(await Task.WhenAll(godotRequests).WaitAsync(TimeSpan.FromSeconds(2)),
                Is.All.EqualTo(GuaClockResult.Ok));

            using var sharedRemote = new GuaWebSocketContext($"ws://127.0.0.1:{port}", TimeSpan.FromSeconds(2));
            sharedRemote.WaitUntilAvailable(TimeSpan.FromSeconds(2));
            var sharedRequests = Enumerable.Range(0, 8).Select(_ => sharedRemote.PauseClockAsync()).ToArray();
            Assert.That(await Task.WhenAll(sharedRequests).WaitAsync(TimeSpan.FromSeconds(2)),
                Is.All.EqualTo(GuaClockResult.Ok));
        }
        finally
        {
            runtime.StopInspectorBridge();
        }
    }

    [Test]
    public async Task RemoteClockRejectsMissingDurationAndNonnumericFields()
    {
        var port = ReservePort();
        using var runtime = new GuaRuntime();
        runtime.EnableVirtualClockAdapter();
        runtime.Clock.Install(step: TimeSpan.FromMilliseconds(10));
        runtime.Clock.Pause();
        runtime.BeginFrame("fixture");
        runtime.EndFrame();
        Assert.That(runtime.StartInspectorBridge(port), Is.True);
        try
        {
            using var socket = new ClientWebSocket();
            await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}"), CancellationToken.None);
            var missing = await SendRawCommandAsync(socket, "{\"id\":1,\"type\":\"clock_run_for\"}");
            var nonnumeric = await SendRawCommandAsync(socket,
                "{\"id\":2,\"type\":\"clock_run_for\",\"durationMs\":\"10\"}");
            var invalidInitialTime = await SendRawCommandAsync(socket,
                "{\"id\":3,\"type\":\"clock_install\",\"initialTimeMs\":\"0\"}");
            var outOfRange = await SendRawCommandAsync(socket,
                "{\"id\":4,\"type\":\"clock_run_for\",\"durationMs\":1e999}");
            var leadingPlus = await SendRawCommandAsync(socket,
                "{\"id\":5,\"type\":\"clock_run_for\",\"durationMs\":+25}", 5);
            var missingInteger = await SendRawCommandAsync(socket,
                "{\"id\":6,\"type\":\"clock_run_for\",\"durationMs\":.5}", 6);
            var invalidSuffix = await SendRawCommandAsync(socket,
                "{\"id\":7,\"type\":\"clock_run_for\",\"durationMs\":25x}", 7);
            var invalidSuffixAfterWhitespace = await SendRawCommandAsync(socket,
                "{\"id\":8,\"type\":\"clock_run_for\",\"durationMs\":25 x}", 8);
            var nestedDuration = await SendRawCommandAsync(socket,
                "{\"id\":9,\"type\":\"clock_run_for\",\"meta\":{\"durationMs\":25}}");
            var statusAfterError = await SendRawCommandAsync(socket, "{\"id\":10,\"type\":\"get_clock\"}");
            Assert.Multiple(() =>
            {
                Assert.That(missing.GetProperty("ok").GetBoolean(), Is.False);
                Assert.That(missing.GetProperty("error").GetString(), Is.EqualTo("invalid_duration"));
                Assert.That(nonnumeric.GetProperty("ok").GetBoolean(), Is.False);
                Assert.That(nonnumeric.GetProperty("error").GetString(), Is.EqualTo("invalid_duration"));
                Assert.That(invalidInitialTime.GetProperty("ok").GetBoolean(), Is.False);
                Assert.That(invalidInitialTime.GetProperty("error").GetString(), Is.EqualTo("invalid_duration"));
                Assert.That(outOfRange.GetProperty("ok").GetBoolean(), Is.False);
                Assert.That(outOfRange.GetProperty("error").GetString(), Is.EqualTo("invalid_duration"));
                Assert.That(leadingPlus.GetProperty("error").GetString(), Is.EqualTo("invalid_duration"));
                Assert.That(missingInteger.GetProperty("error").GetString(), Is.EqualTo("invalid_duration"));
                Assert.That(invalidSuffix.GetProperty("error").GetString(), Is.EqualTo("invalid_duration"));
                Assert.That(invalidSuffixAfterWhitespace.GetProperty("error").GetString(), Is.EqualTo("invalid_duration"));
                Assert.That(nestedDuration.GetProperty("error").GetString(), Is.EqualTo("invalid_duration"));
                Assert.That(statusAfterError.GetProperty("ok").GetBoolean(), Is.True,
                    "An out-of-range number must not close the WebSocket connection.");
                Assert.That(statusAfterError.GetProperty("result").GetProperty("nowMs").GetDouble(), Is.Zero,
                    "Invalid JSON number spellings must not mutate the clock timeline.");
            });
        }
        finally
        {
            runtime.StopInspectorBridge();
        }
    }

    [Test]
    public void RemoteResetRejectsStaleEpochAndKeepsTheConnection()
    {
        var port = ReservePort();
        var runtime = Native.gua_runtime_create();
        Assert.That(runtime, Is.Not.EqualTo(nint.Zero));
        try
        {
            Native.gua_runtime_begin_frame(runtime, "fixture");
            Native.gua_runtime_register_node(runtime, "save", "button", "Save", new GuaBounds(0, 0, 1, 1), 1, 1);
            Native.gua_runtime_end_frame(runtime);
            Assert.That(Native.gua_runtime_start_inspector_bridge(runtime, port), Is.EqualTo(1));
            using var remote = new GuaRemoteContext($"ws://127.0.0.1:{port}", TimeSpan.FromSeconds(2));
            remote.WaitUntilAvailable(TimeSpan.FromSeconds(2));

            var first = remote.Reset(new GuaResetOptions(ExpectedSessionEpoch: 1));
            var stale = remote.Reset(new GuaResetOptions(ExpectedSessionEpoch: 1));
            Assert.Multiple(() =>
            {
                Assert.That(first.Result, Is.EqualTo(GuaResetResult.Succeeded));
                Assert.That(first.SessionEpoch, Is.EqualTo(2));
                Assert.That(stale.Result, Is.EqualTo(GuaResetResult.StaleEpoch));
                Assert.That(remote.GetContextStatus().SessionEpoch, Is.EqualTo(2));
            });
        }
        finally
        {
            Native.gua_runtime_stop_inspector_bridge(runtime);
            Native.gua_runtime_destroy(runtime);
        }
    }

    [Test]
    public void OnDemandScreenshotTimeoutIsTypedAndDoesNotReturnTheStaleLatestImage()
    {
        var port = ReservePort();
        var runtime = Native.gua_runtime_create();
        Assert.That(runtime, Is.Not.EqualTo(nint.Zero));
        try
        {
            Native.gua_runtime_begin_frame(runtime, "fixture");
            Native.gua_runtime_end_frame(runtime);
            Assert.That(Native.gua_runtime_start_inspector_bridge(runtime, port), Is.EqualTo(1));
            using var remote = new GuaRemoteContext($"ws://127.0.0.1:{port}", TimeSpan.FromSeconds(2));
            remote.WaitUntilAvailable(TimeSpan.FromSeconds(2));
            var error = Assert.Throws<GuaScreenshotException>(() =>
                remote.CaptureScreenshot(TimeSpan.FromMilliseconds(30), afterFrameSequence: 1));
            Assert.That(error!.Error, Is.EqualTo(GuaScreenshotError.Timeout));

            using var sharedRemote = new GuaWebSocketContext($"ws://127.0.0.1:{port}", TimeSpan.FromMilliseconds(20));
            sharedRemote.WaitUntilAvailable(TimeSpan.FromSeconds(2));
            var stopwatch = Stopwatch.StartNew();
            var sharedError = Assert.Throws<GuaRemoteScreenshotException>(() =>
                sharedRemote.CaptureScreenshot(TimeSpan.FromMilliseconds(100), afterFrameSequence: 1));
            stopwatch.Stop();
            Assert.That(sharedError!.Error, Is.EqualTo(GuaRemoteScreenshotError.Timeout));
            Assert.That(stopwatch.Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(70)),
                "CaptureScreenshot must honor its capture timeout instead of the shorter default request timeout.");
        }
        finally
        {
            Native.gua_runtime_stop_inspector_bridge(runtime);
            Native.gua_runtime_destroy(runtime);
        }
    }

    [Test]
    public async Task CompletingCanceledRuntimeScreenshotIsBenign()
    {
        var port = ReservePort();
        using var runtime = new GuaRuntime();
        runtime.BeginFrame("fixture");
        runtime.EndFrame();
        Assert.That(runtime.StartInspectorBridge(port), Is.True);
        try
        {
            using var remote = new GuaRemoteContext($"ws://127.0.0.1:{port}", TimeSpan.FromSeconds(2));
            remote.WaitUntilAvailable(TimeSpan.FromSeconds(2));
            var capture = Task.Run(() => remote.CaptureScreenshot(TimeSpan.FromSeconds(1), afterFrameSequence: 0));

            GuaScreenshotRequest request = default;
            Assert.That(SpinWait.SpinUntil(() => runtime.TryConsumeScreenshotRequest(out request), TimeSpan.FromSeconds(2)), Is.True);
            var error = Assert.ThrowsAsync<GuaScreenshotException>(async () => await capture);
            Assert.That(error!.Error, Is.EqualTo(GuaScreenshotError.Timeout));
            Assert.Multiple(() =>
            {
                Assert.That(runtime.TryCompleteScreenshot(request, GuaScreenshotAvailability.Available,
                    "data:image/png;base64,aGVsbG8=", 1, 1), Is.False);
                Assert.DoesNotThrow(() => runtime.CompleteScreenshot(request, GuaScreenshotAvailability.RenderingDisabled));
            });
        }
        finally
        {
            runtime.StopInspectorBridge();
        }
    }

    private static int ReservePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Test]
    public async Task RemoteResetDistinguishesLegacyAggregateMasksFromCurrentExplicitMasks()
    {
        var port = ReservePort();
        using var runtime = new GuaRuntime();
        runtime.EnableVirtualClockAdapter();
        runtime.Clock.Install(step: TimeSpan.FromMilliseconds(250));
        runtime.BeginFrame("fixture"); runtime.EndFrame();
        Assert.That(runtime.StartInspectorBridge(port), Is.True);
        try
        {
            using var socket = new ClientWebSocket();
            await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}"), CancellationToken.None);

            var legacyDefault = await SendRawCommandAsync(socket,
                "{\"id\":1,\"type\":\"reset_context\",\"expectedSessionEpoch\":1,\"flags\":15}");
            var afterLegacyDefault = await SendRawCommandAsync(socket, "{\"id\":2,\"type\":\"get_clock\"}");
            Assert.That(legacyDefault.GetProperty("ok").GetBoolean(), Is.True);
            Assert.That(afterLegacyDefault.GetProperty("result").GetProperty("installed").GetBoolean(), Is.False);

            runtime.Clock.Install(step: TimeSpan.FromMilliseconds(250));
            var currentExplicit = await SendRawCommandAsync(socket,
                "{\"id\":3,\"type\":\"reset_context\",\"expectedSessionEpoch\":2,\"flags\":15,\"flagsVersion\":1}");
            var afterCurrentExplicit = await SendRawCommandAsync(socket, "{\"id\":4,\"type\":\"get_clock\"}");
            Assert.That(currentExplicit.GetProperty("ok").GetBoolean(), Is.True);
            Assert.That(afterCurrentExplicit.GetProperty("result").GetProperty("installed").GetBoolean(), Is.True);

            var fractionalVersion = await SendRawCommandAsync(socket,
                "{\"id\":5,\"type\":\"reset_context\",\"expectedSessionEpoch\":3,\"flags\":15,\"flagsVersion\":1.5}");
            var afterFractionalVersion = await SendRawCommandAsync(socket, "{\"id\":6,\"type\":\"get_clock\"}");
            Assert.That(fractionalVersion.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(afterFractionalVersion.GetProperty("result").GetProperty("installed").GetBoolean(), Is.True,
                "A structurally invalid flagsVersion must not partially reset the session.");

            var legacyAll = await SendRawCommandAsync(socket,
                "{\"id\":7,\"type\":\"reset_context\",\"expectedSessionEpoch\":3,\"flags\":63}");
            var afterLegacyAll = await SendRawCommandAsync(socket, "{\"id\":8,\"type\":\"get_clock\"}");
            Assert.That(legacyAll.GetProperty("ok").GetBoolean(), Is.True);
            Assert.That(afterLegacyAll.GetProperty("result").GetProperty("installed").GetBoolean(), Is.False);
        }
        finally
        {
            runtime.StopInspectorBridge();
        }
    }

    [Test]
    public async Task AutomaticClockExecutionLimitDoesNotStopHostFrames()
    {
        var port = ReservePort();
        using var runtime = new GuaRuntime();
        runtime.EnableVirtualClockAdapter();
        runtime.BeginFrame("before-limit"); runtime.EndFrame();
        Assert.That(runtime.StartInspectorBridge(port), Is.True);
        try
        {
            using var socket = new ClientWebSocket();
            await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}"), CancellationToken.None);
            var install = await SendRawCommandAsync(socket,
                "{\"id\":1,\"type\":\"clock_install\",\"initialTimeMs\":0,\"stepMs\":1e-9}");
            Assert.That(install.GetProperty("ok").GetBoolean(), Is.True);

            var failures = new List<Exception>();
            runtime.Clock.CallbackFailed += failures.Add;
            Assert.That(() => runtime.Clock.Advance(TimeSpan.FromMilliseconds(16)), Throws.Nothing);
            Assert.That(() =>
            {
                runtime.BeginFrame("after-limit");
                runtime.EndFrame();
            }, Throws.Nothing);
            Assert.That(runtime.GetUiTreeJson(), Does.Contain("after-limit"));
            Assert.That(runtime.Clock.Status.NowMilliseconds, Is.Zero);
            Assert.That(failures, Has.Count.EqualTo(1));
            Assert.That(failures[0].Message, Does.Contain("execution_limit"));

            Assert.That(() => runtime.Clock.Advance(TimeSpan.FromMilliseconds(16)), Throws.Nothing);
            Assert.That(failures, Has.Count.EqualTo(1),
                "The same invalid running-clock configuration should be diagnosed once without flooding every host frame.");
        }
        finally
        {
            runtime.StopInspectorBridge();
        }
    }

    [Test]
    public async Task RuntimeClockRejectsSchedulesThatCannotAdvanceRepresentableDeadlines()
    {
        var port = ReservePort();
        using var runtime = new GuaRuntime();
        runtime.EnableVirtualClockAdapter();
        var callbacks = 0;
        var failures = new List<Exception>();
        runtime.Clock.CallbackFailed += failures.Add;
        runtime.Clock.Schedule(TimeSpan.Zero, () => callbacks++, TimeSpan.FromMilliseconds(1));
        Assert.That(runtime.StartInspectorBridge(port), Is.True);
        try
        {
            using var socket = new ClientWebSocket();
            await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}"), CancellationToken.None);
            var install = await SendRawCommandAsync(socket,
                "{\"id\":1,\"type\":\"clock_install\",\"initialTimeMs\":1e16,\"stepMs\":2}");
            Assert.That(install.GetProperty("ok").GetBoolean(), Is.True);

            var intervalError = Assert.Throws<ArgumentOutOfRangeException>(() =>
                runtime.Clock.Schedule(TimeSpan.Zero, () => callbacks++, TimeSpan.FromMilliseconds(1)));
            Assert.That(intervalError!.ParamName, Is.EqualTo("interval"));
            var delayError = Assert.Throws<ArgumentOutOfRangeException>(() =>
                runtime.Clock.Schedule(TimeSpan.FromMilliseconds(1), () => callbacks++));
            Assert.That(delayError!.ParamName, Is.EqualTo("delay"));

            using var valid = runtime.Clock.Schedule(TimeSpan.Zero, () => callbacks++, TimeSpan.FromMilliseconds(2));
            valid.Dispose();
            Assert.That(() => runtime.Clock.Advance(TimeSpan.FromMilliseconds(2)), Throws.Nothing);
            Assert.Multiple(() =>
            {
                Assert.That(callbacks, Is.Zero);
                Assert.That(ScheduledCount(runtime.Clock), Is.Zero,
                    "A pre-install interval that cannot advance the installed timeline must be retired.");
                Assert.That(failures, Has.Count.EqualTo(1));
                Assert.That(failures[0], Is.TypeOf<ArgumentOutOfRangeException>());
            });
        }
        finally
        {
            runtime.StopInspectorBridge();
        }
    }

    private static int ScheduledCount(object clock)
    {
        var field = clock.GetType().GetField("scheduled", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Clock scheduler storage was not found.");
        return ((System.Collections.ICollection)(field.GetValue(clock)
            ?? throw new InvalidOperationException("Clock scheduler storage was null."))).Count;
    }

    private static async Task<JsonElement> SendRawCommandAsync(ClientWebSocket socket, string json, int? expectedResponseId = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var expectedId = expectedResponseId;
        if (!expectedId.HasValue)
        {
            using var requestDocument = JsonDocument.Parse(json);
            expectedId = requestDocument.RootElement.GetProperty("id").GetInt32();
        }
        var request = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(new ArraySegment<byte>(request), WebSocketMessageType.Text, true, timeout.Token);
        var buffer = new byte[4096];
        while (true)
        {
            using var response = new MemoryStream();
            WebSocketReceiveResult received;
            do
            {
                received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
                response.Write(buffer, 0, received.Count);
            } while (!received.EndOfMessage);
            using var document = JsonDocument.Parse(response.ToArray());
            if (document.RootElement.TryGetProperty("id", out var id) && id.GetInt32() == expectedId.Value)
                return document.RootElement.Clone();
        }
    }

    private static class Native
    {
        [DllImport("gua_runtime", CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint gua_runtime_create();
        [DllImport("gua_runtime", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void gua_runtime_destroy(nint runtime);
        [DllImport("gua_runtime", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void gua_runtime_begin_frame(nint runtime, [MarshalAs(UnmanagedType.LPUTF8Str)] string screen);
        [DllImport("gua_runtime", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void gua_runtime_end_frame(nint runtime);
        [DllImport("gua_runtime", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void gua_runtime_register_node(nint runtime, [MarshalAs(UnmanagedType.LPUTF8Str)] string id,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string role, [MarshalAs(UnmanagedType.LPUTF8Str)] string label,
            GuaBounds bounds, int visible, int enabled);
        [DllImport("gua_runtime", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int gua_runtime_consume_click_request(nint runtime, [MarshalAs(UnmanagedType.LPUTF8Str)] string nodeId);
        [DllImport("gua_runtime", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int gua_runtime_emit_click(nint runtime, [MarshalAs(UnmanagedType.LPUTF8Str)] string nodeId);
        [DllImport("gua_runtime", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int gua_runtime_start_inspector_bridge(nint runtime, int port);
        [DllImport("gua_runtime", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void gua_runtime_stop_inspector_bridge(nint runtime);
        [DllImport("gua_runtime", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void gua_runtime_set_virtual_clock_enabled(nint runtime, int enabled);
        [DllImport("gua_runtime", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int gua_runtime_clock_install(nint runtime, double initialTimeMs, double stepMs);
        [DllImport("gua_runtime", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int gua_runtime_clock_advance(nint runtime, double durationMs);
        [DllImport("gua_runtime", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int gua_runtime_clock_resume(nint runtime);
        [DllImport("gua_runtime", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int gua_runtime_clock_consume_step(nint runtime, ref NativeClockStep step);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeClockStep
    {
        public uint StructSize;
        public double DeltaMs;
        public int FinalStep;
        public ulong Generation;
    }
}
