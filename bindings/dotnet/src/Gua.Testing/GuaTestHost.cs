using Gua.Core;

namespace Gua.Testing;

public sealed class GuaTestHost
{
    private readonly GuaContext _context;

    public GuaTestHost(GuaContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public void Frame(string screen, Action<GuaTestHost> buildUi)
    {
        Guard.NotNullOrWhiteSpace(screen, nameof(screen));
        Guard.NotNull(buildUi, nameof(buildUi));

        _context.BeginFrame(screen);
        buildUi(this);
        _context.EndFrame();
    }

    public bool Button(
        string id,
        string label,
        GuaBounds bounds,
        bool visible = true,
        bool enabled = true)
    {
        RegisterNode(id, "button", label, bounds, visible, enabled);
        if (!_context.ConsumeClickRequest(id))
        {
            return false;
        }

        _context.EmitClick(id);
        return true;
    }

    public void Text(
        string id,
        string text,
        GuaBounds bounds,
        bool visible = true)
    {
        RegisterNode(id, "text", text, bounds, visible, enabled: false);
    }

    public void Panel(
        string id,
        string label,
        GuaBounds bounds,
        bool visible = true)
    {
        RegisterNode(id, "panel", label, bounds, visible, enabled: false);
    }

    public void RegisterNode(
        string id,
        string role,
        string label,
        GuaBounds bounds,
        bool visible = true,
        bool enabled = true)
    {
        Guard.NotNullOrWhiteSpace(id, nameof(id));
        Guard.NotNullOrWhiteSpace(role, nameof(role));
        _context.RegisterNode(id, role, label, bounds, visible, enabled);
    }

    public bool DrainClickEvents(Action<string> onClick)
    {
        Guard.NotNull(onClick, nameof(onClick));

        var handled = false;
        while (_context.TryPollEvent(out var e))
        {
            if (e.Type != GuaEventType.Click)
            {
                continue;
            }

            handled = true;
            onClick(e.NodeId);
        }

        return handled;
    }
}
