namespace Gua.Core;

public interface IGuaContext
{
    GuaNodeState GetNodeState(string id);

    string FindNodeById(string id);

    string FindNodeByRole(string role, string? name = null);

    string FindNodeByText(string text);

    bool EnqueueClick(string id);
}
