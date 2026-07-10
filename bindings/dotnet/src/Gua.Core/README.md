# Gua.Core

`Gua.Core` is the .NET binding for the native Gua C ABI.

The NuGet package includes the Windows x64 native runtime at:

```text
runtimes/win-x64/native/gua.dll
```

.NET copies that native asset to the consuming app or test output as part of
normal package restore/build. `GUA_NATIVE_DIR` remains available when you want to
override the packaged runtime with a locally built one.

Semantic operations use `EnqueueAction` and return a `requestId`. The host adapter
consumes the request, performs the real UI operation, then calls
`EmitActionResult`; tests observe completion with `TryPollActionEvent`. Supported
v1 actions are focus, set value, set checked, select, scroll, and key press.
Click remains available through the original API and shares the same queue.

At runtime the resolver checks:

1. `GUA_NATIVE_DIR`
2. the .NET output directory
3. the assembly directory
4. the current directory

To pack the NuGet package locally, build the release native runtime first:

```powershell
cmake --preset windows-msvc-release
cmake --build --preset windows-msvc-release --target gua
dotnet pack bindings/dotnet/src/Gua.Core/Gua.Core.csproj --configuration Release
```

`GuaContext.ConfigureDiagnostics` sets the bounded retained-history limit and
environment JSON. `GetDiagnosticsJson` returns the versioned semantic failure
snapshot without draining the event queue; sensitive action values are already
redacted by the native core.
