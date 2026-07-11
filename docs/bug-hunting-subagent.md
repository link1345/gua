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
