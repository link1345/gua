# Runtime UI Fixture

Import this sample from Package Manager and open `GuaRuntimeUiSample.unity`.
The scene creates UI Toolkit, uGUI, and TextMeshPro controls at runtime. Gua
starts automatically; no semantic node registration is required.

## Virtual clock integration

Gua can pause only game logic that explicitly uses GuaClock as its time source.
Use `GuaUnityRuntime.Clock.Schedule(...)` or subscribe to
`GuaUnityRuntime.Clock.Tick` in the target game subsystem. Calling
`clock_install` from a test, MCP, or Inspector activates that shared clock; it
does not rewrite existing `Time.deltaTime`, `WaitForSeconds`, coroutines,
physics, animation, or audio behavior.

The `Tick` callback receives a `GuaClockDelta`; use `TotalMilliseconds` or
`TotalSeconds` to retain steps below the 100 ns resolution of `TimeSpan`.

The package targets Windows x64, Unity 6000.5 or newer, and the Mono scripting
backend. Import TextMeshPro Essential Resources before building a Player.
