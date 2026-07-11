using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using Gua.Core;
using Gua.Testing;
using Gua.Testing.Godot;
using NUnit.Framework;

namespace Gua.Selector.Tests;

[TestFixture]
public sealed class SelectorParityTests
{
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
        internal static extern int gua_runtime_start_inspector_bridge(nint runtime, int port);
        [DllImport("gua_runtime", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void gua_runtime_stop_inspector_bridge(nint runtime);
    }
}
