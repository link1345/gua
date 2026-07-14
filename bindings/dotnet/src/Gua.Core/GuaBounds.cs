using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace Gua.Core;

[StructLayout(LayoutKind.Sequential)]
public readonly struct GuaBounds
{
    [JsonConstructor]
    public GuaBounds(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    [JsonPropertyName("x")]
    public float X { get; }
    [JsonPropertyName("y")]
    public float Y { get; }
    [JsonPropertyName("w")]
    public float Width { get; }
    [JsonPropertyName("h")]
    public float Height { get; }
}
