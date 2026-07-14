using System.Text.Json;

namespace Gua.Testing;

public enum GuaRemoteScreenshotError { NotPublished, InvalidDataUri, InvalidPng, Headless, RenderingDisabled, Unsupported, Timeout, StaleSession }

public sealed class GuaRemoteScreenshotException : Exception
{
    public GuaRemoteScreenshotException(GuaRemoteScreenshotError error, string message, Exception? inner = null) : base(message, inner) => Error = error;
    public GuaRemoteScreenshotError Error { get; }
}

public sealed record GuaCapturedScreenshot(string DataUri, int Width, int Height, ulong RequestId = 0, ulong SessionEpoch = 0, ulong FrameSequence = 0)
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
    internal static GuaCapturedScreenshot Parse(string json)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        return new(root.GetProperty("dataUri").GetString() ?? "", root.GetProperty("width").GetInt32(), root.GetProperty("height").GetInt32(),
            root.TryGetProperty("requestId", out var id) ? id.GetUInt64() : 0,
            root.TryGetProperty("sessionEpoch", out var epoch) ? epoch.GetUInt64() : 0,
            root.TryGetProperty("frameSequence", out var frame) ? frame.GetUInt64() : 0);
    }
    public byte[] DecodePng()
    {
        const string prefix = "data:image/png;base64,";
        if (string.IsNullOrEmpty(DataUri)) throw new GuaRemoteScreenshotException(GuaRemoteScreenshotError.NotPublished, "The runtime has not published a screenshot.");
        if (!DataUri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new GuaRemoteScreenshotException(GuaRemoteScreenshotError.InvalidDataUri, "The Gua screenshot is not a PNG data URI.");
        try
        {
            var bytes = Convert.FromBase64String(DataUri.Substring(prefix.Length));
            if (bytes.Length < Signature.Length || !bytes.Take(Signature.Length).SequenceEqual(Signature)) throw new GuaRemoteScreenshotException(GuaRemoteScreenshotError.InvalidPng, "The Gua screenshot payload is not a valid PNG stream.");
            return bytes;
        }
        catch (FormatException error) { throw new GuaRemoteScreenshotException(GuaRemoteScreenshotError.InvalidDataUri, "The Gua screenshot data URI contains invalid base64.", error); }
    }
}
