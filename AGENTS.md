# Repository Instructions

## Project

Gua is a runtime UI automation protocol for games. Its center is the protocol,
not any single language implementation.

The initial implementation should stay focused on:

- Semantic UI tree schema
- Command and event schemas
- Stable C ABI runtime core
- Thin C++ wrapper over the C ABI
- ImGui adapter as the first reference integration
- C++ and C# test ergonomics over the C ABI
- MCP and Inspector as consumers of the protocol

Avoid turning the project into a game engine, editor MCP, image-recognition QA
bot, or full UI framework.

## Architecture Rules

- Treat `protocol/` as the source of truth for cross-language behavior.
- Keep externally stable native APIs in C ABI form.
- Put C++ convenience APIs on top of the C ABI.
- For .NET, prefer P/Invoke over a separate runtime implementation.
- Test helpers should be usable from C++ and C# first. Do not make TypeScript the
  primary test API.
- Treat Windows MSVC as the primary native development toolchain.
- Keep the native core portable for Apple Clang and Android NDK Clang by staying
  within the C ABI and standard C++20 unless platform code is isolated.
- Prefer polling/event queues across language boundaries instead of callbacks.
- Add engine integrations as adapters after the core protocol is usable.

## AI Working Notes

- Read `README.md` and `protocol/specs/protocol.md` before broad design work.
- Keep generated output, dependency folders, and local build artifacts out of git.
- When changing schema behavior, update protocol docs and affected clients together.
- Preserve the monorepo shape unless the user explicitly asks to split packages.
