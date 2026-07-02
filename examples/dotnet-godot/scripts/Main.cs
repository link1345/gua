using Godot;
using Gua.Core;
using Gua.Godot;

public partial class Main : Control
{
    private readonly GuaGodotRuntime _gua = new();
    private Label? _titleLabel;
    private Button? _startButton;
    private Button? _settingsButton;
    private Label? _loadingLabel;
    private bool _loading;

    [Export]
    public bool StartInspectorBridgeOnReady { get; set; } = true;

    [Export]
    public int InspectorBridgePort { get; set; } = 8765;

    public override void _Ready()
    {
        _loading = false;
        BuildUi();
        RebuildGuaFrame();

        if (StartInspectorBridgeOnReady)
        {
            StartInspectorBridge();
        }

    }

    public override void _Process(double delta)
    {
        RebuildGuaFrame();
        PollGuaEvents();
    }

    public override void _ExitTree()
    {
        _gua.Dispose();
    }

    private void BuildUi()
    {
        _titleLabel = new Label
        {
            Text = "Gua C# Sample",
            Position = new Vector2(420, 220),
            Size = new Vector2(440, 56),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        AddChild(_titleLabel);

        _startButton = new Button
        {
            Text = "Start Game",
            Position = new Vector2(512, 312),
            Size = new Vector2(256, 56),
        };
        _startButton.Pressed += () => _gua.EnqueueClick("start");
        AddChild(_startButton);

        _settingsButton = new Button
        {
            Text = "Settings",
            Position = new Vector2(512, 384),
            Size = new Vector2(256, 56),
        };
        _settingsButton.Pressed += () => _gua.EnqueueClick("settings");
        AddChild(_settingsButton);

        _loadingLabel = new Label
        {
            Text = "Loading...",
            Position = new Vector2(544, 328),
            Size = new Vector2(192, 48),
            HorizontalAlignment = HorizontalAlignment.Center,
            Visible = false,
        };
        AddChild(_loadingLabel);
    }

    private void RebuildGuaFrame()
    {
        _gua.BeginFrame(_loading ? "loading" : "title");
        _gua.RegisterNode(
            "root",
            "screen",
            _loading ? "Loading Screen" : "Title Screen",
            new Rect2(Vector2.Zero, Size),
            visible: true,
            enabled: false);

        if (_loading)
        {
            RegisterControlNode("loading", "text", "Loading...", _loadingLabel, enabled: false);
        }
        else
        {
            RegisterControlNode("title", "text", "Gua C# Sample", _titleLabel, enabled: false);
            RegisterControlNode("start", "button", "Start Game", _startButton, enabled: true);
            RegisterControlNode("settings", "button", "Settings", _settingsButton, enabled: true);
        }

        _gua.EndFrame();
    }

    private void RegisterControlNode(string id, string role, string label, Control? control, bool enabled)
    {
        if (control is not null)
        {
            _gua.RegisterControlNode(id, role, label, control, enabled);
        }
    }

    private void PollGuaEvents()
    {
        while (_gua.TryPollEvent(out var e))
        {
            if (e.Type == GuaEventType.Click && e.NodeId == "start")
            {
                ShowLoading();
            }
        }
    }

    private void ShowLoading()
    {
        _loading = true;

        if (_titleLabel is not null)
        {
            _titleLabel.Visible = false;
        }

        if (_startButton is not null)
        {
            _startButton.Visible = false;
            _startButton.Disabled = true;
        }

        if (_settingsButton is not null)
        {
            _settingsButton.Visible = false;
            _settingsButton.Disabled = true;
        }

        if (_loadingLabel is not null)
        {
            _loadingLabel.Visible = true;
        }

        RebuildGuaFrame();
    }

    private void StartInspectorBridge()
    {
        if (_gua.StartInspectorBridge(InspectorBridgePort))
        {
            GD.Print($"Gua Inspector bridge listening on {_gua.InspectorBridgeUrl}");
            return;
        }

        GD.PushWarning($"Failed to start Gua Inspector bridge on ws://127.0.0.1:{InspectorBridgePort}");
    }
}
