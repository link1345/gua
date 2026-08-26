using System;
using System.Collections.Generic;
using System.Linq;
using Gua.Core;
using UnityEngine;

namespace Gua.Unity
{
[DisallowMultipleComponent]
public sealed class GuaAgentPolicyComponent : MonoBehaviour
{
    [Serializable]
    public sealed class FieldRule
    {
        public string path = string.Empty;
        public GuaAgentFieldMode mode;
        public string stringReplacement = string.Empty;
        public double numberReplacement;
        public bool booleanReplacement;
        public enum ReplacementType { Null, String, Number, Boolean }
        public ReplacementType replacementType;
        public double quantum;
        internal GuaAgentFieldRule ToPolicy() => new(path, mode,
            mode != GuaAgentFieldMode.Replace ? null : replacementType == ReplacementType.String ? stringReplacement :
            replacementType == ReplacementType.Number ? numberReplacement : replacementType == ReplacementType.Boolean ? booleanReplacement : null, quantum);
    }

    [SerializeField] private bool overrideExposure;
    [SerializeField] private GuaAgentExposure exposure;
    [SerializeField] private FieldRule[] fieldRules = Array.Empty<FieldRule>();
    [SerializeField] private bool overrideAllowedActions;
    [SerializeField] private GuaActionType[] allowedActions = Array.Empty<GuaActionType>();

    public GuaAgentPolicy Policy => new(overrideExposure ? exposure : null, fieldRules.Where(rule => rule != null).Select(rule => rule.ToPolicy()).ToArray(),
        overrideAllowedActions ? new List<GuaActionType>(allowedActions) : null);
}
}
