using Gua.Core;

namespace Gua.Testing;

public sealed record GuaNodeSnapshot(
    string Id,
    string Role,
    string Label,
    GuaBounds Bounds,
    bool Visible,
    bool Enabled,
    IReadOnlyList<string> Actions,
    string? ParentId = null,
    string? Text = null,
    string? Value = null,
    bool? Focused = null,
    bool? Hovered = null,
    bool? Pressed = null,
    bool? Checked = null,
    bool? Selected = null,
    int? SchemaVersion = null,
    ulong? FrameSequence = null,
    ulong? Revision = null);
