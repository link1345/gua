# Gua.Testing.Unity

Starts Unity 6000.5+ Editor Play Mode or Mono standalone players on Windows x64,
Linux x64, Intel macOS, and Apple Silicon macOS, and
connects `Gua.Testing` to the Unity adapter's WebSocket bridge.

Use `UnityPlayerBuilder.Build`, `UnitySceneTestHost.LoadPlayer`,
`LoadRenderedPlayer`, `LoadEditor`, or `BuildAndLoadPlayer`. The host resolves
Unity from an explicit option, `UNITY_EXECUTABLE`, then the Unity Hub install
directory. It allocates an available bridge port by default and captures Unity
logs in startup and teardown diagnostics.

`UnityPlayerBuildOptions.Platform` accepts `Auto`, `WindowsX64`, `LinuxX64`,
`MacOSX64`, `MacOSArm64`, and `MacOSUniversal`. `Auto` selects the current host.
