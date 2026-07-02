namespace Gua.Core;

public readonly struct GuaEvent
{
    public GuaEvent(GuaEventType type, string nodeId)
    {
        Type = type;
        NodeId = nodeId;
    }

    public GuaEventType Type { get; }
    public string NodeId { get; }
}
