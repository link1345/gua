using Gua.Core;
using System.Globalization;
using System.Numerics;

namespace Gua.Testing.Godot;

public sealed partial class GuaRemoteContext
{
    public string GetWorldObjectTreeJson(GuaObservationProfile profile = GuaObservationProfile.Debug) =>
        RequestRawResult(new { type = "get_world_object_tree" });

    public GuaWorldTree GetWorldObjectTree(GuaObservationProfile profile = GuaObservationProfile.Debug) =>
        Request<GuaWorldTree>(new { type = "get_world_object_tree" });

    public GuaWorldQueryResult QueryWorldObjects(GuaWorldSelector selector, GuaObservationProfile profile = GuaObservationProfile.Debug) =>
        Request<GuaWorldQueryResult>(WorldQueryCommand(selector));

    private static object WorldQueryCommand(GuaWorldSelector selector)
    {
        var command = new Dictionary<string, object?> { ["type"] = "query_world_objects" };
        void Text(string name, string? value, GuaMatchMode match) { if (value is not null) { command[name] = value; command[name + "Match"] = (int)match; } }
        Text("worldId", selector.Id, selector.IdMatch); Text("kind", selector.Kind, selector.KindMatch);
        Text("label", selector.Label, selector.LabelMatch); Text("tag", selector.Tag, selector.TagMatch);
        if (selector.ParentId is not null) command["parentId"] = selector.ParentId;
        if (selector.DirectChild) command["directChild"] = 1;
        if (selector.VisibleToPlayer is not null) command["visibleToPlayer"] = WorldFilter(selector.VisibleToPlayer);
        if (selector.Active is not null) command["active"] = WorldFilter(selector.Active);
        if (selector.State is { } state) {
            command["stateKey"] = state.Key;
            command["stateType"] = WorldStateType(state.Value) ?? throw new ArgumentException($"World state '{state.Key}' must be a primitive JSON value.", nameof(selector));
            if (state.Value is string text) command["stateString"] = text;
            else if (state.Value is bool boolean) command["stateBool"] = boolean;
            else if (state.Value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
                command["stateNumber"] = WorldNumber(state.Key, state.Value);
        }
        if (selector.Near is { } near) { command["relativeToObjectId"] = near.RelativeToObjectId; command["maxDistance"] = near.MaxDistance; }
        if (selector.Limit is { } limit) command["limit"] = limit;
        return command;
    }

    public async Task<GuaWorldObject> WaitForWorldObjectAsync(GuaWorldSelector selector, TimeSpan? timeout = null,
        GuaObservationProfile profile = GuaObservationProfile.Debug, CancellationToken cancellationToken = default)
    {
        var limit = timeout ?? TimeSpan.FromSeconds(5);
        var started = System.Diagnostics.Stopwatch.StartNew();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(limit > TimeSpan.Zero ? limit : TimeSpan.Zero);
        try {
            while (started.Elapsed <= limit) {
                var remaining = limit - started.Elapsed;
                if (remaining <= TimeSpan.Zero) break;
                var result = await RequestAsync<GuaWorldQueryResult>(WorldQueryCommand(selector), deadline.Token,
                    responseTimeout: remaining).ConfigureAwait(false);
                if (!result.Valid) throw new ArgumentException(result.Error ?? "Invalid world selector.", nameof(selector));
                var match = result.Matches.FirstOrDefault();
                if (match is not null) return match;
                remaining = limit - started.Elapsed;
                if (remaining <= TimeSpan.Zero) break;
                await Task.Delay(remaining < TimeSpan.FromMilliseconds(50) ? remaining : TimeSpan.FromMilliseconds(50), deadline.Token).ConfigureAwait(false);
            }
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            // The linked deadline maps to the public timeout contract below.
        }
        throw new TimeoutException("Timed out waiting for a Gua world object.");
    }

    private static int WorldFilter(bool? value) => value is null ? 0 : value.Value ? 2 : 1;
    private static int? WorldStateType(object? value) => value switch { null => 0, string => 1, byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => 2, bool => 3, _ => null };
    private static double WorldNumber(string key, object value)
    {
        var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        if (!double.IsFinite(number)) throw new ArgumentException($"World state '{key}' must be a finite number.");
        if (value is byte or sbyte or short or ushort or int or uint or long or ulong) {
            var integral = BigInteger.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
            if (new BigInteger(number) != integral)
                throw new ArgumentException($"World state '{key}' cannot be represented precisely as an ABI double.");
        } else if (value is decimal) {
            try {
                if (Convert.ToDecimal(value, CultureInfo.InvariantCulture) != Convert.ToDecimal(number))
                    throw new ArgumentException($"World state '{key}' cannot be represented precisely as an ABI double.");
            } catch (OverflowException) {
                throw new ArgumentException($"World state '{key}' cannot be represented precisely as an ABI double.");
            }
        }
        return number;
    }
}
