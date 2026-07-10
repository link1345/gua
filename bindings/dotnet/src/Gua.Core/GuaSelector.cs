namespace Gua.Core;

public enum GuaMatchMode
{
    Exact = 0,
    Contains = 1,
    Regex = 2,
}

public enum GuaStateFilter
{
    Any = 0,
    False = 1,
    True = 2,
}

public sealed record GuaSelector(
    string? Id = null,
    GuaMatchMode IdMatch = GuaMatchMode.Exact,
    string? Role = null,
    GuaMatchMode RoleMatch = GuaMatchMode.Exact,
    string? Name = null,
    GuaMatchMode NameMatch = GuaMatchMode.Exact,
    string? Text = null,
    GuaMatchMode TextMatch = GuaMatchMode.Exact,
    string? ParentId = null,
    bool DirectChild = false,
    GuaStateFilter Visible = GuaStateFilter.Any,
    GuaStateFilter Enabled = GuaStateFilter.Any);

public sealed record GuaNodeQueryMatch(string Id, string Role, string Label, string? ParentId);

public sealed record GuaQueryResult(bool Valid, IReadOnlyList<GuaNodeQueryMatch> Matches, string? Error = null);
