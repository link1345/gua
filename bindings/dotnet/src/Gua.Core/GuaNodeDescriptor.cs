namespace Gua.Core;

[Flags]
public enum GuaNodeKnownState : ulong
{
    None = 0,
    ParentId = 1UL << 0,
    Text = 1UL << 1,
    Value = 1UL << 2,
    Focused = 1UL << 3,
    Hovered = 1UL << 4,
    Pressed = 1UL << 5,
    Checked = 1UL << 6,
    Selected = 1UL << 7,
}

public sealed record GuaNodeDescriptor(
    string Id,
    string Role,
    string Label,
    GuaBounds Bounds,
    bool Visible = true,
    bool Enabled = true,
    string? ParentId = null,
    string? Text = null,
    string? Value = null,
    bool? Focused = null,
    bool? Hovered = null,
    bool? Pressed = null,
    bool? Checked = null,
    bool? Selected = null);

public sealed record GuaNodeStateV2(
    GuaNodeKnownState KnownState,
    bool Visible,
    bool Enabled,
    string? ParentId,
    string? Text,
    string? Value,
    bool? Focused,
    bool? Hovered,
    bool? Pressed,
    bool? Checked,
    bool? Selected);
