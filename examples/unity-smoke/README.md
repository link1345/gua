# Unity 6 runtime UI fixture

This is the complete Unity 6000.5.3f1 project used to verify the first supported
configuration: Windows x64 with the Mono scripting backend. It references the
source UPM package in `bindings/unity` and creates UI Toolkit, uGUI, and
TextMeshPro controls without manual Gua node registration.

The rendered sample mirrors the Godot GDScript title flow at 1280x720. It
publishes stable `start`, `settings`, and `loading` ids: Settings intentionally
keeps the `title` screen unchanged, while Start Game switches the logical screen
to `loading` and displays `Loading...`. The Player keeps updating while in the
background so Inspector and MCP actions remain responsive.

Build the managed/native closure and UPM artifact:

```powershell
.\scripts\build-unity-package.ps1
```

Build a fixture Player through the public test-host API or Unity batch method:

```csharp
var player = UnityPlayerBuilder.Build(
    @"examples\unity-smoke\Assets\Scenes\GuaUnityFixture.unity");
using var host = UnitySceneTestHost.LoadRenderedPlayer(player);
var button = host.Context.FindNodeByRole("button", "Start Game");
host.Context.EnqueueClick(button);
```

The external integration tests are enabled with `GUA_UNITY_PLAYER` for a built
Player or `GUA_UNITY_SCENE` for Editor Play Mode:

```powershell
$env:GUA_UNITY_PLAYER = "$PWD\artifacts\unity-player\GuaUnitySmoke.exe"
dotnet test bindings/dotnet/tests/Gua.Unity.Integration.Tests
```

The fixture verifies adapter version reporting, automatic UI reflection,
request-correlated action completion after Unity listeners run, and rendered
PNG capture. Generated Unity directories, plug-ins, TMP Essential Resources,
players, packages, and logs stay out of git.
