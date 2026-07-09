# Gua .NET Testing Sample

This sample shows the test-side loop required by `Gua.Testing`:

1. render a frame into a Gua semantic tree
2. enqueue a click with `Click()`
3. render another frame so the adapter can consume the click request
4. drain the emitted click event and update game state
5. render the resulting state and assert it

Run it after building the native runtime and setting `GUA_NATIVE_DIR`:

```powershell
cmake --preset windows-msvc-debug
cmake --build --preset windows-msvc-debug --target gua
$env:GUA_NATIVE_DIR = "$PWD\build\windows-msvc-debug\native\gua-core\Debug"
dotnet run --project examples/dotnet-testing/GuaDotNetTestingSample.csproj
```
