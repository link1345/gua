# Gua.Core

`Gua.Core` is the .NET binding for the native Gua C ABI.

The NuGet package includes the Windows x64 native runtime at:

```text
runtimes/win-x64/native/gua.dll
```

.NET copies that native asset to the consuming app or test output as part of
normal package restore/build. `GUA_NATIVE_DIR` remains available when you want to
override the packaged runtime with a locally built one.

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
