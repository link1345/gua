# Gua Repo Skill

Use this skill when working on the Gua repository.

## Context

Gua is a runtime UI automation protocol for games. The core product is a
language-independent protocol that exposes semantic UI trees and accepts
automation commands from tools such as test clients, inspectors, MCP servers,
and AI agents.

## Rules

- Read `README.md` and `protocol/specs/protocol.md` before product or
  architecture changes.
- Treat `protocol/schema` and `protocol/specs` as the cross-language contract.
- Keep `native/gua-core` as the reference C ABI implementation.
- Build C++ and ImGui APIs as thin layers over the core.
- Build .NET as P/Invoke bindings over the C ABI.
- Keep test helpers centered on C++ and C#, not TypeScript.
- Prefer adapter-owned UI reflection for integrations such as ImGui and Godot;
  do not require game code to restate standard host UI semantics manually.
- Use MSVC as the primary Windows C++ toolchain. Preserve portability for Apple
  Clang and Android NDK Clang by keeping shared code in standard C++20 and C ABI.
- Avoid editor automation, image-recognition-first QA, or a full UI framework.

## Current Product Surface

Keep protocol schemas, the C ABI runtime, C++ and C# testing helpers, engine
adapters, Inspector, and MCP aligned. Treat Inspector and MCP as consumers of
the runtime protocol rather than separate sources of runtime state.
