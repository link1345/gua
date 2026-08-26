namespace Gua.Core;

public enum GuaAgentExposure { Auto = 0, Private = 1 }
public enum GuaAgentFieldMode { Keep = 0, Omit = 1, Redact = 2, Replace = 3, Quantize = 4 }
public sealed record GuaAgentFieldRule(string Path, GuaAgentFieldMode Mode, object? Replacement = null, double Quantum = 0);
public sealed record GuaAgentPolicy(
    GuaAgentExposure Exposure = GuaAgentExposure.Auto,
    IReadOnlyList<GuaAgentFieldRule>? FieldRules = null,
    IReadOnlyList<GuaActionType>? AllowedActions = null);

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
    CaretPosition = 1UL << 8,
    Selection = 1UL << 9,
    Scroll = 1UL << 10,
    ScrollMax = 1UL << 11,
    RangeValue = 1UL << 12,
    RangeMin = 1UL << 13,
    RangeMax = 1UL << 14,
    SelectedIndex = 1UL << 15,
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
    bool? Selected = null,
    long? CaretPosition = null, long? SelectionStart = null, long? SelectionEnd = null,
    double? ScrollX = null, double? ScrollY = null, double? ScrollMaxX = null, double? ScrollMaxY = null,
    double? RangeValue = null, double? RangeMin = null, double? RangeMax = null, long? SelectedIndex = null,
    GuaAgentPolicy? AgentPolicy = null);

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
