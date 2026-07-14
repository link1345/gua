# Gua.Testing.Unity

Starts Unity 6 Windows x64 Editor Play Mode or Mono standalone players and
connects `Gua.Testing` to the Unity adapter's WebSocket bridge.

Use `UnityPlayerBuilder.Build`, `UnitySceneTestHost.LoadPlayer`,
`LoadRenderedPlayer`, `LoadEditor`, or `BuildAndLoadPlayer`. The host resolves
Unity from an explicit option, `UNITY_EXECUTABLE`, then the Unity Hub install
directory. It allocates an available bridge port by default and captures Unity
logs in startup and teardown diagnostics.
