using System.Runtime.InteropServices;

namespace Gua.Core;

[StructLayout(LayoutKind.Sequential)]
public struct GuaNodeState
{
    private int _visible;
    private int _enabled;

    public GuaNodeState(bool visible, bool enabled)
    {
        _visible = visible ? 1 : 0;
        _enabled = enabled ? 1 : 0;
    }

    public readonly bool Visible => _visible != 0;
    public readonly bool Enabled => _enabled != 0;
}
