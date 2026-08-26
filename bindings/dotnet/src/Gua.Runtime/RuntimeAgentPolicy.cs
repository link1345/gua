using System.Globalization;
using System.Runtime.InteropServices;
using Gua.Core;

namespace Gua.Runtime;

internal sealed class RuntimeAgentPolicy : IDisposable
{
    private readonly List<nint> allocations = [];
    internal Native.AgentPolicy Value { get; }

    internal RuntimeAgentPolicy(GuaAgentPolicy? source, GuaAgentExposure fallback = GuaAgentExposure.Auto)
    {
        source ??= new GuaAgentPolicy(fallback);
        try
        {
            var rules = (source.FieldRules ?? []).Select(CreateRule).ToArray();
            var size = Marshal.SizeOf<Native.AgentFieldRule>();
            nint memory = 0;
            if (rules.Length != 0)
            {
                memory = Marshal.AllocCoTaskMem(size * rules.Length); allocations.Add(memory);
                for (var index = 0; index < rules.Length; index++) Marshal.StructureToPtr(rules[index], memory + index * size, false);
            }
            ulong allowed = 0;
            foreach (var action in source.AllowedActions ?? []) allowed |= 1UL << (int)action;
            Value = new Native.AgentPolicy { StructSize = (uint)Marshal.SizeOf<Native.AgentPolicy>(), Exposure = (int)source.Exposure,
                HasAllowedActions = source.AllowedActions is null ? 0 : 1, AllowedActions = allowed, FieldRules = memory, FieldRuleCount = (uint)rules.Length };
        }
        catch { Dispose(); throw; }
    }

    private Native.AgentFieldRule CreateRule(GuaAgentFieldRule source)
    {
        if (string.IsNullOrWhiteSpace(source.Path)) throw new ArgumentException("Agent field rule path is required.");
        nint Text(string value) { var pointer = Marshal.StringToCoTaskMemUTF8(value); allocations.Add(pointer); return pointer; }
        var rule = new Native.AgentFieldRule { StructSize = (uint)Marshal.SizeOf<Native.AgentFieldRule>(), Path = Text(source.Path), Mode = (int)source.Mode, Quantum = source.Quantum };
        if (source.Mode == GuaAgentFieldMode.Replace && source.Replacement is null) rule.ReplacementType = 0;
        else if (source.Mode == GuaAgentFieldMode.Replace && source.Replacement is string text) { rule.ReplacementType = 1; rule.StringValue = Text(text); }
        else if (source.Mode == GuaAgentFieldMode.Replace && source.Replacement is bool value) { rule.ReplacementType = 3; rule.BoolValue = value ? 1 : 0; }
        else if (source.Mode == GuaAgentFieldMode.Replace) { rule.ReplacementType = 2; rule.NumberValue = Convert.ToDouble(source.Replacement, CultureInfo.InvariantCulture); if (!double.IsFinite(rule.NumberValue)) throw new ArgumentException($"Rule '{source.Path}' requires a primitive finite replacement."); }
        else if (source.Mode == GuaAgentFieldMode.Quantize && (!double.IsFinite(source.Quantum) || source.Quantum <= 0)) throw new ArgumentException($"Rule '{source.Path}' requires a positive finite quantum.");
        return rule;
    }

    public void Dispose() { foreach (var allocation in allocations) Marshal.FreeCoTaskMem(allocation); allocations.Clear(); }
}
