# Gua Repository Brief for AI Agents

Gua exists to make in-game UI testable like web UI by exposing runtime UI as a
semantic tree. It should remain protocol-first and engine-independent.

When working in this repository:

- Start from `protocol/` for behavioral contracts.
- Keep native ABI boundaries in C, not C++.
- Keep C++, ImGui, C#, MCP, Inspector, and .NET aligned with schema
  changes.
- Keep the primary test API usable from C++ and C#.
- Use MSVC as the Windows native reference toolchain, while preserving the C ABI
  and standard C++20 portability for Apple Clang and Android NDK Clang.
- Do not make Unity, Unreal, Godot, or MonoGame the first-class center of the
  design.
- Do not introduce image-recognition as the primary automation model.

The first useful demo should prove this path:

1. A game-like UI registers semantic nodes.
2. Gua exports a UI tree.
3. A C++ or C# test finds `Start Game`.
4. The test clicks it through the runtime bridge.
5. The runtime reports the next UI state.
