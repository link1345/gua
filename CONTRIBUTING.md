# Contributing to Gua

Gua is still in the planning and early implementation phase. Contributions should
prefer small, protocol-aligned changes over broad framework work.

## Development Priorities

1. Define the protocol clearly.
2. Keep the C ABI stable and minimal.
3. Build one reference path first: C++ core, ImGui adapter, C++ tests, C# tests.
4. Add MCP, Inspector, and .NET bindings after the core behavior is testable.

## Native Builds

Windows C++ development should use MSVC through the repository CMake presets.
Keep shared native code portable C++20 so Apple Clang and Android NDK Clang can
be added without redesigning the ABI.

## Pull Requests

- Explain which part of the protocol or runtime surface changes.
- Include tests or examples for behavior changes.
- Do not commit build outputs, dependency directories, local caches, or generated
  artifacts unless they are intentionally source-controlled fixtures.
