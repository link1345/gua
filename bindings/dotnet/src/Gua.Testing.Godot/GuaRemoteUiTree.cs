namespace Gua.Testing.Godot;

public sealed class GuaRemoteUiTree
{
    public int? SchemaVersion { get; set; }

    public ulong? SessionEpoch { get; set; }

    public ulong? FrameSequence { get; set; }

    public ulong? Revision { get; set; }

    public string Screen { get; set; } = "";

    public List<GuaRemoteNode> Nodes { get; set; } = [];

    public GuaRemoteNode? FindNodeById(string id)
    {
        return Nodes.FirstOrDefault(node => string.Equals(node.Id, id, StringComparison.Ordinal));
    }
}

public sealed class GuaRemoteNode
{
    public string Id { get; set; } = "";

    public string Role { get; set; } = "";

    public string Label { get; set; } = "";

    public bool Visible { get; set; }

    public bool Enabled { get; set; }

    public string? ParentId { get; set; }

    public string? Text { get; set; }

    public string? Value { get; set; }

    public GuaRemoteNodeState? State { get; set; }
}

public sealed class GuaRemoteNodeState
{
    public bool? Focused { get; set; }
    public bool? Hovered { get; set; }
    public bool? Pressed { get; set; }
    public bool? Checked { get; set; }
    public bool? Selected { get; set; }
}
