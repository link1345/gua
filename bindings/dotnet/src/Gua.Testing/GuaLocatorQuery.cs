using Gua.Core;

namespace Gua.Testing;

public sealed record GuaLocatorQuery
{
    private readonly IGuaContext _context;
    private readonly GuaSelector _selector;

    internal GuaLocatorQuery(IGuaContext context, GuaSelector? selector = null)
    {
        _context = context;
        _selector = selector ?? new GuaSelector();
    }

    public GuaLocatorQuery ById(string id, GuaMatchMode match = GuaMatchMode.Exact) => Next(_selector with { Id = id, IdMatch = match });
    public GuaLocatorQuery ByRole(string role, string? name = null, GuaMatchMode match = GuaMatchMode.Exact) =>
        Next(_selector with { Role = role, RoleMatch = match, Name = name, NameMatch = match });
    public GuaLocatorQuery ByText(string text, GuaMatchMode match = GuaMatchMode.Exact) => Next(_selector with { Text = text, TextMatch = match });
    public GuaLocatorQuery Within(string parentId, bool directChild = false) => Next(_selector with { ParentId = parentId, DirectChild = directChild });
    public GuaLocatorQuery WhereVisible(bool visible = true) => Next(_selector with { Visible = visible ? GuaStateFilter.True : GuaStateFilter.False });
    public GuaLocatorQuery WhereEnabled(bool enabled = true) => Next(_selector with { Enabled = enabled ? GuaStateFilter.True : GuaStateFilter.False });

    public IReadOnlyList<GuaNodeExpectation> QueryAll()
    {
        var result = Execute();
        return result.Matches.Select(match => new GuaNodeExpectation(_context, match.Id, Describe())).ToArray();
    }

    public GuaNodeExpectation Get()
    {
        var result = Execute();
        if (result.Matches.Count == 0)
            GuaAssertions.Fail($"Strict Gua selector matched no nodes: {Describe()}.");
        if (result.Matches.Count > 1)
        {
            var candidates = string.Join("; ", result.Matches.Select(match =>
                $"{match.Id} ({match.Role}, '{match.Label}', parentId='{match.ParentId ?? "<root>"}')"));
            GuaAssertions.Fail($"Strict Gua selector matched {result.Matches.Count} nodes: {Describe()}. Candidates: {candidates}. Narrow the scope with Within(...) or add stable id/state filters.");
        }
        return new GuaNodeExpectation(_context, result.Matches[0].Id, Describe());
    }

    public GuaLocatorQuery AssertCount(int expected)
    {
        var actual = Execute().Matches.Count;
        if (actual != expected)
            GuaAssertions.Fail($"Expected selector {Describe()} to match {expected} nodes, but matched {actual}.");
        return this;
    }

    private GuaQueryResult Execute()
    {
        GuaQueryResult result;
        try
        {
            result = _context.Query(_selector);
        }
        catch (NotSupportedException) when (CanUseLegacyFallback())
        {
            var id = _selector.Id is not null
                ? _context.FindNodeById(_selector.Id)
                : _selector.Role is not null
                    ? _context.FindNodeByRole(_selector.Role, _selector.Name)
                    : _context.FindNodeByText(_selector.Text!);
            result = new GuaQueryResult(true, [new GuaNodeQueryMatch(id, "legacy", "legacy", null)]);
        }
        if (!result.Valid)
            GuaAssertions.Fail($"Invalid Gua selector {Describe()}: {result.Error ?? "unknown syntax error"}");
        return result;
    }

    private bool CanUseLegacyFallback() =>
        _selector.ParentId is null && !_selector.DirectChild &&
        _selector.Visible == GuaStateFilter.Any && _selector.Enabled == GuaStateFilter.Any &&
        _selector.IdMatch == GuaMatchMode.Exact && _selector.RoleMatch == GuaMatchMode.Exact &&
        _selector.NameMatch == GuaMatchMode.Exact && _selector.TextMatch == GuaMatchMode.Exact &&
        (_selector.Id is not null || _selector.Role is not null || _selector.Text is not null);

    private GuaLocatorQuery Next(GuaSelector selector) => new(_context, selector);

    private string Describe()
    {
        var fields = new List<string>();
        if (_selector.Id is not null) fields.Add($"id={_selector.IdMatch}:{_selector.Id}");
        if (_selector.Role is not null) fields.Add($"role={_selector.RoleMatch}:{_selector.Role}");
        if (_selector.Name is not null) fields.Add($"name={_selector.NameMatch}:{_selector.Name}");
        if (_selector.Text is not null) fields.Add($"text={_selector.TextMatch}:{_selector.Text}");
        if (_selector.ParentId is not null) fields.Add($"scope={_selector.ParentId} ({(_selector.DirectChild ? "direct children" : "descendants")})");
        if (_selector.Visible != GuaStateFilter.Any) fields.Add($"visible={_selector.Visible == GuaStateFilter.True}");
        if (_selector.Enabled != GuaStateFilter.Any) fields.Add($"enabled={_selector.Enabled == GuaStateFilter.True}");
        return "{" + string.Join(", ", fields) + "}";
    }
}
