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
        _gua.Attach(this);
        _gua.SyncAttachedTree(CurrentScreen);

        if (StartInspectorBridgeOnReady)
        {
            StartInspectorBridge();
        }

    }

    public override void _Process(double delta)
    {
        _gua.SyncAttachedTree(CurrentScreen);
    }

    public override void _ExitTree()
    {
        _gua.Dispose();
    }

    private void BuildUi()
    {
        _titleLabel = new Label
        {
            Name = "title",
            Text = "Gua C# Sample",
            Position = new Vector2(420, 220),
            Size = new Vector2(440, 56),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        AddChild(_titleLabel);

        _startButton = new Button
        {
            Name = "start",
            Text = "Start Game",
            Position = new Vector2(512, 312),
            Size = new Vector2(256, 56),
        };
        _startButton.Pressed += ShowLoading;
        AddChild(_startButton);

        _settingsButton = new Button
        {
            Name = "settings",
            Text = "Settings",
            Position = new Vector2(512, 384),
            Size = new Vector2(256, 56),
        };
        AddChild(_settingsButton);

        _loadingLabel = new Label
        {
            Name = "loading",
            Text = "Loading...",
            Position = new Vector2(544, 328),
            Size = new Vector2(192, 48),
            HorizontalAlignment = HorizontalAlignment.Center,
            Visible = false,
        };
        AddChild(_loadingLabel);
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

        _gua.SyncAttachedTree(CurrentScreen);
    }

    private string CurrentScreen => _loading ? "loading" : "title";

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
