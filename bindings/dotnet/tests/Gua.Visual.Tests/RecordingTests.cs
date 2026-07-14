using System.Text.Json;
using Gua.Core;
using Gua.Testing.Recording;
using NUnit.Framework;

namespace Gua.Visual.Tests;

public sealed class RecordingTests
{
    private string _root = null!;
    [SetUp] public void SetUp() => _root = Path.Combine(Path.GetTempPath(), "gua-recording-tests", Guid.NewGuid().ToString("N"));
    [TearDown] public void TearDown() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Test]
    public void ValidationRejectsAmbiguousTargetsTimingAndSecretLeaks()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidDataException>(() => GuaRecordingFile.Validate(new(2, [])));
            Assert.Throws<InvalidDataException>(() => GuaRecordingFile.Validate(new(1,
                [new(GuaRecordedAction.click, 0, 1, 2, false, Target: new(Id: "a", Role: "button"))])));
            Assert.Throws<InvalidDataException>(() => GuaRecordingFile.Validate(new(1,
                [new(GuaRecordedAction.click, 2, 1, 2, false, Target: new(Id: "a")),
                 new(GuaRecordedAction.click, 1, 2, 3, false, Target: new(Id: "b"))])));
            Assert.Throws<InvalidDataException>(() => GuaRecordingFile.Validate(new(1,
                [new(GuaRecordedAction.click, 0, 3, 2, false, Target: new(Id: "a"))])));
            Assert.Throws<InvalidDataException>(() => GuaRecordingFile.Validate(new(1,
                [new(GuaRecordedAction.set_value, 0, 1, 2, true, Target: new(Id: "password"),
                    Value: "secret-marker", SecretKey: "login-password")])));
        });
    }

    [Test]
    public void SaveLoadNeverSerializesSensitiveValue()
    {
        var recording = new GuaRecording(1,
            [new(GuaRecordedAction.set_value, 0, 1, 2, true, 7, 7,
                new(Id: "password"), SecretKey: "login-password")]);
        var path = Path.Combine(_root, "recording.json");
        GuaRecordingFile.Save(path, recording);
        var json = File.ReadAllText(path);
        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("login-password"));
            Assert.That(json, Does.Not.Contain("secret-marker"));
            Assert.That(json, Does.Not.Contain("\"role\": null"));
            Assert.That(GuaRecordingFile.Load(path).SchemaVersion, Is.EqualTo(1));
        });
    }

    [Test]
    public void DiagnosticsImportPairsCompletionAndPreservesTimingRevisionAndArguments()
    {
        const string json = """
        {
          "revision": 9,
          "operations": [
            {"sequence":1,"elapsedMilliseconds":120,"revision":4,"phase":"enqueued","requestId":4,"action":"scroll","nodeId":"list","value":"","sensitive":false,"deltaX":2.5,"deltaY":40,"scrollUnit":1},
            {"sequence":2,"elapsedMilliseconds":121,"revision":4,"phase":"consumed","requestId":4,"action":"scroll","nodeId":"list","value":"","sensitive":false}
          ],
          "events": [
            {"sequence":3,"elapsedMilliseconds":175,"revision":6,"phase":"observed","requestId":4,"action":"scroll","nodeId":"list","value":"","sensitive":false},
            {"sequence":4,"elapsedMilliseconds":200,"revision":7,"phase":"observed","requestId":0,"action":"focus","nodeId":"name","value":"","sensitive":false}
          ]
        }
        """;
        var imported = GuaRecordingFile.ImportDiagnostics(json);
        Assert.Multiple(() =>
        {
            Assert.That(imported.UsedLegacySyntheticTiming, Is.False);
            Assert.That(imported.PairedRequestCount, Is.EqualTo(1));
            Assert.That(imported.UnpairedStepCount, Is.EqualTo(1));
            Assert.That(imported.Recording.Steps, Has.Count.EqualTo(2));
            Assert.That(imported.Recording.Steps[0].RelativeMilliseconds, Is.Zero);
            Assert.That(imported.Recording.Steps[0].PreRevision, Is.EqualTo(4));
            Assert.That(imported.Recording.Steps[0].PostRevision, Is.EqualTo(6));
            Assert.That(imported.Recording.Steps[0].DeltaX, Is.EqualTo(2.5f));
            Assert.That(imported.Recording.Steps[0].DeltaY, Is.EqualTo(40));
            Assert.That(imported.Recording.Steps[0].ScrollUnit, Is.EqualTo(1));
            Assert.That(imported.Recording.Steps[1].RelativeMilliseconds, Is.EqualTo(80));
        });
    }

    [Test]
    public async Task RecorderCapturesCorrelatedCompletionRevisionsAndSensitivePlaceholder()
    {
        var context = new FakeContext();
        var recorder = new GuaRecorder(context);
        var completion = await recorder.SetValueAsync(new(Id: "password"), "secret-marker",
            sensitive: true, secretKey: "login-password", waitCondition: GuaWaitConditions.Visible("password"));
        await recorder.PressKeyAsync("Enter", modifiers: 4);
        var recording = recorder.Recording;
        var json = JsonSerializer.Serialize(recording);
        Assert.Multiple(() =>
        {
            Assert.That(completion.RequestId, Is.EqualTo(recording.Steps[0].RequestId));
            Assert.That(recording.Steps[0].PreRevision, Is.EqualTo(1));
            Assert.That(recording.Steps[0].PostRevision, Is.EqualTo(2));
            Assert.That(recording.Steps[1].Target!.CurrentFocus, Is.True);
            Assert.That(recording.Steps[1].Modifiers, Is.EqualTo(4));
            Assert.That(json, Does.Not.Contain("secret-marker"));
            Assert.That(json, Does.Contain("login-password"));
        });
    }

    [Test]
    public async Task ReplayWaitsForEachCompletionAndPreservesAllActionArguments()
    {
        var context = new FakeContext();
        var recording = new GuaRecording(1,
        [
            new(GuaRecordedAction.set_value, 0, 1, 2, true, Target: new(Id: "password"), SecretKey: "password"),
            new(GuaRecordedAction.scroll, 0, 2, 3, false, Target: new(Id: "list"), DeltaX: 3, DeltaY: 25, ScrollUnit: 1),
            new(GuaRecordedAction.press_key, 0, 3, 4, false, Target: new(CurrentFocus: true), Value: "Enter", Modifiers: 5),
        ]);
        var result = await GuaReplayer.ReplayAsync(context, recording, new()
        {
            SecretResolver = key => key == "password" ? "secret-marker" : null,
        });
        Assert.Multiple(() =>
        {
            Assert.That(result.Steps, Has.Count.EqualTo(3));
            Assert.That(result.Steps, Has.All.Property(nameof(GuaReplayStepResult.Completion)).Not.Null);
            Assert.That(context.Requests[0].Sensitive, Is.True);
            Assert.That(context.Requests[1].DeltaX, Is.EqualTo(3));
            Assert.That(context.Requests[1].DeltaY, Is.EqualTo(25));
            Assert.That(context.Requests[1].ScrollUnit, Is.EqualTo(1));
            Assert.That(context.Requests[2].NodeId, Is.Null);
            Assert.That(context.Requests[2].Key, Is.EqualTo("Enter"));
            Assert.That(context.Requests[2].Modifiers, Is.EqualTo(5));
        });
    }

    [Test]
    public void ReplayRefusesCoordinateFallbackByDefault()
    {
        var recording = new GuaRecording(1,
            [new(GuaRecordedAction.click, 0, 1, 2, false, CoordinateFallback: new(3, 4))]);
        Assert.ThrowsAsync<InvalidOperationException>(() => GuaReplayer.ReplayAsync(new FakeContext(), recording));
    }

    private sealed class FakeContext : IGuaContext
    {
        private readonly Dictionary<ulong, GuaActionEvent> _events = [];
        private ulong _nextRequestId = 1;
        private ulong _revision = 1;
        public List<GuaActionRequest> Requests { get; } = [];

        public string GetUiTreeJson() => JsonSerializer.Serialize(new
        {
            schemaVersion = 2,
            screen = "test",
            frameSequence = _revision,
            revision = _revision,
            sessionEpoch = 1,
            nodes = new object[]
            {
                Node("password", "textbox", "Password", new[] { "set_value" }),
                Node("list", "list", "Items", new[] { "scroll" }),
            },
        });
        public string GetDiagnosticsJson() => "{}";
        public GuaQueryResult Query(GuaSelector selector)
        {
            var nodes = new[]
            {
                new GuaNodeQueryMatch("password", "textbox", "Password", null),
                new GuaNodeQueryMatch("list", "list", "Items", null),
            };
            return new(true, nodes.Where(node =>
                (selector.Id is null || selector.Id == node.Id) &&
                (selector.Role is null || selector.Role == node.Role) &&
                (selector.Name is null || selector.Name == node.Label)).ToArray());
        }
        public GuaActionError EnqueueAction(GuaActionRequest request, out ulong requestId)
        {
            requestId = _nextRequestId++;
            Requests.Add(request);
            _revision++;
            _events[requestId] = new(requestId, request.Action, true, GuaActionError.None,
                request.NodeId ?? "", request.Sensitive ? "" : request.Value ?? request.Key ?? "",
                request.Sensitive, 1, _revision, _revision);
            return GuaActionError.None;
        }
        public bool TryPollActionEvent(ulong requestId, out GuaActionEvent e) => _events.Remove(requestId, out e);
        public bool TryPollActionEvent(out GuaActionEvent e)
        {
            if (_events.Count == 0) { e = default; return false; }
            var first = _events.First(); _events.Remove(first.Key); e = first.Value; return true;
        }
        public GuaNodeState GetNodeState(string id) => new(true, true);
        public string FindNodeById(string id) => id;
        public string FindNodeByRole(string role, string? name = null) => role == "list" ? "list" : "password";
        public string FindNodeByText(string text) => "password";
        public bool EnqueueClick(string id) => true;
        public bool TryPollEvent(out GuaEvent e) { e = default; return false; }
        private static object Node(string id, string role, string label, string[] actions) => new
        {
            id, role, label, bounds = new { x = 0, y = 0, w = 1, h = 1 }, visible = true,
            enabled = true, actions,
        };
    }
}
