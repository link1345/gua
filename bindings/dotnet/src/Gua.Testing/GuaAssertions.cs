using Gua.Core;

namespace Gua.Testing;

public static class GuaAssertions
{
    public static GuaNodeExpectation GetById(IGuaContext context, string id)
    {
        return new GuaNodeExpectation(context, context.FindNodeById(id));
    }

    public static GuaNodeExpectation GetByRole(IGuaContext context, string role, string? name = null)
    {
        return new GuaNodeExpectation(context, context.FindNodeByRole(role, name));
    }

    public static GuaNodeExpectation GetByText(IGuaContext context, string text)
    {
        return new GuaNodeExpectation(context, context.FindNodeByText(text));
    }

    public static GuaNodeExpectation ExpectNode(IGuaContext context, string id)
    {
        return GetById(context, id);
    }

    public static void WaitFor(GuaNodeExpectation expectation, TimeSpan? timeout = null)
    {
        expectation.WaitFor(timeout);
    }

    public static GuaNodeExpectation WaitForId(IGuaContext context, string id, TimeSpan? timeout = null)
    {
        return WaitForQuery(() => GetById(context, id), $"id: {id}", timeout);
    }

    public static GuaNodeExpectation WaitForRole(IGuaContext context, string role, string? name = null, TimeSpan? timeout = null)
    {
        return WaitForQuery(() => GetByRole(context, role, name), name is null ? $"role: {role}" : $"role and name: {role}, {name}", timeout);
    }

    public static GuaNodeExpectation WaitForText(IGuaContext context, string text, TimeSpan? timeout = null)
    {
        return WaitForQuery(() => GetByText(context, text), $"text: {text}", timeout);
    }

    private static GuaNodeExpectation WaitForQuery(Func<GuaNodeExpectation> query, string description, TimeSpan? timeout)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(1));
        do
        {
            try
            {
                return query();
            }
            catch (InvalidOperationException)
            {
                Thread.Sleep(10);
            }
        }
        while (DateTime.UtcNow < deadline);

        throw new TimeoutException($"Timed out waiting for Gua node by {description}");
    }
}

public sealed class GuaNodeExpectation
{
    private readonly IGuaContext _context;
    private readonly string _id;

    internal GuaNodeExpectation(IGuaContext context, string id)
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

    public void WaitFor(TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(1));
        do
        {
            try
            {
                _context.GetNodeState(_id);
                return;
            }
            catch (InvalidOperationException)
            {
                Thread.Sleep(10);
            }
        }
        while (DateTime.UtcNow < deadline);

        throw new TimeoutException($"Timed out waiting for Gua node: {_id}");
    }
}
