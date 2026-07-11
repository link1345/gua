using System.Text.Json;

namespace Gua.Core;

public sealed record GuaVersion(
    string ProtocolSchemaVersion,
    string CoreVersion,
    string RuntimeVersion,
    string? GodotPluginVersion,
    int AbiVersion,
    string BuildId,
    IReadOnlyList<string> Capabilities)
{
    public static GuaVersion Parse(string json) =>
        JsonSerializer.Deserialize<GuaVersion>(json, JsonOptions)
        ?? throw new InvalidOperationException("Gua returned an empty version response.");

    public void EnsureCompatible(
        string? protocolSchemaVersion = null,
        int? abiVersion = null,
        IEnumerable<string>? requiredCapabilities = null)
    {
        var missing = (requiredCapabilities ?? []).Except(Capabilities, StringComparer.Ordinal).ToArray();
        if ((protocolSchemaVersion is not null && ProtocolSchemaVersion != protocolSchemaVersion) ||
            (abiVersion is not null && AbiVersion != abiVersion) || missing.Length != 0)
        {
            throw new GuaCompatibilityException(
                $"Incompatible Gua runtime. Expected protocol={protocolSchemaVersion ?? "any"}, ABI={abiVersion?.ToString() ?? "any"}; " +
                $"actual protocol={ProtocolSchemaVersion}, ABI={AbiVersion}; missing capabilities=[{string.Join(", ", missing)}].",
                protocolSchemaVersion, abiVersion, this, missing);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
}

public sealed class GuaCompatibilityException : InvalidOperationException
{
    public GuaCompatibilityException(string message, string? expectedProtocolSchemaVersion, int? expectedAbiVersion,
        GuaVersion actual, IReadOnlyList<string> missingCapabilities) : base(message)
    {
        ExpectedProtocolSchemaVersion = expectedProtocolSchemaVersion;
        ExpectedAbiVersion = expectedAbiVersion;
        Actual = actual;
        MissingCapabilities = missingCapabilities;
    }

    public string? ExpectedProtocolSchemaVersion { get; }
    public int? ExpectedAbiVersion { get; }
    public GuaVersion Actual { get; }
    public IReadOnlyList<string> MissingCapabilities { get; }
}
