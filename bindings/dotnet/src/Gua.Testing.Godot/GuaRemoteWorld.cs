using Gua.Core;

namespace Gua.Testing.Godot;

public sealed partial class GuaRemoteContext
{
    public string GetWorldObjectTreeJson(GuaObservationProfile profile = GuaObservationProfile.Debug) =>
        RequestRawResult(new { type = "get_world_object_tree" });

    public GuaWorldTree GetWorldObjectTree(GuaObservationProfile profile = GuaObservationProfile.Debug) =>
        Request<GuaWorldTree>(new { type = "get_world_object_tree" });

    public GuaWorldQueryResult QueryWorldObjects(GuaWorldSelector selector, GuaObservationProfile profile = GuaObservationProfile.Debug) =>
        Request<GuaWorldQueryResult>(new {
            type = "query_world_objects", worldId = selector.Id, worldIdMatch = (int)selector.IdMatch,
            kind = selector.Kind, kindMatch = (int)selector.KindMatch, label = selector.Label, labelMatch = (int)selector.LabelMatch,
            tag = selector.Tag, tagMatch = (int)selector.TagMatch, parentId = selector.ParentId,
            directChild = selector.DirectChild ? 1 : 0, visibleToPlayer = WorldFilter(selector.VisibleToPlayer), active = WorldFilter(selector.Active),
            stateKey = selector.State?.Key, stateType = selector.State is null ? null : WorldStateType(selector.State.Value),
            stateString = selector.State?.Value as string,
            stateNumber = selector.State?.Value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal ? Convert.ToDouble(selector.State.Value) : (double?)null,
            stateBool = selector.State?.Value as bool?
        });

    public async Task<GuaWorldObject> WaitForWorldObjectAsync(GuaWorldSelector selector, TimeSpan? timeout = null,
        GuaObservationProfile profile = GuaObservationProfile.Debug, CancellationToken cancellationToken = default)
    {
        var limit = timeout ?? TimeSpan.FromSeconds(5);
        var started = System.Diagnostics.Stopwatch.StartNew();
        while (started.Elapsed <= limit) {
            cancellationToken.ThrowIfCancellationRequested();
            var result = QueryWorldObjects(selector, profile);
            if (!result.Valid) throw new ArgumentException(result.Error ?? "Invalid world selector.", nameof(selector));
            var match = result.Matches.FirstOrDefault();
            if (match is not null) return match;
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException("Timed out waiting for a Gua world object.");
    }

    private static int WorldFilter(bool? value) => value is null ? 0 : value.Value ? 2 : 1;
    private static int? WorldStateType(object? value) => value switch { null => 0, string => 1, byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => 2, bool => 3, _ => null };
}
