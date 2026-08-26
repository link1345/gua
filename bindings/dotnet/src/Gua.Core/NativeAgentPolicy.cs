using System.Globalization;
using System.Runtime.InteropServices;

namespace Gua.Core;

internal sealed class NativeAgentPolicy : IDisposable
{
    private readonly List<nint> allocations = [];
    internal Native.GuaNativeAgentPolicyV1 Value { get; }

    internal NativeAgentPolicy(GuaAgentPolicy? source, GuaAgentExposure fallback = GuaAgentExposure.Auto)
    {
        source ??= new GuaAgentPolicy(fallback);
        try
        {
            var rules = (source.FieldRules ?? []).Select(Rule).ToArray();
            nint ruleMemory = 0;
            var ruleSize = Marshal.SizeOf<Native.GuaNativeAgentFieldRuleV1>();
            if (rules.Length != 0)
            {
                ruleMemory = Marshal.AllocCoTaskMem(ruleSize * rules.Length);
                allocations.Add(ruleMemory);
                for (var index = 0; index < rules.Length; index++)
                    Marshal.StructureToPtr(rules[index], ruleMemory + index * ruleSize, false);
            }
            ulong allowed = 0;
            foreach (var action in source.AllowedActions ?? []) allowed |= 1UL << (int)action;
            Value = new Native.GuaNativeAgentPolicyV1
            {
                StructSize = (uint)Marshal.SizeOf<Native.GuaNativeAgentPolicyV1>(),
                Exposure = (int)source.Exposure,
                HasAllowedActions = source.AllowedActions is null ? 0 : 1,
                AllowedActions = allowed,
                FieldRules = ruleMemory,
                FieldRuleCount = (uint)rules.Length,
            };
        }
        catch { Dispose(); throw; }
    }

    private Native.GuaNativeAgentFieldRuleV1 Rule(GuaAgentFieldRule source)
    {
        if (string.IsNullOrWhiteSpace(source.Path)) throw new ArgumentException("Agent field rule path is required.", nameof(source));
        nint Text(string value) { var pointer = Marshal.StringToCoTaskMemUTF8(value); allocations.Add(pointer); return pointer; }
        var result = new Native.GuaNativeAgentFieldRuleV1
        {
            StructSize = (uint)Marshal.SizeOf<Native.GuaNativeAgentFieldRuleV1>(), Path = Text(source.Path), Mode = (int)source.Mode,
            Quantum = source.Quantum,
        };
        switch (source.Mode)
        {
            case GuaAgentFieldMode.Replace when source.Replacement is null: result.ReplacementType = 0; break;
            case GuaAgentFieldMode.Replace when source.Replacement is string text: result.ReplacementType = 1; result.StringValue = Text(text); break;
            case GuaAgentFieldMode.Replace when source.Replacement is bool boolean: result.ReplacementType = 3; result.BoolValue = boolean ? 1 : 0; break;
            case GuaAgentFieldMode.Replace:
                result.ReplacementType = 2; result.NumberValue = Convert.ToDouble(source.Replacement, CultureInfo.InvariantCulture);
                if (!double.IsFinite(result.NumberValue)) throw new ArgumentException($"Rule '{source.Path}' requires a primitive finite replacement."); break;
            case GuaAgentFieldMode.Quantize when !double.IsFinite(source.Quantum) || source.Quantum <= 0:
                throw new ArgumentException($"Rule '{source.Path}' requires a positive finite quantum.");
        }
        return result;
    }

    public void Dispose()
    {
        foreach (var allocation in allocations) Marshal.FreeCoTaskMem(allocation);
        allocations.Clear();
    }
}
