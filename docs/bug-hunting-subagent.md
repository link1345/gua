# Bug-hunting subagent

The repository includes the project-local Codex skill `$gua-bug-hunt` for an
independent, evidence-driven defect pass. It complements `$gua-repo`: the latter
defines architecture rules, while `$gua-bug-hunt` defines investigation and
reporting behavior.

Ask the main agent to delegate a bounded component or diff, for example:

```text
Use $gua-bug-hunt in a subagent to audit packages/mcp for reproducible protocol
and error-handling bugs. Keep the pass read-only and return file/line evidence,
reproduction steps, and verification results.
```

Good delegation scopes are one changed diff, one protocol path, or one adapter.
For cross-language work, divide by independent boundary (for example core ABI,
WebSocket/MCP, and Godot) and let the parent agent deduplicate findings. The
subagent must not edit product files; fixes remain a separate, explicit task.

## Pull-request-wide local audit

The bounded subagent gate runs after ordinary repository edits. Before handing
off a large pull request, use the dedicated cumulative-diff harness from the PR
branch or its worktree:

```powershell
git fetch origin main
./scripts/run-local-pr-audit.ps1 -Base origin/main
```

This runs one dedicated cumulative `codex review` plus three independent,
read-only Gua boundary reviews covering protocol/core, engine adapters, and
protocol consumers. If the worktree is dirty, the dedicated reviewer also runs
the uncommitted-change preset so staged, unstaged, and untracked fixes are not
lost between passes. Reports are written under the system temporary directory
rather than the repository.

To validate findings, apply supported fixes, run focused verification, and
repeat the complete audit until no files change or four passes finish:

```powershell
./scripts/run-local-pr-audit.ps1 -Base origin/main -Fix
```

Use `-Sequential` if the host cannot sustain four concurrent Codex sessions.
The harness never commits, pushes, or posts review comments. Exact parity with
the GitHub Connector is not guaranteed because its private prompt and runtime
configuration are not available locally.
