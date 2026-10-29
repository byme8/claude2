# CLAUDE.md snippet

Every denial the law guards emit names a `[RULE-slug]`. Paste the block below into `~/.claude/CLAUDE.md` so the agent knows each rule before it is refused, not only after. Drop the lines for any law switched off with `disabled_laws`.

```markdown
## Enforced laws

PreToolUse guards from the `law` plugin enforce the rules below; a rejection is tagged with its rule slug and carries the remedy.

- RULE-no-guard-evasion: The rules bind whether or not the hook catches a given case; evading a guard by rewording, relocating, or splitting the refused action is itself the violation.
- RULE-comment-policy: Code carries the problem and the design through names, types and structure, so a comment is only one of these tags — no why-comments, narration, restatements, section dividers or banners, docstrings included: `WORKAROUND:` (which needs a prior `workarounds.md` entry) `PERF:` `BUG:` `FIXME:` `TODO:` `DEPRECATED:`, plus `TEST_POINT:` on a test case for why it exists and `SAFETY:` above a Rust `unsafe` block or impl for the invariant that makes it sound.
- RULE-workaround-ledger: A `WORKAROUND:` comment needs an entry in the project's `workarounds.md` first — path, verbatim comment text, why it is unavoidable, and the correct fix once the external cause is gone.
- RULE-edit-tools-only: Edit project files only with the Edit or Write tools, never a shell redirect, `tee`, `sed -i`, `perl -i` or `truncate`; output you only want to inspect goes to the OS temp directory.
- RULE-scoped-search: Search only within a concrete directory, never a filesystem-wide sweep of `/`, `~` or `/Users`.
- RULE-scratch-temp: Send throwaway scripts, probe files and command output to the OS temp directory, never the harness's session scratchpad.
- RULE-outright-denials: Never run git commands that discard working-tree state: `git clean`, `git reset --hard`, `git checkout -- <paths>`, `git restore <paths>`.
- RULE-dedicated-rm: A removal is its own command — one bare `rm` with explicit paths, nothing chained, globbed or computed — and reaches only the session's launch folder, `/tmp`, `$TMPDIR` or `~/.claude`, never those roots themselves.
- RULE-worktree-rm: Never delete content git holds no copy of; a worktree is removed with `git worktree remove` without `--force`.
- RULE-item-folder-owned: Never remove files from an item's `.orchestrator` folder; the backlog tooling owns it.
- RULE-no-polling-loop: Never wait in a shell polling loop (`while`/`until`/`for` with `sleep`, `watch`); wait on an event — a `run_in_background` completion notification, Monitor with an until-condition, or CronCreate / ScheduleWakeup.
- RULE-short-foreground: A foreground command's timeout is at most 60 s; longer work runs with `run_in_background`.
- RULE-guard-fail-closed: When a guard cannot parse the call or throws, the call is refused; fix the guard, do not route around it.
```
