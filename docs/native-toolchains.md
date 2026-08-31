# Native Toolchains

Gua's native reference implementation is developed first on Windows with MSVC.
That is the primary local toolchain for early C++ work.

The project should still keep the native core portable:

- Public native boundary: C ABI
- C++ implementation: standard C++20
- Windows: MSVC
- macOS: the native core, runtime, and WebSocket bridge are built with Apple Clang on Intel and Apple Silicon
- iOS: Apple Clang when this target becomes active
- Android: Android NDK Clang
- Linux: the native core, runtime, WebSocket bridge, and native bridge example
  are built in CI with the default Ubuntu C++ toolchain

Do not put Windows API calls, MSVC-only extensions, or platform-specific behavior
inside protocol-level code. If platform code becomes necessary, isolate it under
a platform-specific native directory.

## Windows MSVC

Use the CMake presets as the official Windows entrypoint:

```powershell
cmake --preset windows-msvc-debug
cmake --build --preset windows-msvc-debug
```

Release build:

```powershell
cmake --preset windows-msvc-release
cmake --build --preset windows-msvc-release
```

## Linux CI

Portable native targets are configured and built on `ubuntu-latest`:

```sh
cmake -S . -B build/cpp -DCMAKE_BUILD_TYPE=Debug
cmake --build build/cpp --parallel
```

The portable native matrix also runs on Intel and Apple Silicon macOS. It builds
the shared runtime and native bridge example, runs CTest, and exercises the
Inspector WebSocket contract through the .NET selector suite. The Win32 DirectX
11 ImGui example remains Windows-only.

## Apple And Android

Desktop macOS is validated with Apple Clang for both `osx-x64` and `osx-arm64`.
iOS and Android should be added as separate CMake presets or toolchain files
when those targets become active. The native API shape should not change for
them; they should consume the same C ABI.
