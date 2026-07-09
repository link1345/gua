namespace Gua.Testing.Godot;

public sealed class GuaRemoteUiTree
{
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
}
