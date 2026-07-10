# Gua .NET NUnit Sample

This sample shows the recommended .NET shape for real tests: use NUnit as the
test runner and use `Gua.Testing` for Gua-specific locators, waits, assertions,
and adapter test loops.

One C# file can contain multiple NUnit tests:

```csharp
[Test]
public void StartClickShowsLoadingText()
{
    using var _ = GuaAssertionScope.UseNUnit(Assert.Fail);
    GuaAssertions.GetByRole(ui, "button", "Start Game").Click();
}
```

This project intentionally references `Gua.Testing` as a NuGet package instead
of a source `ProjectReference`.

Run it after building the release native runtime and packing the local NuGet
packages. `Gua.Core` carries `gua.dll` as a `runtimes/win-x64/native` asset, so
`GUA_NATIVE_DIR` is not required for this package-based sample.

```powershell
cmake --preset windows-msvc-release
cmake --build --preset windows-msvc-release --target gua
dotnet pack bindings/dotnet/src/Gua.Core/Gua.Core.csproj --configuration Release
dotnet pack bindings/dotnet/src/Gua.Testing/Gua.Testing.csproj --configuration Release
dotnet test examples/dotnet-nunit/GuaDotNetNUnitSample.csproj
```

For a Godot process or native runtime shared across tests, create a
`GuaTestSession` in setup, call non-strict `Reset()` to start a new epoch, and
use `Reset(new GuaResetOptions(Strict: true))` in teardown. Strict teardown
reports leaked requests/events and leaves them intact for diagnosis. Logs and
screenshots are preserved by default; select `GuaResetTargets.All` to clear them.
