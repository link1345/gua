using System;
using System.Collections.Generic;
using System.Linq;
using Gua.Runtime;
using UnityEngine;

namespace Gua.Unity
{

[Serializable]
public sealed class GuaGameInputAction
{
    public string Id = "";
    [TextArea] public string Description = "";
    public GuaGameInputValueType ValueType;
    public bool HasRange;
    public float Minimum = -1;
    public float Maximum = 1;
    public bool Holdable;
    public bool Active = true;
    public string[] Bindings = Array.Empty<string>();
    public string Risk = "safe";
    public bool RequiresConfirmation;

    internal GuaGameInputActionDescriptor Descriptor() => new(
        Id, Description, ValueType, HasRange ? Minimum : null, HasRange ? Maximum : null,
        Holdable, Active, Bindings, Risk, RequiresConfirmation);
}

public sealed class GuaGameInputMap : MonoBehaviour
{
    public string Context = "gameplay";
    public bool EnableRawInput;
    public List<GuaGameInputAction> Actions = new();
    internal IReadOnlyList<GuaGameInputActionDescriptor> Descriptors() => Actions.Select(action => action.Descriptor()).ToArray();
}

}
