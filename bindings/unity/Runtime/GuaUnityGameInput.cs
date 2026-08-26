using System;
using System.Collections.Generic;
using System.Text.Json;
using Gua.Runtime;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

namespace Gua.Unity
{

public sealed partial class GuaUnityRuntime
{
    private readonly Dictionary<string, object?> gameInputValues = new(StringComparer.Ordinal);
    public static event Action<string, object?>? GameInputChanged;

#if ENABLE_INPUT_SYSTEM
    private Keyboard? virtualKeyboard;
    private Mouse? virtualMouse;
    private Gamepad? virtualGamepad;
    private readonly HashSet<ButtonControl> heldButtons = new();
    private readonly HashSet<AxisControl> heldAxes = new();
#endif

    public static object? GetGameInputValue(string actionId, object? fallback = null) =>
        activeRuntime != null && activeRuntime.gameInputValues.TryGetValue(actionId, out var value) ? value : fallback;

    private void InitializeGameInput()
    {
        if (runtime == null) return;
        var map = FindFirstObjectByType<GuaGameInputMap>(FindObjectsInactive.Include);
        if (map == null) return;
        runtime.PublishGameInputActions(map.Context, map.Descriptors());
        var capabilities = GuaGameInputCapabilities.Semantic;
#if ENABLE_INPUT_SYSTEM
        if (map.EnableRawInput)
        {
            virtualKeyboard ??= InputSystem.AddDevice<Keyboard>();
            virtualMouse ??= InputSystem.AddDevice<Mouse>();
            virtualGamepad ??= InputSystem.AddDevice<Gamepad>();
            capabilities |= GuaGameInputCapabilities.Keyboard | GuaGameInputCapabilities.Pointer |
                GuaGameInputCapabilities.Gamepad | GuaGameInputCapabilities.Text;
        }
#endif
        runtime.EnableGameInput(capabilities);
    }

    private void PumpGameInput()
    {
        if (runtime == null) return;
        runtime.TickGameInputLeases(TimeSpan.FromSeconds(Time.unscaledDeltaTime));
        while (runtime.TryConsumeGameInput(out var request))
        {
            try
            {
                var succeeded = ApplyGameInput(request);
                runtime.CompleteGameInput(request, succeeded, succeeded ? 0 : -4);
            }
            catch (Exception error)
            {
                Debug.LogError($"Gua game input {request.RequestId} failed: {error.Message}");
                runtime.CompleteGameInput(request, false, -6);
            }
        }
#if ENABLE_INPUT_SYSTEM
        InputSystem.Update();
#endif
    }

    private bool ApplyGameInput(GuaGameInputRequest request)
    {
        if (request.Kind == GuaGameInputKind.Cleanup || request.Operation == GuaGameInputOperation.ReleaseAll)
        {
            ReleaseAllInjectedInput();
            return true;
        }
        if (request.Kind == GuaGameInputKind.Semantic)
        {
            if (request.Operation == GuaGameInputOperation.Press)
            {
                SetSemantic(request.Target, true);
                SetSemantic(request.Target, false);
                return true;
            }
            if (request.Operation == GuaGameInputOperation.Release)
            {
                object neutral = gameInputValues.TryGetValue(request.Target, out var current) && current is Vector2 ? Vector2.zero :
                    current is double or float ? 0d : false;
                SetSemantic(request.Target, neutral);
                return true;
            }
            if (request.Operation != GuaGameInputOperation.Set) return false;
            using var document = JsonDocument.Parse(request.ValueJson);
            var root = document.RootElement;
            object? value = root.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => root.GetDouble(),
                JsonValueKind.String => root.GetString(),
                JsonValueKind.Object when root.TryGetProperty("x", out var x) && root.TryGetProperty("y", out var y) =>
                    new Vector2(x.GetSingle(), y.GetSingle()),
                _ => null,
            };
            SetSemantic(request.Target, value);
            return value != null;
        }
#if ENABLE_INPUT_SYSTEM
        if (request.Kind == GuaGameInputKind.Keyboard && virtualKeyboard != null)
        {
            if (!TryKey(request.Target, out var key)) return false;
            var control = virtualKeyboard[key];
            if (request.Operation == GuaGameInputOperation.Press)
            {
                InputSystem.QueueDeltaStateEvent(control, 1f);
                InputSystem.Update();
                InputSystem.QueueDeltaStateEvent(control, 0f);
            }
            else
            {
                var pressed = request.Operation == GuaGameInputOperation.Down;
                InputSystem.QueueDeltaStateEvent(control, pressed ? 1f : 0f);
                if (pressed) heldButtons.Add(control); else heldButtons.Remove(control);
            }
            return true;
        }
        if (request.Kind == GuaGameInputKind.Pointer && virtualMouse != null)
        {
            if (request.Operation is GuaGameInputOperation.MoveAbsolute or GuaGameInputOperation.MoveDelta)
            {
                var position = new Vector2((float)request.X, (float)request.Y);
                if (request.Operation == GuaGameInputOperation.MoveAbsolute && request.Target.EndsWith("viewport_normalized", StringComparison.Ordinal))
                    position = Vector2.Scale(position, new Vector2(Screen.width, Screen.height));
                InputSystem.QueueDeltaStateEvent(request.Operation == GuaGameInputOperation.MoveAbsolute ? virtualMouse.position : virtualMouse.delta, position);
                return true;
            }
            if (request.Operation == GuaGameInputOperation.Wheel)
            {
                InputSystem.QueueDeltaStateEvent(virtualMouse.scroll, new Vector2((float)request.X, (float)request.Y));
                return true;
            }
            var button = PointerButton(request.Target);
            if (button == null) return false;
            var pressed = request.Operation == GuaGameInputOperation.Down;
            InputSystem.QueueDeltaStateEvent(button, pressed ? 1f : 0f);
            if (pressed) heldButtons.Add(button); else heldButtons.Remove(button);
            return true;
        }
        if (request.Kind == GuaGameInputKind.Gamepad && virtualGamepad != null)
        {
            if (request.Operation == GuaGameInputOperation.Reset) { ReleaseAllInjectedInput(); return true; }
            if (request.Operation == GuaGameInputOperation.Set)
            {
                var axis = GamepadAxis(request.Target);
                if (axis == null) return false;
                using var value = JsonDocument.Parse(request.ValueJson);
                InputSystem.QueueDeltaStateEvent(axis, value.RootElement.GetSingle());
                heldAxes.Add(axis);
                return true;
            }
            var button = GamepadButton(request.Target);
            if (button == null) return false;
            var pressed = request.Operation == GuaGameInputOperation.Down;
            InputSystem.QueueDeltaStateEvent(button, pressed ? 1f : 0f);
            if (pressed) heldButtons.Add(button); else heldButtons.Remove(button);
            return true;
        }
        if (request.Kind == GuaGameInputKind.TextInput && virtualKeyboard != null)
        {
            using var text = JsonDocument.Parse(request.ValueJson);
            foreach (var character in text.RootElement.GetString() ?? "") InputSystem.QueueTextEvent(virtualKeyboard, character);
            return true;
        }
#endif
        return false;
    }

    private void SetSemantic(string actionId, object? value)
    {
        gameInputValues[actionId] = value;
        GameInputChanged?.Invoke(actionId, value);
    }

    private void DisposeGameInput()
    {
        ReleaseAllInjectedInput();
#if ENABLE_INPUT_SYSTEM
        if (virtualKeyboard != null) InputSystem.RemoveDevice(virtualKeyboard);
        if (virtualMouse != null) InputSystem.RemoveDevice(virtualMouse);
        if (virtualGamepad != null) InputSystem.RemoveDevice(virtualGamepad);
        virtualKeyboard = null; virtualMouse = null; virtualGamepad = null;
#endif
    }

    private void ReleaseAllInjectedInput()
    {
        foreach (var action in new List<string>(gameInputValues.Keys))
            SetSemantic(action, gameInputValues[action] is Vector2 ? Vector2.zero : gameInputValues[action] is double or float ? 0d : false);
#if ENABLE_INPUT_SYSTEM
        foreach (var button in heldButtons) InputSystem.QueueDeltaStateEvent(button, 0f);
        foreach (var axis in heldAxes) InputSystem.QueueDeltaStateEvent(axis, 0f);
        heldButtons.Clear(); heldAxes.Clear();
        InputSystem.Update();
#endif
    }

#if ENABLE_INPUT_SYSTEM
    private static bool TryKey(string code, out Key key)
    {
        if (code.StartsWith("Key", StringComparison.Ordinal) && code.Length == 4)
            return Enum.TryParse(code.Substring(3), true, out key);
        if (code.StartsWith("Digit", StringComparison.Ordinal) && code.Length == 6)
            return Enum.TryParse("Digit" + code.Substring(5), true, out key);
        return Enum.TryParse(code.Replace("Arrow", ""), true, out key);
    }
    private ButtonControl? PointerButton(string name) => name switch
    {
        "primary" => virtualMouse?.leftButton, "secondary" => virtualMouse?.rightButton,
        "auxiliary" => virtualMouse?.middleButton, "back" => virtualMouse?.backButton,
        "forward" => virtualMouse?.forwardButton, _ => null,
    };
    private AxisControl? GamepadAxis(string name) => name switch
    {
        "left_stick_x" => virtualGamepad?.leftStick.x, "left_stick_y" => virtualGamepad?.leftStick.y,
        "right_stick_x" => virtualGamepad?.rightStick.x, "right_stick_y" => virtualGamepad?.rightStick.y, _ => null,
    };
    private ButtonControl? GamepadButton(string name) => name switch
    {
        "south" => virtualGamepad?.buttonSouth, "east" => virtualGamepad?.buttonEast,
        "west" => virtualGamepad?.buttonWest, "north" => virtualGamepad?.buttonNorth,
        "left_shoulder" => virtualGamepad?.leftShoulder, "right_shoulder" => virtualGamepad?.rightShoulder,
        "left_trigger" => virtualGamepad?.leftTrigger, "right_trigger" => virtualGamepad?.rightTrigger,
        "back" => virtualGamepad?.selectButton, "start" => virtualGamepad?.startButton,
        "left_stick" => virtualGamepad?.leftStickButton, "right_stick" => virtualGamepad?.rightStickButton,
        "dpad_up" => virtualGamepad?.dpad.up, "dpad_down" => virtualGamepad?.dpad.down,
        "dpad_left" => virtualGamepad?.dpad.left, "dpad_right" => virtualGamepad?.dpad.right, _ => null,
    };
#endif
}

}
