using System;
using Gua.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Gua.Unity
{
internal sealed class GuaUnityTmpAdapter : IGuaUnityControlAdapter
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register() { GuaUnityAdapterRegistry.Register(new GuaUnityTmpAdapter()); }

    public bool TryApply(object target, GuaActionRequest request, out string value)
    {
        value = null;
        if (target is TMP_InputField input && request.Action == GuaActionType.SetValue)
        { input.text = request.Value ?? ""; value = input.text; return true; }
        if (target is TMP_InputField keyInput && request.Action == GuaActionType.PressKey)
        {
            if (!GuaUnityKeyEvent.TryCreateGesture(request, out var keyDown, out var keyUp)) return false;
            if (!GuaUnityKeyEvent.TryQueueKeyUp(keyUp)) return false;
            keyInput.ProcessEvent(keyDown); value = keyInput.text; return true;
        }
        if (target is TMP_Dropdown dropdown && request.Action == GuaActionType.Select)
        {
            var index = dropdown.options.FindIndex(option => string.Equals(option.text, request.Value, StringComparison.Ordinal));
            if (index < 0) return false;
            dropdown.value = index; value = dropdown.options[index].text; return true;
        }
        return false;
    }

    public bool TryDescribe(Transform transform, out object target, out string role, out string label, out string value)
    {
        var button = transform.GetComponent<Button>();
        var buttonLabel = button != null ? transform.GetComponentInChildren<TMP_Text>(true) : null;
        if (button != null && buttonLabel != null) { target = button; role = "button"; label = buttonLabel.text; value = null; return true; }
        var input = transform.GetComponent<TMP_InputField>();
        if (input != null) { target = input; role = "textbox"; label = input.text; value = input.text; return true; }
        var dropdown = transform.GetComponent<TMP_Dropdown>();
        if (dropdown != null)
        {
            target = dropdown; role = "combobox"; label = dropdown.captionText != null ? dropdown.captionText.text : transform.name;
            value = dropdown.value >= 0 && dropdown.value < dropdown.options.Count ? dropdown.options[dropdown.value].text : ""; return true;
        }
        var text = transform.GetComponent<TMP_Text>();
        if (text != null) { target = text; role = "text"; label = text.text; value = null; return true; }
        target = transform.gameObject; role = "panel"; label = transform.name; value = null; return false;
    }
}
}
