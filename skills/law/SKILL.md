---
name: law
description: Verify, rebuild, or configure the law plugin's PreToolUse guards — comment policy, workaround ledger, scoped search, safe deletions, edit-tools-only, no polling loops, short foreground commands. Use when the user runs /law, asks "is the comment guard running", "why was this refused", "rebuild the guards", or wants to switch a law off.
---

# law — enforced-law guards

One compiled guard, `${CLAUDE_PLUGIN_DATA}/bin/law.exe` (that name on every OS), runs on every `Bash`, `PowerShell`, `Write`, `Edit` and `MultiEdit` call and routes it to the guards that apply. A denial names its `[RULE-slug]`; the slugs are explained in `${CLAUDE_PLUGIN_ROOT}/docs/claude-md-snippet.md`.

## How it is built

The plugin ships C# source, not binaries. At `SessionStart`, `dotnet run ${CLAUDE_PLUGIN_ROOT}/build.cs` hashes `law.cs` and `src/*.cs`; when `bin/.hash` does not match, it publishes the guard with Native AOT, falls back to a framework-dependent build if AOT fails (Windows without the Visual Studio C++ tools), and swaps the result into `bin/`. Requires the .NET 10 SDK.

The guard hook sets `onFailure: block`: while `law.exe` is missing, every guarded call is refused. Read `${CLAUDE_PLUGIN_DATA}/build.log`.

## Verify it runs

```bash
printf '%s' '{"tool_name":"Bash","tool_input":{"command":"rg -n pattern ~/"},"cwd":"."}' | "${CLAUDE_PLUGIN_DATA}/bin/law.exe"
```

A working guard returns a `deny` whose reason starts with `[RULE-scoped-search]`. Silence means the payload broke no law; an `allow` means routing is wrong. An auto-allowed removal (`rm __probe.ts`) returns `allow` with no reason: only `deny` and `ask` carry text.

## Rebuild

Delete `${CLAUDE_PLUGIN_DATA}/bin/.hash` and restart the session, or run the builder by hand:

```bash
dotnet run "${CLAUDE_PLUGIN_ROOT}/build.cs" -- "${CLAUDE_PLUGIN_ROOT}" "${CLAUDE_PLUGIN_DATA}"
```

## Configure

- **Switch a law off**: set the plugin option `disabled_laws` in `/config` to a comma-separated list of `bash`, `comment`, `workaround`, `edit-tools`, `polling-loop`, `short-foreground`.
- **Allow a script**: add its absolute path to `~/.config/claude-law/allow.list`, one per line.
- **Control root** for the orchestrator rules (`RULE-item-folder-owned`, `RULE-worktree-rm`): `BACKLOG_WORK_ROOT`, default `~/Develop/Work`; worktrees live in its `.worktrees/`.
- **Refused-deletion ledger**: `~/.local/state/claude-law/refused-deletions.md`, override with `LAW_REFUSED_DELETIONS_LEDGER`.
