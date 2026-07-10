namespace Gua.Core;

public interface IGuaContext
{
    string GetUiTreeJson();

    GuaNodeState GetNodeState(string id);

    string FindNodeById(string id);

    string FindNodeByRole(string role, string? name = null);

    string FindNodeByText(string text);

    bool EnqueueClick(string id);

    GuaActionError EnqueueAction(GuaActionRequest request, out ulong requestId);

    bool TryPollActionEvent(out GuaActionEvent e);

    bool TryPollActionEvent(ulong requestId, out GuaActionEvent e);

    bool TryPollEvent(out GuaEvent e);
}
