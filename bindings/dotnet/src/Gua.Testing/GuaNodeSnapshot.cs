using Gua.Core;

namespace Gua.Testing;

public sealed record GuaNodeSnapshot(
    string Id,
    string Role,
    string Label,
    GuaBounds Bounds,
    bool Visible,
    bool Enabled,
    IReadOnlyList<string> Actions);
