using System.Text.Json;

namespace Gua.Testing.Godot;

public enum GuaScreenshotError
{
    NotPublished,
    InvalidDataUri,
    InvalidPng,
}

public sealed class GuaScreenshotException : Exception
{
    public GuaScreenshotException(GuaScreenshotError error, string message, Exception? inner = null) : base(message, inner) => Error = error;
    public GuaScreenshotError Error { get; }
}

public sealed record GuaScreenshot(string DataUri, int Width, int Height)
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    internal static GuaScreenshot Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new GuaScreenshot(
            root.GetProperty("dataUri").GetString() ?? string.Empty,
            root.GetProperty("width").GetInt32(),
            root.GetProperty("height").GetInt32());
    }

    public byte[] DecodePng()
    {
        if (string.IsNullOrEmpty(DataUri))
            throw new GuaScreenshotException(GuaScreenshotError.NotPublished, "The Godot adapter has not published a screenshot.");
        const string prefix = "data:image/png;base64,";
        if (!DataUri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new GuaScreenshotException(GuaScreenshotError.InvalidDataUri, "The Gua screenshot is not a PNG data URI.");
        try
        {
            var bytes = Convert.FromBase64String(DataUri[prefix.Length..]);
            if (bytes.Length < PngSignature.Length || !bytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
                throw new GuaScreenshotException(GuaScreenshotError.InvalidPng, "The Gua screenshot payload is not a valid PNG stream.");
            return bytes;
        }
        catch (FormatException error)
        {
            throw new GuaScreenshotException(GuaScreenshotError.InvalidDataUri, "The Gua screenshot data URI contains invalid base64.", error);
        }
    }
}

public sealed record GuaSavedScreenshot(int Width, int Height, string AbsolutePath);
