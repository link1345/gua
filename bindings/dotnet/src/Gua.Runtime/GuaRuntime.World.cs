using System.Runtime.InteropServices;
using System.Globalization;
using System.Numerics;
using System.Text;
using Gua.Core;

namespace Gua.Runtime;

public sealed partial class GuaRuntime
{
    public void EnableWorldObjectTreeAdapter() { ThrowIfDisposed(); Native.gua_runtime_set_world_object_tree_enabled(_handle, 1); }
    public void SetObservationProfile(GuaObservationProfile profile)
    {
        ThrowIfDisposed();
        if (profile is not GuaObservationProfile.Debug and not GuaObservationProfile.Player) throw new ArgumentOutOfRangeException(nameof(profile));
        if (Native.gua_runtime_set_observation_profile(_handle, (int)profile) == 0)
            throw new InvalidOperationException("The observation profile cannot change while the Inspector bridge is running.");
    }
    public void SetPlayerScreenshotEnabled(bool enabled)
    {
        ThrowIfDisposed();
        if (Native.gua_runtime_set_player_screenshot_enabled(_handle, enabled ? 1 : 0) == 0)
            throw new InvalidOperationException("Player screenshot policy cannot change while the Inspector bridge is running.");
    }
    public void BeginWorldFrame(string scene) { ThrowIfDisposed(); if (Native.gua_runtime_begin_world_frame(_handle, scene) == 0) throw new InvalidOperationException("Failed to begin the Gua world frame."); }
    public void EndWorldFrame() { ThrowIfDisposed(); if (Native.gua_runtime_end_world_frame(_handle) == 0) throw new InvalidOperationException("The Gua world frame was rejected."); }
    public void AbortWorldFrame() { ThrowIfDisposed(); if (Native.gua_runtime_abort_world_frame(_handle) == 0) throw new InvalidOperationException("There is no active Gua world frame to abort."); }

    public void RegisterWorldObject(GuaWorldObjectDescriptor source)
    {
        ThrowIfDisposed();
        if (source is null) throw new ArgumentNullException(nameof(source));
        var allocations = new List<nint>();
        using var policy = new RuntimeAgentPolicy(source.AgentPolicy, source.AgentExposure);
        nint Text(string? value) { if (value is null) return 0; var p = (nint)Marshal.StringToCoTaskMemUTF8(value); allocations.Add(p); return p; }
        try {
            var tagPointers = (source.Tags ?? []).Select(Text).ToArray();
            nint tags = 0;
            if (tagPointers.Length != 0) { tags = (nint)Marshal.AllocCoTaskMem(IntPtr.Size * tagPointers.Length); allocations.Add(tags); Marshal.Copy(tagPointers, 0, tags, tagPointers.Length); }
            var values = new List<Native.WorldState>();
            foreach (var pair in source.State ?? new Dictionary<string, object?>()) {
                var state = new Native.WorldState { StructSize = (uint)Marshal.SizeOf<Native.WorldState>(), Key = Text(pair.Key) };
                switch (pair.Value) {
                    case null: state.Type = 0; break;
                    case string text: state.Type = 1; state.StringValue = Text(text); break;
                    case bool boolean: state.Type = 3; state.BoolValue = boolean ? 1 : 0; break;
                    case byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                        state.Type = 2; state.NumberValue = WorldNumber(pair.Key, pair.Value); break;
                    default: throw new ArgumentException($"World state '{pair.Key}' must be a primitive JSON value.");
                }
                values.Add(state);
            }
            nint states = 0; var stateSize = Marshal.SizeOf<Native.WorldState>();
            if (values.Count != 0) { states = (nint)Marshal.AllocCoTaskMem(stateSize * values.Count); allocations.Add(states); for (var i = 0; i < values.Count; i++) Marshal.StructureToPtr(values[i], states + i * stateSize, false); }
            var native = new Native.WorldObject { StructSize = (uint)Marshal.SizeOf<Native.WorldObject>(), Id = Text(source.Id), ParentId = Text(source.ParentId), Kind = Text(source.Kind), Label = Text(source.Label), Description = Text(source.Description), Space = (int)source.Space,
                X = source.Position.X, Y = source.Position.Y, Z = source.Position.Z, VisibleToPlayer = source.VisibleToPlayer ? 1 : 0, Active = source.Active ? 1 : 0, AgentExposure = (int)source.AgentExposure,
                DomainId = Text(source.DomainId), RelatedUiNodeId = Text(source.RelatedUiNodeId), Tags = tags, TagCount = (uint)tagPointers.Length, StateValues = states, StateValueCount = (uint)values.Count };
            var secured = new Native.WorldObjectV2 { StructSize = (uint)Marshal.SizeOf<Native.WorldObjectV2>(), Base = native, AgentPolicy = policy.Value };
            if (Native.gua_runtime_register_world_object_v2(_handle, in secured) == 0) throw new InvalidOperationException($"Failed to register Gua world object '{source.Id}'.");
        } finally { foreach (var allocation in allocations) Marshal.FreeCoTaskMem(allocation); }
    }

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

    public unsafe string GetWorldObjectTreeJson()
    {
        ThrowIfDisposed();
        var required = Native.gua_runtime_copy_world_object_tree_json(_handle, null, 0);
        if (required <= 0) throw new InvalidOperationException("World Object Tree is unavailable.");
        while (true) {
            var bytes = new byte[required];
            fixed (byte* output = bytes) {
                var actual = Native.gua_runtime_copy_world_object_tree_json(_handle, output, bytes.Length);
                if (actual <= 0) throw new InvalidOperationException("World Object Tree is unavailable.");
                if (actual <= bytes.Length) return Encoding.UTF8.GetString(bytes, 0, actual - 1);
                required = actual;
            }
        }
    }
}
