# Gua Repo Skill

Use this skill when working on the Gua repository.

## Context

Gua is a runtime UI automation protocol for games. The core product is a
language-independent protocol that exposes semantic UI trees and accepts
automation commands from tools such as test clients, inspectors, MCP servers,
and AI agents.

## Rules

- Read `plan.md` before product or architecture changes.
- Treat `protocol/schema` and `protocol/specs` as the cross-language contract.
- Keep `native/gua-core` as the reference C ABI implementation.
- Build C++ and ImGui APIs as thin layers over the core.
- Build .NET as P/Invoke bindings over the C ABI.
- Keep test helpers centered on C++ and C#, not TypeScript.
- Avoid editor automation, image-recognition-first QA, or a full UI framework.

## First Milestone

Focus v0.1 on protocol schemas, C ABI core, C++ wrapper, ImGui adapter, C++ and
C# testing helpers, a minimal example, UI tree dump, and click event queue.
