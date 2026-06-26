# C++ ImGui Example

This is the first C++ demo for Gua's ImGui adapter boundary. It follows Dear
ImGui's official `examples/example_win32_directx11` shape: Win32 window,
DirectX 11 renderer backend, and `imgui_impl_win32` / `imgui_impl_dx11`.

```powershell
cmake --preset windows-msvc-debug
cmake --build --preset windows-msvc-debug
.\build\windows-msvc-debug\examples\cpp-imgui\Debug\gua-cpp-imgui-example.exe
```

The executable opens a small game-like ImGui surface. Clicking `Start Game`
registers a Gua click event and transitions the semantic UI tree from `title`
to `loading`.

For a non-interactive smoke check:

```powershell
.\build\windows-msvc-debug\examples\cpp-imgui\Debug\gua-cpp-imgui-example.exe --smoke
```
