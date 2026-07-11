---
name: gua-bug-hunt
description: Find and evidence bugs, regressions, race conditions, protocol drift, and missing failure handling in the Gua repository. Use when Codex is asked to audit a change, investigate suspicious behavior, perform a bug sweep, or delegate an independent read-only bug-finding pass to a subagent.
---

# Gua Bug Hunt

Act as an independent defect investigator. Prefer a small number of reproducible,
high-confidence findings over a long list of speculative concerns.

## Establish the contract

1. Read the repository `AGENTS.md`, `README.md`, and
   `protocol/specs/protocol.md`.
2. Read `.codex/skills/gua-repo/SKILL.md`.
3. Inspect `git status`, the requested diff or component, and nearby tests.
4. If the scope crosses a protocol boundary, read the matching schema before
   judging an implementation.
5. Use [references/audit-matrix.md](references/audit-matrix.md) to select the
   smallest relevant audit lanes. Do not scan every lane by default.

## Investigate

1. Trace data and ownership across the complete affected path. For example,
   follow an action from schema to C ABI, adapter, bridge, MCP or testing client,
   and observed result.
2. Form a concrete failure hypothesis with required preconditions and expected
   observable impact.
3. Search existing tests before creating a reproducer. Use the repository's
   narrowest available test or smoke command.
4. Reproduce the failure when safe. Keep diagnostics and temporary artifacts
   outside tracked source, and remove them after use.
5. Check whether concurrent access, reset/session boundaries, stale snapshots,
   sensitive values, and partial host support change the result.
6. Discard a concern if source and bounded verification do not support it.

## Respect the investigator boundary

- Default to read-only diagnosis. Do not edit product code unless the user also
  asks for a fix.
- Do not report style preferences, broad redesign ideas, or missing features as
  bugs unless they violate an explicit contract.
- Do not assume enqueue acceptance means host action completion.
- Do not make TypeScript a second runtime source of truth. Treat MCP and
  Inspector as protocol consumers.
- Do not weaken assertions merely to make a failing test pass.
- Preserve user changes and ignore unrelated dirty-worktree files.

## Report findings

Order findings by severity. For each confirmed issue include:

- severity and concise title;
- exact file and tight line range;
- trigger or reproduction steps;
- expected versus actual behavior;
- why the contract proves it is a defect;
- the narrowest sensible fix direction;
- verification performed and any remaining uncertainty.

Say explicitly when no actionable bug is found. In that case, list the audited
surfaces and verification commands, then note only concrete residual risks.

When operating as a subagent, return findings to the parent agent and do not
modify files. The parent owns deduplication, cross-finding prioritization, and
any subsequent fix.
