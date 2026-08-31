# Gua.Runtime

Managed `net10.0` and `netstandard2.1` wrapper over the stable Gua runtime C
ABI. Engine adapters use it to publish semantic frames, consume actions,
complete screenshot requests, expose adapter versions, and run the Inspector
WebSocket bridge without duplicating P/Invoke declarations.

`GuaRuntime.Clock` is the adapter-side clock pump. Adapters call
`AdvanceMilliseconds(unscaledDeltaMs)` when their engine exposes elapsed time
as a floating-point value, or `Advance(unscaledDelta)` when a `TimeSpan` is
already available. Game code uses `Schedule` and `Tick` for deterministic
pause/run-for behavior. Installing the
clock does not intercept `Time.deltaTime`, coroutines, or engine timers; each
game subsystem that should be controllable must explicitly use this clock as
its time source.

`Tick` receives `GuaClockDelta`, which retains the native double-precision
millisecond value even below `TimeSpan`'s 100 ns resolution. It exposes
`TotalMilliseconds`, `TotalSeconds`, and an explicitly fallible `TimeSpan`
projection. Clock status likewise keeps `NowMilliseconds` as the authoritative
protocol value; `Now` is null when that value exceeds `TimeSpan`'s range.
Adapter callback failures are reported by `CallbackFailed` after the scheduler
isolates the failure and continues the remaining due callbacks and tick
notification.

The NuGet package deploys `gua_runtime.dll`, `libgua_runtime.so`, or
`libgua_runtime.dylib` from its `win-x64`, `linux-x64`, `osx-x64`, and
`osx-arm64` native assets. `GUA_RUNTIME_NATIVE_DIR` remains available for local
build overrides.

Local distributable packing requires an absolute `GuaNativeAssetsRoot` whose
four RID directories contain `gua_runtime.dll`, `libgua_runtime.so`, or
`libgua_runtime.dylib` as appropriate. Packing fails when that root is omitted
without the legacy Windows Release DLL, or when any required RID asset is
missing.

Screenshot adapters should use `TryCompleteScreenshot`. It returns `false`
when a timeout, cancellation, or context reset has already invalidated the
request; such a late completion is benign and must not be retried.

## Game input adapter API

Publish a complete action-map frame with `PublishGameInputActions`, then enable
only the initialized `GuaGameInputCapabilities` with `EnableGameInput` and a
shutdown action that synchronously neutralizes every injected host value before
the native runtime is destroyed. Local consumers create a
`GuaGameInputSession`; `Dispose` queues owner-scoped neutral cleanup. Adapters
call `TryConsumeGameInput`, inject the request on the host thread, and then call
`CompleteGameInput`. Call `TickGameInputLeases` once per host frame with
unscaled elapsed time. Enqueue acceptance and host completion are deliberately
separate; local callers use `GuaGameInputSession.PollResult(requestId)` for the
correlated completion. Lease timing never uses `GuaRuntime.Clock`.
The optional third `EnableGameInput` argument is the Player/Public Agent
capability ceiling and defaults to `None`. Trusted engine bridges create a
Player session with `CreateGameInputSession(GuaObservationProfile.Player)`;
the runtime intersects that authorization with initialized adapter capabilities
at enqueue and again immediately before host consumption.
