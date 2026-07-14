using System;
using System.Reflection;
using Gua.Core;
using UnityEngine;

namespace Gua.Unity
{

public static class GuaUnityKeyEvent
{
    private static readonly MethodInfo QueueEvent = typeof(Event).GetMethod("QueueEvent", BindingFlags.NonPublic | BindingFlags.Static);

    public static bool TryCreateGesture(GuaActionRequest request, out Event keyDown, out Event keyUp)
    {
        keyDown = null;
        keyUp = null;
        if (!TryParseKey(request.Key, out var keyCode)) return false;

        var modifiers = ToEventModifiers(request.Modifiers);
        keyDown = new Event
        {
            type = EventType.KeyDown,
            keyCode = keyCode,
            character = CharacterFor(keyCode, request.Key, modifiers),
            modifiers = modifiers,
        };
        keyUp = new Event(keyDown) { type = EventType.KeyUp, character = '\0' };
        return true;
    }

    public static bool TryQueueKeyUp(Event keyUp)
    {
        if (QueueEvent == null) return false;
        try { QueueEvent.Invoke(null, new object[] { keyUp }); return true; }
        catch { return false; }
    }

    public static EventModifiers ToEventModifiers(uint modifiers)
    {
        var result = EventModifiers.None;
        if ((modifiers & 1U) != 0) result |= EventModifiers.Shift;
        if ((modifiers & 2U) != 0) result |= EventModifiers.Alt;
        if ((modifiers & 4U) != 0) result |= EventModifiers.Control;
        if ((modifiers & 8U) != 0) result |= EventModifiers.Command;
        return result;
    }

    private static bool TryParseKey(string value, out KeyCode keyCode)
    {
        var normalized = value ?? string.Empty;
        if (string.Equals(normalized, "Enter", StringComparison.OrdinalIgnoreCase)) normalized = "Return";
        else if (string.Equals(normalized, "Esc", StringComparison.OrdinalIgnoreCase)) normalized = "Escape";
        else if (string.Equals(normalized, "ArrowUp", StringComparison.OrdinalIgnoreCase)) normalized = "UpArrow";
        else if (string.Equals(normalized, "ArrowDown", StringComparison.OrdinalIgnoreCase)) normalized = "DownArrow";
        else if (string.Equals(normalized, "ArrowLeft", StringComparison.OrdinalIgnoreCase)) normalized = "LeftArrow";
        else if (string.Equals(normalized, "ArrowRight", StringComparison.OrdinalIgnoreCase)) normalized = "RightArrow";
        return Enum.TryParse(normalized, true, out keyCode) && keyCode != KeyCode.None;
    }

    private static char CharacterFor(KeyCode keyCode, string requested, EventModifiers modifiers)
    {
        if (keyCode == KeyCode.Space) return ' ';
        if (keyCode is KeyCode.Return or KeyCode.KeypadEnter) return '\n';
        if (keyCode == KeyCode.Tab) return '\t';
        if (keyCode == KeyCode.Backspace) return '\b';
        if (!string.IsNullOrEmpty(requested) && requested.Length == 1)
        {
            var value = requested[0];
            if (char.IsLetter(value)) return (modifiers & EventModifiers.Shift) != 0 ? char.ToUpperInvariant(value) : char.ToLowerInvariant(value);
            return value;
        }
        return '\0';
    }
}

}
