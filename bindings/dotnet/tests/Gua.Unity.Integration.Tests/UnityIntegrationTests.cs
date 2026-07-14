using System.Text.Json;
using System.Text;
using Gua.Core;
using Gua.Testing.Unity;
using NUnit.Framework;

namespace Gua.Unity.Integration.Tests;

[TestFixture]
public sealed class UnityIntegrationTests
{
    [Test]
    public void PrecompiledUpmArtifactPlayer_LoadsManagedAndNativeClosure()
    {
        var player = Environment.GetEnvironmentVariable("GUA_UNITY_ARTIFACT_PLAYER");
        if (string.IsNullOrWhiteSpace(player)) Assert.Ignore("Set GUA_UNITY_ARTIFACT_PLAYER to verify the precompiled UPM artifact.");
        using var host = UnitySceneTestHost.LoadRenderedPlayer(player!, new UnitySceneTestHostOptions { ConnectTimeout = TimeSpan.FromSeconds(30) });
        var version = host.RemoteContext.GetVersion();
        Assert.That(version.AdapterVersions, Contains.Key("unity"));
        Assert.That(WaitForFrame(host), Is.True, "The precompiled Unity adapter did not publish a frame.");
    }

    [Test]
    public void RenderedEditorPlayMode_ReflectsFixture()
    {
        var scene = Environment.GetEnvironmentVariable("GUA_UNITY_SCENE");
        if (string.IsNullOrWhiteSpace(scene)) Assert.Ignore("Set GUA_UNITY_SCENE to run the Unity Editor integration fixture.");
        using var host = UnitySceneTestHost.LoadEditor(scene!, new UnitySceneTestHostOptions
        {
            UnityExecutablePath = Environment.GetEnvironmentVariable("UNITY_EXECUTABLE"),
            ConnectTimeout = TimeSpan.FromSeconds(60),
            SceneTimeout = TimeSpan.FromSeconds(15),
        });
        Assert.That(WaitForText(host, "Start Game"), Is.True);
        Assert.That(WaitForText(host, "Gua Unity Sample"), Is.True);
        Assert.That(WaitForScreen(host, "title"), Is.True);
        Assert.That(host.RemoteContext.GetVersion().AdapterVersions, Contains.Key("unity"));
    }

    [Test]
    public void RenderedPlayer_ReflectsUiAndDispatchesButtonListener()
    {
        var player = Environment.GetEnvironmentVariable("GUA_UNITY_PLAYER");
        if (string.IsNullOrWhiteSpace(player)) Assert.Ignore("Set GUA_UNITY_PLAYER to run the Unity integration fixture.");

        using var host = UnitySceneTestHost.LoadRenderedPlayer(player!, new UnitySceneTestHostOptions
        {
            ConnectTimeout = TimeSpan.FromSeconds(30),
            SceneTimeout = TimeSpan.FromSeconds(15),
            EnvironmentVariables = new Dictionary<string, string> { ["GUA_UNITY_COVERAGE"] = "1" },
        });

        var version = host.RemoteContext.GetVersion();
        Assert.That(version.AdapterVersions, Contains.Key("unity"));
        Assert.That(WaitForText(host, "Start Game"), Is.True);
        Assert.That(WaitForText(host, "Gua Unity Sample"), Is.True);
        Assert.That(WaitForScreen(host, "title"), Is.True);
        Assert.That(host.RemoteContext.GetRemoteTree().Nodes.All(node => Encoding.UTF8.GetByteCount(node.Id) <= 127), Is.True,
            "Unity must keep node IDs round-trippable through the fixed-size C ABI action request.");

        Assert.That(host.Context.FindNodeByRole("button", "TMP Launch"), Is.EqualTo("tmp-button"));
        var listId = host.Context.FindNodeByRole("list", "fixture-list");
        Assert.That(listId, Is.Not.Null.And.Not.Empty);
        Assert.That(WaitForListItems(host, listId!, "One", "Two", "Three"), Is.True);

        var scrollError = host.Context.EnqueueAction(new GuaActionRequest(GuaActionType.Scroll, listId, DeltaY: 1, ScrollUnit: 1), out var scrollRequestId);
        Assert.That(scrollError, Is.EqualTo(GuaActionError.None));
        Assert.That(WaitForActionEvent(host, scrollRequestId, out var scrollResult), Is.True,
            () => "ListView scroll did not complete. " + host.RemoteContext.GetDiagnosticsJson());
        Assert.That(scrollResult.Succeeded, Is.True, $"ListView scroll failed: {scrollResult.Error}");

        var invalidError = host.Context.EnqueueAction(new GuaActionRequest(GuaActionType.SetValue, "sample-slider", "not-a-number"), out var invalidRequestId);
        Assert.That(invalidError, Is.EqualTo(GuaActionError.None));
        Assert.That(WaitForActionEvent(host, invalidRequestId, out var invalidResult), Is.True);
        Assert.That(invalidResult.Succeeded, Is.False);
        Assert.That(invalidResult.Error, Is.EqualTo(GuaActionError.InvalidValue));

        var keyError = host.Context.EnqueueAction(new GuaActionRequest(GuaActionType.PressKey, "sample-input", Key: "A", Modifiers: 1), out var keyRequestId);
        Assert.That(keyError, Is.EqualTo(GuaActionError.None));
        Assert.That(WaitForActionEvent(host, keyRequestId, out var keyResult), Is.True);
        Assert.That(keyResult.Succeeded, Is.True, $"uGUI press_key failed: {keyResult.Error}");
        Assert.That(keyResult.Value, Does.Contain("A"));
        Assert.That(WaitForValue(host, "sample-input", value => value.Contains("A", StringComparison.Ordinal)), Is.True);

        var tmpKeyError = host.Context.EnqueueAction(new GuaActionRequest(GuaActionType.PressKey, "tmp-input", Key: "Z", Modifiers: 1), out var tmpKeyRequestId);
        Assert.That(tmpKeyError, Is.EqualTo(GuaActionError.None));
        Assert.That(WaitForActionEvent(host, tmpKeyRequestId, out var tmpKeyResult), Is.True);
        Assert.That(tmpKeyResult.Succeeded, Is.True, $"TMP press_key failed: {tmpKeyResult.Error}");
        Assert.That(tmpKeyResult.Value, Does.Contain("Z"));

        Assert.That(WaitForBounds(host, "scaled-box", out var scaledBounds), Is.True);
        Assert.That(scaledBounds.X, Is.EqualTo(200).Within(1));
        Assert.That(scaledBounds.Y, Is.EqualTo(120).Within(1));
        Assert.That(scaledBounds.Width, Is.EqualTo(400).Within(1));
        Assert.That(scaledBounds.Height, Is.EqualTo(80).Within(1));

        var settings = host.Context.FindNodeByRole("button", "Settings");
        var settingsError = host.Context.EnqueueAction(new GuaActionRequest(GuaActionType.Click, settings), out var settingsRequestId);
        Assert.That(settingsError, Is.EqualTo(GuaActionError.None));
        Assert.That(WaitForAction(host, settingsRequestId), Is.True);
        Assert.That(WaitForScreen(host, "title"), Is.True, "Settings should intentionally leave the sample on the title screen.");

        var button = host.Context.FindNodeByRole("button", "Start Game");
        var error = host.Context.EnqueueAction(new GuaActionRequest(GuaActionType.Click, button), out var requestId);
        Assert.That(error, Is.EqualTo(GuaActionError.None));
        Assert.That(WaitForAction(host, requestId), Is.True, "Unity did not emit the request-correlated action completion event.");
        Assert.That(WaitForText(host, "Loading..."), Is.True, "The Start Game listener did not show the loading screen.");
        Assert.That(WaitForScreen(host, "loading"), Is.True);

        var screenshot = host.CaptureScreenshot(TimeSpan.FromSeconds(15));
        Assert.That(screenshot.DecodePng().Take(8), Is.EqualTo(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
        Assert.That(screenshot.Width, Is.GreaterThan(0));
        Assert.That(screenshot.Height, Is.GreaterThan(0));
    }

    private static bool WaitForAction(UnitySceneTestHost host, ulong requestId)
    {
        return WaitForActionEvent(host, requestId, out var action) && action.Succeeded;
    }

    private static bool WaitForActionEvent(UnitySceneTestHost host, ulong requestId, out GuaActionEvent result)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (host.Context.TryPollActionEvent(requestId, out result)) return true;
            Thread.Sleep(20);
        }
        result = default;
        return false;
    }

    private static bool WaitForListItems(UnitySceneTestHost host, string parentId, params string[] labels)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            using var tree = JsonDocument.Parse(host.Context.GetUiTreeJson());
            var items = tree.RootElement.GetProperty("nodes").EnumerateArray()
                .Where(node => node.GetProperty("role").GetString() == "listitem" &&
                    node.TryGetProperty("parentId", out var parent) && parent.GetString() == parentId)
                .Select(node => node.GetProperty("label").GetString()).ToArray();
            if (labels.All(label => items.Contains(label))) return true;
            Thread.Sleep(20);
        }
        return false;
    }

    private static bool WaitForValue(UnitySceneTestHost host, string id, Func<string, bool> predicate)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            using var tree = JsonDocument.Parse(host.Context.GetUiTreeJson());
            foreach (var node in tree.RootElement.GetProperty("nodes").EnumerateArray())
                if (node.GetProperty("id").GetString() == id && node.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String && predicate(value.GetString() ?? "")) return true;
            Thread.Sleep(20);
        }
        return false;
    }

    private static bool WaitForBounds(UnitySceneTestHost host, string idSuffix, out GuaBounds bounds)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            using var tree = JsonDocument.Parse(host.Context.GetUiTreeJson());
            foreach (var node in tree.RootElement.GetProperty("nodes").EnumerateArray())
            {
                if (!(node.GetProperty("id").GetString() ?? "").EndsWith(idSuffix, StringComparison.Ordinal)) continue;
                var value = node.GetProperty("bounds");
                bounds = new GuaBounds(value.GetProperty("x").GetSingle(), value.GetProperty("y").GetSingle(), value.GetProperty("w").GetSingle(), value.GetProperty("h").GetSingle());
                if (bounds.Width > 0 && bounds.Height > 0) return true;
            }
            Thread.Sleep(20);
        }
        bounds = default;
        return false;
    }

    private static bool WaitForFrame(UnitySceneTestHost host)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (host.Context.GetContextStatus().FrameSequence > 0) return true;
            Thread.Sleep(20);
        }
        return false;
    }

    private static bool WaitForText(UnitySceneTestHost host, string text)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            using var tree = JsonDocument.Parse(host.Context.GetUiTreeJson());
            if (tree.RootElement.GetProperty("nodes").EnumerateArray().Any(node =>
                node.TryGetProperty("text", out var value) && value.ValueKind == JsonValueKind.String && value.GetString() == text)) return true;
            Thread.Sleep(20);
        }
        return false;
    }

    private static bool WaitForScreen(UnitySceneTestHost host, string screen)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            using var tree = JsonDocument.Parse(host.Context.GetUiTreeJson());
            if (tree.RootElement.GetProperty("screen").GetString() == screen) return true;
            Thread.Sleep(20);
        }
        return false;
    }
}
