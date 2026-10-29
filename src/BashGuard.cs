using System.Diagnostics;
using System.Text.RegularExpressions;

internal sealed partial class BashGuard : Law
{
    public override string Name
    {
        get { return "bash"; }
    }

    public override IReadOnlyList<string> Tools
    {
        get { return ["Bash", "PowerShell"]; }
    }

    private static readonly string[] SearchTools =
    [
        "find", "grep", "rg", "fd", "ag", "ack",
        "get-childitem", "gci", "ls", "dir", "select-string", "sls",
    ];

    private static readonly string[] SearchWrappers =
        ["sudo", "env", "nohup", "time", "xargs", "command"];

    private static readonly string[] RootPaths =
    [
        "/", "~", "~/", "$HOME", "$HOME/", "$env:USERPROFILE", "$env:USERPROFILE\\",
        "/Users", "/Users/", "/home", "/home/", "/System", "/System/",
        "/Library", "/Library/", "/usr", "/usr/", "/var", "/var/",
        "/opt", "/opt/", "/private", "/private/",
    ];

    private static readonly string[] PathishWrappers =
    [
        "sudo", "env", "nohup", "time", "command", "xargs",
        "bash", "sh", "zsh", "node", "bun",
        "powershell", "pwsh", "powershell.exe", "pwsh.exe",
    ];

    private static readonly string[] ShellOperators =
        ["&&", "||", ";", "|", "&", ">", ">>", "<"];

    private const string PsRemoveVerbs =
        "Remove-Item|Remove-ItemProperty|ri|rd|rmdir|del|erase|Clear-Content|Clear-Item";

    [GeneratedRegex("""
        ^ (?: \\\\\?\\ )?   # optional Windows extended-length prefix
          [A-Za-z] :        # drive letter
          [\\/]? \z         # optional trailing separator, then end
        """, RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex DriveRoot();

    [GeneratedRegex(@"[;&|\n]+|\$\(|`")]
    private static partial Regex SegmentSplit();

    [GeneratedRegex(@";|\n|&&|\|\||\||&")]
    private static partial Regex CommandSplit();

    [GeneratedRegex(@"^(?:\d*&?>{1,2}|\d*<)")]
    private static partial Regex RedirectPrefix();

    [GeneratedRegex(@"claude-\d+[/\\][^\s;|&""']*[/\\]scratchpad\b")]
    private static partial Regex ScratchpadPath();

    [GeneratedRegex(@"\$(?!HOME\b)(?!TMPDIR\b)(?!env:USERPROFILE\b)", RegexOptions.IgnoreCase)]
    private static partial Regex UnexpandedVar();

    [GeneratedRegex(@"\bfind\b[^\n]*?-delete\b")]
    private static partial Regex FindDelete();

    [GeneratedRegex("""
        (?: ^                                            # start of command, or
          | [;&|\n(`]                                    # a shell separator, or
          | \b(?: xargs | env | sudo | nohup | time )\s+ # a wrapper that runs it, or
          | \b[A-Za-z_][A-Za-z0-9_]*=\S*\s+              # an inline env assignment
        )
        \s* rm \b
        """, RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex RmLead();

    [GeneratedRegex($@"(^|[;&|\n(`{{])\s*(?:{PsRemoveVerbs})\b", RegexOptions.IgnoreCase)]
    private static partial Regex PsRemoveLead();

    [GeneratedRegex($@"\|\s*(?:{PsRemoveVerbs})\b", RegexOptions.IgnoreCase)]
    private static partial Regex PsRemovePipe();

    private static readonly string ControlRoot = Path.GetFullPath(Path.Combine(
        Environment.GetEnvironmentVariable("BACKLOG_WORK_ROOT") is { Length: > 0 } workRoot
            ? Paths.FromMsys(workRoot)
            : Path.Combine(Env.Home, "Develop", "Work"),
        ".worktrees"));

    private static readonly string TempDir = Paths.Slashed(Fs.Canonical(Env.TempDir));

    private static readonly string[] ExemptRoots =
    [
        Paths.Slashed(Fs.Canonical(Path.Combine(Env.Home, ".claude"))),
        Paths.Slashed(Fs.Canonical(Paths.FromMsys("/tmp"))),
        TempDir,
    ];

    protected override GuardResult Evaluate(ToolInput? input, string cwd)
    {
        var command = input?.Command ?? "";

        var sweepRoot = GlobalSearchRoot(command);
        if (sweepRoot.Length > 0)
        {
            return Decide("deny", "RULE-scoped-search",
                $@"This call breaks a law.

Search law: this searches {sweepRoot} — a filesystem-wide sweep. You don't want to do this: it walks millions of files, thrashes the disk, and takes minutes.

Scope the search to a concrete directory (the repo, a specific SDK dir), or ask the package manager instead — e.g. 'dotnet nuget locals global-packages --list' then search that one path. Widen one directory at a time if the scoped search misses.");
        }

        if (ScratchpadTarget(command, cwd))
        {
            return Decide("deny", "RULE-scratch-temp",
                $@"This call breaks a law.

Scratch-location law: the harness's per-session scratchpad directory is never written to. Throwaway scripts, probe files and full command output belong in the OS temp directory — on this machine that is '{TempDir}'.

Re-issue the command against '{TempDir}/<name>' and it will run.");
        }

        var destructive = DestructiveGit(command);
        if (destructive.Length > 0)
        {
            return Decide("deny", "RULE-outright-denials",
                $@"This call breaks a law.

Destructive-git law: '{destructive}' discards working-tree state that exists nowhere else — uncommitted edits, untracked files, or a tree git already considers unsafe to delete. It is refused outright, not prompted.

Undo by editing the files back to what you want, or commit first and revert the commit. To drop a worktree, use 'git worktree remove <tree>' without --force.

If you were SEARCHING for this text rather than running it, this is a false positive: the command is split on '|' without regard for quoting, so an alternation pattern like 'foo|git clean|bar' becomes a segment that reads as the real command. The guard cannot tell the two apart, and it fails closed on purpose. Use the Grep tool for the search instead of a shell 'rg', or drop the alternation and search one term at a time.");
        }

        var escaped = OutsideLaunchFolder(command, cwd);
        if (escaped.Count > 0)
        {
            var launch = LaunchFolder();
            var where = launch.Length > 0
                ? $"the folder this session was launched in, '{launch}'"
                : "the folder this session was launched in — which is unknown here, because the hook received no CLAUDE_PROJECT_DIR";
            var recorded = RefusedDeletions.Record(launch, cwd, escaped, command)
                ? $"It has been recorded for the user in '{RefusedDeletions.Path}'."
                : $"Recording it for the user in '{RefusedDeletions.Path}' failed, so the only record is your final report.";
            var paths = string.Join(", ", escaped.Select(p => $"'{p}'"));
            return Decide("deny", "RULE-dedicated-rm",
                $@"This call breaks a law.

Dedicated-removal law: this removal reaches {paths}, outside {where}. A session removes only what lies inside its own launch folder, or inside '/tmp', '{TempDir}' ($TMPDIR) and '~/.claude' — never anything elsewhere on the machine, whatever git holds. This deletion is forbidden. {recorded}

Never retry it, and never retry any rewording, relocation or split of it; do not delete it some other way either. Leave the path as it is, continue your work, and mention the refused deletion in your final report.");
        }

        var worktreeTarget = SafeWorktreeRemoval(command);
        if (worktreeTarget.Length > 0)
        {
            return Allow();
        }

        var namedRoot = RemovedSafeRoot(command, cwd);
        if (namedRoot.Length > 0)
        {
            return Decide("deny", "RULE-dedicated-rm",
                $@"This call breaks a law.

Dedicated-removal law: this removal names the safe root '{namedRoot}' itself. Paths inside '/tmp', '{TempDir}' ($TMPDIR) and '~/.claude' are removable without a prompt; the roots are not — '~/.claude' holds every law, skill and hook on this machine, and the temp roots hold other sessions' output.

Re-issue it as one bare 'rm' naming the specific paths inside the root that you created. If the root itself really has to be emptied, stop and escalate to the user instead.");
        }

        var exemptRoot = ExemptTargetRoot(command, cwd);
        if (exemptRoot.Length > 0)
        {
            return Allow();
        }

        if (BareRemovalInExemptRoot(command, cwd))
        {
            return Allow();
        }

        if (IsProbeCleanup(command))
        {
            return Allow();
        }

        var itemDirectory = ControlRootItemRemoval(command, cwd);
        if (itemDirectory.Length > 0)
        {
            return Allow();
        }

        if (OrchestratorProofRemoval(command, cwd))
        {
            return Allow();
        }

        var orchestratorFile = OrchestratorTarget(command, cwd);
        if (orchestratorFile.Length > 0)
        {
            return Decide("deny", "RULE-item-folder-owned",
                $@"This call breaks a law.

Item-folder law: '{orchestratorFile}' is inside an item's .orchestrator folder, which the 'backlog' script owns. Its files are deliberately never committed, so there is no 'git add' that would make this removable — that escape does not apply here.

Agents append to progress.md, problems.md and testplan.md and never delete them; the whole folder is relocated by 'backlog close', which archives it, or discarded by 'backlog delete <id> --yes'.");
        }

        var held = GitHoldsCopy(command, cwd);
        if (held is not null && held.Unheld.Count == 0)
        {
            return Allow();
        }

        if (held is not null)
        {
            var sample = string.Join("\n", held.Unheld.Take(8).Select(l => "    " + l));
            var more = held.Unheld.Count > 8 ? $"\n    ... and {held.Unheld.Count - 8} more" : "";
            return Decide("deny", "RULE-worktree-rm",
                $@"This call breaks a law.

Precise-deletion law: git holds no copy of this content, so removing it destroys it outright:
{sample}{more}

This is a refusal, not a prompt — an unattended agent has nobody to answer a dialog.

Put the content in git first ('git -C {held.Root} add <paths>' — staging is enough, no commit needed), then remove it. To relocate a file rather than delete it, use 'git mv', which is never gated.");
        }

        if (InvokesRm(command))
        {
            var allowedShapes =
                $"every path inside '/tmp', '{TempDir}' ($TMPDIR) or '~/.claude'; or, for targets inside this session's launch folder, every target a '__' probe file, one item directory under the control root, one 'git worktree remove' without --force, or content git already holds";

            if (Js.HasShellControl(command) || Js.HasGlob(command) || FindDelete().IsMatch(command))
            {
                return Decide("deny", "RULE-dedicated-rm",
                    $@"This call breaks a law.

Dedicated-removal law: a removal is its own command — one bare 'rm' with explicit paths, and nothing else on the line. This one chains, redirects, globs, substitutes or computes its targets at runtime, so what it will actually delete cannot be read off it.

Re-issue it as one bare 'rm' naming every path explicitly, with nothing else on the line, and check the result in a separate command. On its own it runs without a prompt when it is one of: {allowedShapes}.

If you were SEARCHING for text such as '(rm|...)' rather than deleting, this is a false positive: 'rm' after '(', '|', ';' or '&' reads as a removal regardless of quoting. Use the Grep tool, or reorder the alternation so 'rm' does not follow one of those characters.");
            }

            return Decide("deny", "RULE-dedicated-rm",
                $@"This call breaks a law.

Dedicated-removal law: this removal's target lies outside every safe root and git holds no copy of it, so deleting it would destroy it outright. Removals run without a prompt only when they are one of: {allowedShapes}.

If the path is in a git repository, 'git add' it first (staging is enough, no commit needed) and re-issue the 'rm' alone. Otherwise do not delete it: stop and escalate to the user, naming the path and why it should go.");
        }

        var scriptPath = Allowlist.LeadingScriptPath(command, cwd);
        if (scriptPath.Length > 0 && Allowlist.AllowedScripts().Contains(scriptPath))
        {
            return Allow();
        }

        return Decide("pass", "", "");
    }

    private static string LaunchFolder()
    {
        var raw = Paths.FromMsys(Environment.GetEnvironmentVariable("CLAUDE_PROJECT_DIR") ?? "");
        if (raw.Length == 0 || !Path.IsPathRooted(raw))
        {
            return "";
        }
        return Paths.Slashed(Fs.Canonical(raw));
    }

    private static List<string> OutsideLaunchFolder(string command, string cwd)
    {
        var text = NormalizeTempVar(command ?? "");
        var targets = new List<string>();
        var unresolvable = new List<string>();

        var worktreeTarget = SafeWorktreeRemoval(text);
        if (worktreeTarget.Length > 0)
        {
            var baseDir = cwd;
            var tokens = SegmentTokens(text.Trim());
            var c = tokens.IndexOf("-C");
            if (c > 0 && c + 1 < tokens.Count && tokens.IndexOf("worktree") > c)
            {
                var dir = Paths.Expand(tokens[c + 1]);
                if (Js.HasGlobOrVar(dir))
                {
                    return [tokens[c + 1]];
                }
                baseDir = Paths.Absolute(cwd, dir);
            }
            var expanded = Paths.Expand(worktreeTarget);
            if (Js.HasGlobOrVar(expanded))
            {
                return [worktreeTarget];
            }
            targets.Add(Paths.Absolute(baseDir, expanded));
        }
        else
        {
            if (!InvokesRm(text) || Js.HasShellControl(text) || Js.HasGlob(text))
            {
                return [];
            }
            foreach (var token in BareRemovalTargets(text) ?? [])
            {
                var expanded = Paths.Expand(token);
                if (Js.HasGlobOrVar(expanded) || Paths.HasParentStep(expanded))
                {
                    unresolvable.Add(token);
                    continue;
                }
                targets.Add(Paths.Absolute(cwd, expanded));
            }
        }

        var launch = LaunchFolder();
        var escaped = targets
            .Where(abs => ExemptRootFor(abs).Length == 0)
            .Where(abs => launch.Length == 0 || Paths.Same(abs, launch) || !Paths.IsUnder(abs, launch))
            .Where(abs => !InsideLaunchProjectWorktree(abs, launch))
            .ToList();
        return [.. unresolvable, .. escaped];
    }

    private static bool InsideLaunchProjectWorktree(string abs, string launch)
    {
        if (launch.Length == 0)
        {
            return false;
        }

        var root = Paths.Slashed(Fs.Canonical(ControlRoot));
        if (!Paths.IsUnder(abs, root) || Paths.IsUnder(launch, root))
        {
            return false;
        }

        var rel = abs[(root.Length + 1)..].Split('/');
        var project = launch.TrimEnd('/').Split('/')[^1];
        return rel.Length > 2
            && rel[0] is not ("backlog" or "archive")
            && string.Equals(rel[0], project, StringComparison.OrdinalIgnoreCase);
    }

    private static bool OrchestratorProofRemoval(string command, string cwd)
    {
        var text = command ?? "";
        if (!InvokesRm(text) || Js.HasShellControl(text) || Js.HasGlob(text))
        {
            return false;
        }

        var targets = BareRemovalTargets(text);
        if (targets is null)
        {
            return false;
        }

        foreach (var token in targets)
        {
            var expanded = Paths.Expand(token);
            if (Js.HasGlobOrVar(expanded) || Paths.HasParentStep(expanded))
            {
                return false;
            }
            var parts = Paths.Absolute(cwd, expanded).Split('/', '\\');
            var i = Array.IndexOf(parts, ".orchestrator");
            if (i == -1 || i + 2 >= parts.Length || parts[i + 1] is not ("screenshots" or "baselines"))
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsHomeRoot(string token)
    {
        var t = token.ToLowerInvariant();
        return token is "~" or "~/" or "~\\" or "$HOME" or "$HOME/"
            || t is "$env:userprofile" or "$env:userprofile\\" or "$env:userprofile/";
    }

    private static string GlobalSearchRoot(string command)
    {
        foreach (var segment in SegmentSplit().Split(command ?? ""))
        {
            var tokens = Js.Words(segment.Trim());
            if (tokens.Length == 0)
            {
                continue;
            }

            var i = 0;
            while (i < tokens.Length
                   && (Js.IsEnvAssignmentLead(tokens[i]) || SearchWrappers.Contains(tokens[i])))
            {
                i++;
            }

            var lead = i < tokens.Length ? tokens[i] : "";
            var tool = Js.TrimExe(Js.LastSegment(lead).ToLowerInvariant());
            if (!SearchTools.Contains(tool))
            {
                continue;
            }

            foreach (var rawArg in tokens.Skip(i + 1))
            {
                var arg = Js.StripQuotes(rawArg);
                if (arg.StartsWith('-'))
                {
                    continue;
                }
                if (IsHomeRoot(arg))
                {
                    return "the home directory";
                }
                if (DriveRoot().IsMatch(arg))
                {
                    return $"the {arg.TrimEnd('\\', '/')} drive root";
                }
                if (RootPaths.Contains(arg))
                {
                    return arg;
                }
            }
        }
        return "";
    }

    private static string NormalizeTempVar(string text)
    {
        return text.Replace("${TMPDIR}", "$TMPDIR", StringComparison.Ordinal);
    }

    private static string ExemptRootFor(string absPath)
    {
        return ExemptRoots.FirstOrDefault(root => Paths.IsUnder(absPath, root)) ?? "";
    }

    private static bool IsPathish(string token)
    {
        if (token.Length == 0 || token.StartsWith('-'))
        {
            return false;
        }
        if (ShellOperators.Contains(token))
        {
            return false;
        }
        if (Js.IsEnvAssignmentLead(token))
        {
            return false;
        }
        return token.Contains('/')
            || token.Contains('\\')
            || Js.IsDriveLetter(token)
            || token.StartsWith('~')
            || token.StartsWith("$HOME")
            || token.StartsWith("$env:", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string>? PathTargets(string command, string cwd)
    {
        var text = NormalizeTempVar(command ?? "");
        if (Js.HasSubstitution(text))
        {
            return null;
        }

        var results = new List<string>();
        foreach (var rawToken in Js.Words(text))
        {
            var token = RedirectPrefix().Replace(Js.StripQuotes(rawToken), "");
            if (PathishWrappers.Contains(token))
            {
                continue;
            }
            if (!IsPathish(token))
            {
                continue;
            }
            if (UnexpandedVar().IsMatch(token))
            {
                return null;
            }

            var expanded = Paths.Expand(token);
            if (Paths.HasParentStep(expanded))
            {
                return null;
            }

            results.Add(Paths.Absolute(cwd, expanded));
        }
        return results;
    }

    private static string ExemptTargetRoot(string command, string cwd)
    {
        var targets = PathTargets(command, cwd);
        if (targets is null || targets.Count == 0)
        {
            return "";
        }

        var matchedRoot = "";
        foreach (var abs in targets)
        {
            var root = ExemptRootFor(abs);
            if (root.Length == 0)
            {
                return "";
            }
            matchedRoot = root;
        }
        return matchedRoot;
    }

    private static string RemovedSafeRoot(string command, string cwd)
    {
        if (!InvokesRm(command ?? ""))
        {
            return "";
        }

        var candidates = new List<string>(PathTargets(command ?? "", cwd) ?? []);
        foreach (var token in BareRemovalTargets(NormalizeTempVar(command ?? "")) ?? [])
        {
            var expanded = Paths.Expand(token);
            if (Js.HasGlobOrVar(expanded))
            {
                continue;
            }
            candidates.Add(Paths.Absolute(cwd, expanded));
        }

        foreach (var abs in candidates)
        {
            var root = ExemptRoots.FirstOrDefault(r => Paths.Same(r, abs));
            if (root is not null)
            {
                return root;
            }
        }
        return "";
    }

    private static bool BareRemovalInExemptRoot(string command, string cwd)
    {
        var targets = BareRemovalTargets(NormalizeTempVar(command ?? ""));
        if (targets is null)
        {
            return false;
        }

        foreach (var token in targets)
        {
            var expanded = Paths.Expand(token);
            if (Js.HasGlobOrVar(expanded) || Paths.HasParentStep(expanded))
            {
                return false;
            }

            var abs = Paths.Absolute(cwd, expanded);
            var root = ExemptRootFor(abs);
            if (root.Length == 0 || Paths.Same(abs, root))
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsSessionScratchpad(string absPath)
    {
        var parts = absPath.Split('/');
        var i = Array.FindIndex(parts, Js.IsClaudeSession);
        return i != -1 && parts.Skip(i + 1).Contains("scratchpad");
    }

    private static bool ScratchpadTarget(string command, string cwd)
    {
        var text = command ?? "";
        if (ScratchpadPath().IsMatch(text))
        {
            return true;
        }

        var targets = PathTargets(text, cwd);
        return targets is not null && targets.Any(IsSessionScratchpad);
    }

    private static string SafeWorktreeRemoval(string command)
    {
        var text = command ?? "";
        if (Js.HasSubstitution(text))
        {
            return "";
        }

        var segments = CommandSegments(text).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        if (segments.Count != 1)
        {
            return "";
        }

        var git = GitInvocation(SegmentTokens(segments[0]));
        if (git is null || git.Sub != "worktree")
        {
            return "";
        }
        if (git.Args.Count == 0 || git.Args[0] != "remove")
        {
            return "";
        }

        var rest = git.Args.Skip(1).ToList();
        if (rest.Any(a => a is "--force" or "-f"))
        {
            return "";
        }

        var targets = rest.Where(a => !a.StartsWith('-')).ToList();
        if (targets.Count != 1)
        {
            return "";
        }
        return targets[0];
    }

    private static List<string>? BareRemovalTargets(string command)
    {
        var text = command ?? "";
        if (Js.HasShellControl(text))
        {
            return null;
        }

        var tokens = Js.Words(text.Trim()).ToList();
        while (tokens.Count > 1 && Js.IsEnvAssignmentLead(tokens[0]))
        {
            tokens.RemoveAt(0);
        }

        if (tokens.Count == 0)
        {
            return null;
        }

        var lead = Js.TrimExe(Js.LastSegment(tokens[0]).ToLowerInvariant());
        if (lead != "rm")
        {
            return null;
        }

        var targets = tokens.Skip(1).Where(t => !t.StartsWith('-')).Select(Js.StripQuotes).ToList();
        return targets.Count > 0 ? targets : null;
    }

    private static string ControlRootItemRemoval(string command, string cwd)
    {
        var targets = BareRemovalTargets(command);
        if (targets is null || targets.Count != 1)
        {
            return "";
        }

        var token = targets[0];
        if (Js.HasGlobOrVar(token))
        {
            return "";
        }

        var expanded = Paths.Expand(token);
        if (Paths.HasParentStep(expanded))
        {
            return "";
        }

        var abs = Paths.Absolute(cwd, expanded);
        var root = Paths.Slashed(Fs.Canonical(ControlRoot));
        if (!Paths.IsUnder(abs, root) || Paths.Same(abs, root))
        {
            return "";
        }

        var rel = abs[(root.Length + 1)..].Split('/');
        if (rel[0].StartsWith('.'))
        {
            return "";
        }

        var underHome = rel[0] is "backlog" or "archive";
        if (rel.Length == 3 && underHome)
        {
            return abs;
        }
        if (rel.Length == 2 && !underHome)
        {
            return abs;
        }
        return "";
    }

    private static bool IsProbeCleanup(string command)
    {
        var targets = BareRemovalTargets(command);
        if (targets is null)
        {
            return false;
        }

        return targets.All(token =>
            !Js.HasGlob(token) && Js.LastSegment(token).StartsWith("__"));
    }

    private static string EnclosingGitRoot(string absPath)
    {
        var dir = absPath;
        for (var hops = 0; hops < 64; hops++)
        {
            if (Fs.Exists(Path.Combine(dir, ".git")))
            {
                return Paths.Slashed(dir);
            }

            var parent = Path.GetDirectoryName(dir);
            if (string.IsNullOrEmpty(parent) || parent == dir)
            {
                return "";
            }
            dir = parent;
        }
        return "";
    }

    private static string SingleRemovalTarget(string command, string cwd)
    {
        var targets = BareRemovalTargets(command);
        if (targets is null || targets.Count != 1)
        {
            return "";
        }

        var token = targets[0];
        if (Js.HasGlobOrVar(token))
        {
            return "";
        }

        var expanded = Paths.Expand(token);
        if (Paths.HasParentStep(expanded))
        {
            return "";
        }

        return Paths.Absolute(cwd, expanded);
    }

    private static string OrchestratorTarget(string command, string cwd)
    {
        var abs = SingleRemovalTarget(command, cwd);
        if (abs.Length == 0)
        {
            return "";
        }

        var parts = abs.Split('/', '\\');
        var i = Array.IndexOf(parts, ".orchestrator");
        if (i != -1 && i < parts.Length - 1)
        {
            return abs;
        }
        return "";
    }

    private sealed record Held(string Path, string Root, List<string> Unheld);

    private static Held? GitHoldsCopy(string command, string cwd)
    {
        var abs = SingleRemovalTarget(command, cwd);
        if (abs.Length == 0)
        {
            return null;
        }

        var root = EnclosingGitRoot(abs);
        if (root.Length == 0)
        {
            return null;
        }

        if (Paths.Same(abs, root) || abs[root.Length..].Split('/', '\\').Contains(".git"))
        {
            return new Held(abs, root, [$"{abs}  (the repository itself or its .git — git's own copy lives there)"]);
        }

        string status;
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("-C");
            psi.ArgumentList.Add(root);
            psi.ArgumentList.Add("status");
            psi.ArgumentList.Add("--porcelain");
            psi.ArgumentList.Add("--ignored");
            psi.ArgumentList.Add("--");
            psi.ArgumentList.Add(abs);

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return null;
            }

            status = proc.StandardOutput.ReadToEnd();
            if (!proc.WaitForExit(5000))
            {
                return null;
            }
            if (proc.ExitCode != 0)
            {
                return null;
            }
        }
        catch
        {
            return null;
        }

        var entries = status.Split('\n').Where(l => l.Length > 0).ToList();
        var unheld = entries.Where(l => Js.At(l, 1) != ' ').ToList();
        return new Held(abs, root, unheld);
    }

    private static bool InvokesRm(string command)
    {
        if (RmLead().IsMatch(command))
        {
            return true;
        }
        if (FindDelete().IsMatch(command))
        {
            return true;
        }
        if (PsRemoveLead().IsMatch(command))
        {
            return true;
        }
        if (PsRemovePipe().IsMatch(command))
        {
            return true;
        }
        return false;
    }

    private static string[] CommandSegments(string command)
    {
        return CommandSplit().Split(command ?? "");
    }

    private static List<string> SegmentTokens(string segment)
    {
        var tokens = Js.Words(segment.Trim()).Select(Js.StripQuotes).ToList();
        while (tokens.Count > 0
               && (Js.IsEnvAssignmentLead(tokens[0]) || SearchWrappers.Contains(tokens[0])))
        {
            tokens.RemoveAt(0);
        }
        return tokens;
    }

    private sealed record Git(string Sub, List<string> Args);

    private static Git? GitInvocation(List<string> tokens)
    {
        if (tokens.Count == 0)
        {
            return null;
        }

        var lead = Js.TrimExe(Js.LastSegment(tokens[0]).ToLowerInvariant());
        if (lead != "git")
        {
            return null;
        }

        var i = 1;
        while (i < tokens.Count)
        {
            var token = tokens[i];
            if (token is "-C" or "-c")
            {
                i += 2;
                continue;
            }
            if (token.StartsWith('-'))
            {
                i += 1;
                continue;
            }
            break;
        }
        return new Git(i < tokens.Count ? tokens[i] : "", tokens.Skip(i + 1).ToList());
    }

    private static string DestructiveGit(string command)
    {
        foreach (var segment in CommandSegments(command))
        {
            var git = GitInvocation(SegmentTokens(segment));
            if (git is null)
            {
                continue;
            }

            var sub = git.Sub;
            var args = git.Args;

            if (sub == "reset" && args.Contains("--hard"))
            {
                return "git reset --hard";
            }
            if (sub == "clean")
            {
                return "git clean";
            }
            if (sub == "checkout" && args.Contains("--"))
            {
                return "git checkout -- <paths>";
            }
            if (sub == "restore" && (!args.Contains("--staged") || args.Contains("--worktree")))
            {
                return "git restore";
            }
            if (sub == "worktree"
                && args.Count > 0
                && args[0] == "remove"
                && args.Any(a => a is "--force" or "-f"))
            {
                return "git worktree remove --force";
            }
        }
        return "";
    }
}
