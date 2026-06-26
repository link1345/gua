using Gua.Core;

namespace Gua.Testing;

public static class GuaAssertions
{
    public static GuaNodeExpectation ExpectNode(GuaContext context, string id)
    {
        return new GuaNodeExpectation(context, id);
    }
}

public sealed class GuaNodeExpectation
{
    private readonly GuaContext _context;
    private readonly string _id;

    internal GuaNodeExpectation(GuaContext context, string id)
    {
        _context = context;
        _id = id;
    }

    public void ToExist()
    {
        _context.GetNodeState(_id);
    }

    public void ToBeVisible()
    {
        var state = _context.GetNodeState(_id);
        if (!state.Visible)
        {
            throw new InvalidOperationException($"Expected Gua node to be visible: {_id}");
        }
    }

    public void ToBeEnabled()
    {
        var state = _context.GetNodeState(_id);
        if (!state.Enabled)
        {
            throw new InvalidOperationException($"Expected Gua node to be enabled: {_id}");
        }
    }

    public void Click()
    {
        if (!_context.EnqueueClick(_id))
        {
            throw new InvalidOperationException($"Failed to click Gua node: {_id}");
        }
    }
}
