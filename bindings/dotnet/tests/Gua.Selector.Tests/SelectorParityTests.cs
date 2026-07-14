using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
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
                Assert.That(remoteVersion.Capabilities, Is.EqualTo(localVersion.Capabilities));
            });
            Assert.That(remoteVersion.Capabilities, Does.Contain("version_v1"));

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

    private static int ReservePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
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
    }
}
