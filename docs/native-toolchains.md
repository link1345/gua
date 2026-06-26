# Native Toolchains

Gua's native reference implementation is developed first on Windows with MSVC.
That is the primary local toolchain for early C++ work.

The project should still keep the native core portable:

- Public native boundary: C ABI
- C++ implementation: standard C++20
- Windows: MSVC
- macOS and iOS: Apple Clang
- Android: Android NDK Clang
- Linux: Clang or GCC can be supported later, but it is not the first target

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

## Apple And Android

Apple and Android support should be added as separate CMake presets or toolchain
files when those targets become active. The native API shape should not change
for them; they should consume the same C ABI.
