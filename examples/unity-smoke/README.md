# Unity 6 Windows Editor smoke test

This sample verifies the first supported Unity configuration: Unity 6 on the
Windows x64 Editor with **Api Compatibility Level** set to **.NET Standard 2.1**.
It does not add a Unity-specific runtime; `Gua.Core` remains a thin P/Invoke
binding over the stable native C ABI.

## Prepare the managed plug-ins

Build both Unity-compatible assemblies:

```powershell
dotnet restore bindings/dotnet/src/Gua.Testing/Gua.Testing.csproj
dotnet build bindings/dotnet/src/Gua.Core/Gua.Core.csproj --configuration Release --framework netstandard2.1 --no-restore
dotnet build bindings/dotnet/src/Gua.Testing/Gua.Testing.csproj --configuration Release --framework netstandard2.1 --no-restore
```

Copy `Gua.Core.dll` and `Gua.Testing.dll` from their
`bin/Release/netstandard2.1` directories into
`Assets/Plugins/Gua/Managed` in the Unity project. When consuming the NuGet
packages, also install their dependency closure, including `System.Text.Json`;
.NET Standard itself does not provide that library. A Unity NuGet client may do
this automatically, or the dependency assemblies can be copied beside the Gua
managed plug-ins.

## Prepare the Windows x64 native plug-in

```powershell
cmake --preset windows-msvc-release
cmake --build --preset windows-msvc-release --target gua --config Release
```

Copy `build/windows-msvc-release/native/gua-core/Release/gua.dll` to
`Assets/Plugins/x86_64/gua.dll`. In Unity's Plugin Inspector, enable it for the
Windows Editor and Windows Standalone x86_64 targets. The `netstandard2.1`
binding intentionally uses normal `[DllImport("gua")]` resolution so Unity's
Plugin Import Settings own native loading.

Copy `Assets/GuaUnitySmoke.cs` from this directory into the Unity project,
attach `GuaUnitySmoke` to a GameObject, and enter Play Mode. A successful run
logs a semantic tree containing the UTF-8 label `開始`. This exercises managed
assembly loading, `GuaContext` creation/destruction, native P/Invoke, and the
minimal UI tree round trip.

The checked-in command-line host exercises the same `netstandard2.1` Gua
assemblies and native calls without requiring Unity:

```powershell
dotnet build examples/unity-smoke/Gua.UnitySmoke.csproj --configuration Release
Copy-Item build/windows-msvc-release/native/gua-core/Release/gua.dll examples/unity-smoke/bin/Release/net10.0/gua.dll
dotnet run --project examples/unity-smoke/Gua.UnitySmoke.csproj --configuration Release
```

Only Unity 6 Windows Editor x64 is in the verified scope. IL2CPP/AOT, Android,
iOS, macOS, and player architectures other than Windows x64 require separate
native packaging and marshalling verification.

Unity reference: [.NET profile support](https://docs.unity3d.com/ja/current/Manual/dotnet-profile-support.html).
