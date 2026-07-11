# Gua defect audit matrix

Select only lanes touched by the requested component or diff.

| Lane | Contract and implementation | High-signal failure classes |
| --- | --- | --- |
| Protocol/schema | `protocol/specs`, `protocol/schema`, fixtures | undocumented payload drift, invalid optionality, legacy incompatibility |
| Core C ABI | `native/gua-core/include`, `native/gua-core/src` | ABI size/version mistakes, staging-frame leaks, queue corruption, lock gaps |
| Runtime/bridge | `native/gua-runtime`, `native/gua-ws-bridge` | lifetime bugs, stale epoch acceptance, response correlation, disconnect races |
| C++ helpers | `native/gua-testing` | first-match ambiguity, stale snapshots, timeout and action-completion errors |
| .NET bindings | `bindings/dotnet/src` | P/Invoke layout mismatch, ownership/disposal, exception masking, reset leakage |
| ImGui adapter | `native/gua-imgui`, `examples/cpp-imgui` | unstable IDs, missing host events, incorrect visible/enabled/action state |
| Godot adapters | `native/gua-godot`, `examples/dotnet-godot`, `examples/godot-gdscript` | tree reflection drift, signal duplication, stale DLL/API mismatch, scene teardown |
| MCP | `packages/mcp` | tool/schema mismatch, unsupported command claims, timeouts, lost bridge errors |
| Inspector | `packages/inspector` | stale push/poll ordering, incorrect node state rendering, reconnect state leaks |
| Visual/recording | `Gua.Testing.Visual`, screenshot and recording schemas | implicit resize, mask denominator errors, secret retention, nondeterministic replay |

## Cross-boundary invariants

- A published frame is atomic; readers never observe staging data.
- `sessionEpoch`, `frameSequence`, and `revision` have distinct meanings.
- Action success follows `enqueue -> consume -> host action -> observed event`.
- Request IDs correlate results without consuming unrelated events.
- Unknown state stays omitted; observed false is not the same as unsupported.
- Sensitive action values never enter logs, diagnostics, history, or recordings.
- Strict reset is all-or-nothing and stale remote epochs cannot mutate state.
- Adapter-owned reflection follows the host UI rather than manual semantic
  restatement.
- MCP and Inspector do not own runtime state.

## Bounded verification menu

Choose the smallest commands that exercise the suspected defect:

```powershell
bun run check
cmake --preset windows-msvc-debug
cmake --build --preset windows-msvc-debug --target gua
ctest --test-dir build/windows-msvc-debug --output-on-failure
dotnet test bindings/dotnet/tests/Gua.Selector.Tests/Gua.Selector.Tests.csproj
dotnet test bindings/dotnet/tests/Gua.Visual.Tests/Gua.Visual.Tests.csproj
```

For adapter defects, prefer the relevant existing sample smoke path. Do not
launch every engine or package when a narrower target proves or disproves the
hypothesis.
