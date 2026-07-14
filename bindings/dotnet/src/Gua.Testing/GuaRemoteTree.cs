using Gua.Core;
using System.Text.Json.Serialization;

namespace Gua.Testing;

public sealed class GuaRemoteTree
{
    public string Screen { get; set; } = "";
    public ulong Revision { get; set; }
    public List<GuaRemoteNode> Nodes { get; set; } = new();
}

public sealed class GuaRemoteNode
{
    public string Id { get; set; } = "";
    public string? ParentId { get; set; }
    public string Role { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Text { get; set; }
    public string? Value { get; set; }
    public bool Visible { get; set; }
    public bool Enabled { get; set; }
    [JsonPropertyName("state")]
    public GuaRemoteNodeState State { get; set; } = new();
    [JsonIgnore]
    public bool? Focused { get => State.Focused; set => State.Focused = value; }
    [JsonIgnore]
    public bool? Checked { get => State.Checked; set => State.Checked = value; }
    [JsonIgnore]
    public bool? Selected { get => State.Selected; set => State.Selected = value; }
    public GuaBounds Bounds { get; set; }
    public List<string> Actions { get; set; } = new();
}

public sealed class GuaRemoteNodeState
{
    public bool? Focused { get; set; }
    public bool? Checked { get; set; }
    public bool? Selected { get; set; }
}
