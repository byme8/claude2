# law

A Claude Code plugin: one Native AOT PreToolUse guard that enforces coding-agent laws on `Bash`, `PowerShell`, `Write`, `Edit` and `MultiEdit` calls.

| Guard | Tools | Rules |
|---|---|---|
| `comment` | Write, Edit, MultiEdit | `RULE-comment-policy` |
| `workaround` | Write, Edit, MultiEdit | `RULE-workaround-ledger` |
| `edit-tools` | Bash, PowerShell | `RULE-edit-tools-only` |
| `bash` | Bash, PowerShell | `RULE-scoped-search` `RULE-scratch-temp` `RULE-outright-denials` `RULE-dedicated-rm` `RULE-worktree-rm` `RULE-item-folder-owned` |
| `polling-loop` | Bash | `RULE-no-polling-loop` |
| `short-foreground` | Bash, PowerShell | `RULE-short-foreground` |

Each rule is described in [docs/claude-md-snippet.md](docs/claude-md-snippet.md); paste that block into `~/.claude/CLAUDE.md`.

## Requirements

- Windows, macOS or Linux
- .NET 10 SDK on `PATH`
- Claude Code 2.1.295 or later (`onFailure` on the guard hook)
- Optional on Windows: the Visual Studio C++ build tools, for a Native AOT guard; without them the build falls back to a framework-dependent guard that starts slower

## Install

```bash
claude plugin marketplace add byme8/claude-law
claude plugin install a@claude-law
```

Then restart Claude Code. At session start `build.cs` hashes the guard sources and, when the installed guard is missing or stale, publishes it (Native AOT, falling back to JIT) into `<plugin data>/bin/law.exe` — that name on every OS. The first session waits about a minute; later sessions skip the build until the sources change.

The guard hook sets `onFailure: block`, so while the guard is missing every guarded call is refused. The build log is `~/.claude/plugins/data/<plugin-id>/build.log`.

## Configure

- `disabled_laws` plugin option (`/config`): comma-separated guard names to switch off.
- `~/.config/claude-law/allow.list`: absolute script paths the bash guard lets run, one per line.
- `BACKLOG_WORK_ROOT`: control root for the orchestrator rules, default `~/Develop/Work`.
- `LAW_REFUSED_DELETIONS_LEDGER`: refused-deletion log, default `~/.local/state/claude-law/refused-deletions.md`.

## Develop

```bash
dotnet publish law.cs -o ./bin
dotnet run tests/law.tests.cs
dotnet run build.cs -- . <data-dir>
claude --plugin-dir .
```

The tests drive `bin/law` (override with `LAW_TEST_BINARY`) and write a decision snapshot to `tests/artifacts/`.
