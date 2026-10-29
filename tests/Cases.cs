using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

internal sealed record TestCase(
    string Name,
    string Payload,
    string Decision,
    string Rule,
    string? Context = null,
    string? HookTempDir = null,
    string? LaunchDir = null);

internal static class Cases
{
    private const string ProjectCwd = "/repo";

    private static string Cwd
    {
        get { return Path.TrimEndingDirectorySeparator(Path.GetTempPath()); }
    }

    private static string Payload(string toolName, Action<Utf8JsonWriter> writeToolInput, string? cwd = null)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer,
            new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("tool_name", toolName);
            w.WritePropertyName("tool_input");
            w.WriteStartObject();
            writeToolInput(w);
            w.WriteEndObject();
            w.WriteString("cwd", cwd ?? Cwd);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static string Bash(string command)
    {
        return Payload("Bash", w => w.WriteString("command", command));
    }

    private static string ProjectBash(string command)
    {
        return Payload("Bash", w => w.WriteString("command", command), ProjectCwd);
    }

    private static string BashIn(string cwd, string command)
    {
        return Payload("Bash", w => w.WriteString("command", command), cwd);
    }

    private static void Git(string dir, params string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in (string[])["-C", dir, "-c", "core.hooksPath=/dev/null", "-c", "commit.gpgsign=false",
                     "-c", "user.name=law", "-c", "user.email=law@example.invalid", .. args])
        {
            psi.ArgumentList.Add(a);
        }
        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.StandardOutput.ReadToEnd();
        proc.StandardError.ReadToEnd();
        proc.WaitForExit(20000);
    }

    private static string FixtureRepo(string tmp)
    {
        var repo = Path.Combine(tmp, "law-fixture-repo");
        Directory.CreateDirectory(repo);
        Git(repo, "init", "-q");
        File.WriteAllText(Path.Combine(repo, ".gitignore"), "ignored.txt\n");
        File.WriteAllText(Path.Combine(repo, "tracked.txt"), "held\n");
        Git(repo, "add", ".gitignore", "tracked.txt");
        Git(repo, "commit", "-q", "-m", "fixture");
        File.WriteAllText(Path.Combine(repo, "untracked.txt"), "not held\n");
        File.WriteAllText(Path.Combine(repo, "ignored.txt"), "not held\n");
        return repo;
    }

    private static string PowerShell(string command)
    {
        return Payload("PowerShell", w => w.WriteString("command", command));
    }

    private static string Timed(string toolName, string command, int timeoutMs, bool? background)
    {
        return Payload(toolName, w =>
        {
            w.WriteString("command", command);
            w.WriteNumber("timeout", timeoutMs);
            if (background is { } b)
            {
                w.WriteBoolean("run_in_background", b);
            }
        });
    }

    private static string Read(string filePath)
    {
        return Payload("Read", w => w.WriteString("file_path", filePath));
    }

    private static string Write(string filePath, string content)
    {
        return Payload("Write", w =>
        {
            w.WriteString("file_path", filePath);
            w.WriteString("content", content);
        });
    }

    private static string Edit(string filePath, string newString)
    {
        return Payload("Edit", w =>
        {
            w.WriteString("file_path", filePath);
            w.WriteString("new_string", newString);
        });
    }

    private static string MultiEdit(string filePath, params string[] newStrings)
    {
        return Payload("MultiEdit", w =>
        {
            w.WriteString("file_path", filePath);
            w.WritePropertyName("edits");
            w.WriteStartArray();
            foreach (var s in newStrings)
            {
                w.WriteStartObject();
                w.WriteString("new_string", s);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        });
    }

    public static readonly string WorkRoot =
        Path.Combine(Path.GetPathRoot(Path.GetTempPath()) ?? "/", "law-fixture-work-root");

    public static IReadOnlyList<TestCase> All { get; } = Build();

    private static List<TestCase> Build()
    {
        var tmp = Path.TrimEndingDirectorySeparator(Path.GetTempPath());
        var privateTmp = tmp.StartsWith("/var/") ? "/private" + tmp : tmp;
        var tmpSlashed = tmp.Replace('\\', '/');
        var repo = FixtureRepo(tmp);
        var hookTmp = Path.Combine(tmp, "law-hook-tmp");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var homeSlashed = home.Replace('\\', '/');
        var homeMsys = home.Length > 1 && home[1] == ':'
            ? "/" + char.ToLowerInvariant(home[0]) + homeSlashed[2..]
            : "/c" + home;
        var otherLaunch = Path.Combine(tmp, "law-other-launch");
        var workRoot = WorkRoot;
        var itemDir = Path.Combine(workRoot, ".worktrees", "realplus", "B99-law-fixture");

        return
        [
            new("GitClean_IsRefused",
                Bash("git clean -fd"), "deny", "RULE-outright-denials"),
            new("GitResetHard_IsRefused",
                Bash("git reset --hard origin/main"), "deny", "RULE-outright-denials"),
            new("GitCheckoutPaths_IsRefused",
                Bash("git checkout -- src/app.ts"), "deny", "RULE-outright-denials"),
            new("GitRestoreWorktree_IsRefused",
                Bash("git restore src/app.ts"), "deny", "RULE-outright-denials"),
            new("GitRestoreStaged_PassesThrough",
                ProjectBash("git restore --staged src/app.ts"), "pass", ""),
            new("GitDashCPrefix_StillSeesReset",
                Bash("git -C /repo reset --hard"), "deny", "RULE-outright-denials"),
            new("GitExeSuffix_StillSeesClean",
                Bash("git.exe clean -fd"), "deny", "RULE-outright-denials"),
            new("WorktreeRemove_IsAutoAllowed",
                Bash("git worktree remove ../item-42"), "allow", "", null, null, Path.GetDirectoryName(tmp)),
            new("WorktreeRemoveForce_IsRefused",
                Bash("git worktree remove --force ../item-42"), "deny", "RULE-outright-denials"),
            new("QuotedAlternationNamingDestructiveVerb_FailsClosed",
                Bash("rg -n 'a|git clean|b' x.mjs"), "deny", "RULE-outright-denials"),

            new("HomeWideSweep_IsRefused",
                Bash("rg -n pattern ~/"), "deny", "RULE-scoped-search"),
            new("FilesystemRootSweep_IsRefused",
                Bash("find / -name foo"), "deny", "RULE-scoped-search"),
            new("SystemDirSweep_IsRefused",
                Bash("grep -r x /usr"), "deny", "RULE-scoped-search"),
            new("WrapperPrefixedSweep_IsRefused",
                Bash("sudo rg -n x $HOME"), "deny", "RULE-scoped-search"),
            new("DriveRootSweep_IsRefused",
                Bash("ls C:/"), "deny", "RULE-scoped-search"),
            new("ScopedSearch_Passes",
                ProjectBash("rg -n pattern ./docs"), "pass", ""),

            new("SessionScratchpadWrite_IsRefused",
                Bash("echo hi > /x/claude-123/scratchpad/a.txt"), "deny", "RULE-scratch-temp"),

            new("ProbeFileRemoval_IsAutoAllowed",
                Bash("rm __probe.ts"), "allow", ""),
            new("RemovalUnderOsTempDir_IsAutoAllowed",
                Bash($"rm {tmp}/junk.txt"), "allow", ""),
            new("RemovalUnderCanonicalOsTempDir_IsAutoAllowed",
                Bash($"rm {privateTmp}/junk.txt"), "allow", ""),
            new("RemovalViaTmpDirVariable_IsAutoAllowed",
                Bash("rm $TMPDIR/junk.txt"), "allow", ""),
            new("RemovalViaBracedTmpDirVariable_IsAutoAllowed",
                Bash("rm -f \"${TMPDIR}/junk.txt\""), "allow", ""),
            new("RelativeRemovalWithTempCwd_IsAutoAllowed",
                Bash("rm notes.txt"), "allow", ""),
            new("RemovalOfTempDirItself_IsRefused",
                Bash("rm -rf $TMPDIR"), "deny", "RULE-dedicated-rm"),
            new("RemovalOfBracedTempDirWithSlash_IsRefused",
                Bash("rm -rf \"${TMPDIR}/\""), "deny", "RULE-dedicated-rm"),
            new("RemovalOfAbsoluteTempDirWithSlash_IsRefused",
                Bash($"rm -rf {tmp}/"), "deny", "RULE-dedicated-rm"),
            new("RemovalOfCanonicalTempDir_IsRefused",
                Bash($"rm -rf {privateTmp}"), "deny", "RULE-dedicated-rm"),
            new("RemovalOfCwdWhenCwdIsTempDir_IsRefused",
                Bash("rm -rf ."), "deny", "RULE-dedicated-rm"),
            new("RemovalOfTmp_IsRefused",
                Bash("rm -rf /tmp"), "deny", "RULE-dedicated-rm"),
            new("RemovalOfTmpWithSlash_IsRefused",
                Bash("rm -rf /tmp/"), "deny", "RULE-dedicated-rm"),
            new("RemovalOfPrivateTmp_IsRefused",
                Bash("rm -rf /private/tmp"), "deny", "RULE-dedicated-rm"),
            new("RemovalOfClaudeDir_IsRefused",
                Bash("rm -rf ~/.claude"), "deny", "RULE-dedicated-rm"),
            new("RemovalOfClaudeDirWithSlash_IsRefused",
                Bash($"rm -rf {home}/.claude/"), "deny", "RULE-dedicated-rm"),
            new("ChainedRemovalOfClaudeDir_IsRefused",
                Bash("rm -rf $HOME/.claude && ls"), "deny", "RULE-dedicated-rm"),
            new("RemovalOfTildeClaudeDirWithSlash_IsRefused",
                Bash("rm -rf ~/.claude/"), "deny", "RULE-dedicated-rm"),
            new("RemovalOfHomeVarClaudeDirWithSlash_IsRefused",
                Bash("rm -rf $HOME/.claude/"), "deny", "RULE-dedicated-rm"),
            new("RemovalOfForwardSlashClaudeDirWithSlash_IsRefused",
                Bash($"rm -rf {homeSlashed}/.claude/"), "deny", "RULE-dedicated-rm"),
            new("RemovalOfMsysClaudeDir_IsRefused",
                Bash($"rm -rf {homeMsys}/.claude"), "deny", "RULE-dedicated-rm"),
            new("RemovalOfTmpWithSlashFromProject_IsRefused",
                ProjectBash("rm -rf /tmp/"), "deny", "RULE-dedicated-rm"),
            new("RemovalOfTempDirWithoutSeparator_IsRefused",
                Bash($"rm -rf {tmp}"), "deny", "RULE-dedicated-rm"),
            new("RemovalOfForwardSlashTempDir_IsRefused",
                Bash($"rm -rf {tmpSlashed}"), "deny", "RULE-dedicated-rm"),
            new("RemovalOfCwdWhenCwdIsForwardSlashTempDir_IsRefused",
                BashIn(tmpSlashed, "rm -rf ."), "deny", "RULE-dedicated-rm"),

            new("TrackedCleanFileInAnyRepo_IsAutoAllowed",
                BashIn(repo, "rm tracked.txt"), "allow", "", null, hookTmp),
            new("UntrackedFileInAnyRepo_IsRefused",
                BashIn(repo, "rm untracked.txt"), "deny", "RULE-worktree-rm", null, hookTmp),
            new("IgnoredFileInAnyRepo_IsRefused",
                BashIn(repo, "rm ignored.txt"), "deny", "RULE-worktree-rm", null, hookTmp),
            new("RepoGitDirectory_IsRefused",
                BashIn(repo, "rm -rf .git"), "deny", "RULE-worktree-rm", null, hookTmp),
            new("RepoRootItself_IsRefused",
                BashIn(repo, $"rm -rf {repo}"), "deny", "RULE-worktree-rm", null, hookTmp),
            new("RemovalThroughProbeEntryUnderOsTempDir_IsAutoAllowed",
                Bash($"rm {tmp}/__probe-dir/output.bin {tmp}/__probe.txt"), "allow", ""),
            new("RemovalUnderClaudeDir_IsAutoAllowed",
                Bash($"rm {home}/.claude/junk.txt"), "allow", ""),

            new("TrackedFileInsideLaunchFolder_IsAutoAllowed",
                BashIn(repo, "rm tracked.txt"), "allow", "", null, hookTmp, repo),
            new("TrackedFileOutsideLaunchFolder_IsRefused",
                BashIn(repo, "rm tracked.txt"), "deny", "RULE-dedicated-rm", null, hookTmp, otherLaunch),
            new("MissingLaunchFolder_IsRefused",
                BashIn(repo, "rm tracked.txt"), "deny", "RULE-dedicated-rm", null, hookTmp, ""),
            new("ClaudeDirFileLaunchedElsewhere_IsAutoAllowed",
                Bash($"rm {home}/.claude/junk.txt"), "allow", "", null, hookTmp, repo),
            new("ClaudeDirFileLaunchedInClaudeDir_IsAutoAllowed",
                Bash($"rm {home}/.claude/junk.txt"), "allow", "", null, hookTmp, $"{home}/.claude"),
            new("TempFileLaunchedElsewhere_IsAutoAllowed",
                Bash("rm $TMPDIR/junk.txt"), "allow", "", null, null, otherLaunch),
            new("TempFileWithMissingLaunchFolder_IsAutoAllowed",
                Bash("rm $TMPDIR/junk.txt"), "allow", "", null, null, ""),
            new("ProbeOutsideLaunchFolder_IsRefused",
                BashIn(repo, "rm /etc/__probe.ts"), "deny", "RULE-dedicated-rm", null, hookTmp, repo),
            new("ItemDirectoryFromWorkRootSession_IsAutoAllowed",
                Bash($"rm -rf {itemDir}"), "allow", "", null, null, workRoot),
            new("WorktreeRemoveFromWorkRootSession_IsAutoAllowed",
                Bash($"git -C {workRoot}/realplus worktree remove {itemDir}"), "allow", "", null, null, workRoot),
            new("WorktreeSessionRemovingOutsideItsTree_IsRefused",
                Bash($"rm {workRoot}/realplus/tracked.txt"), "deny", "RULE-dedicated-rm", null, null, itemDir),
            new("WorktreeSessionRemovingAnotherWorktree_IsRefused",
                Bash($"git -C {workRoot}/realplus worktree remove {workRoot}/.worktrees/realplus/B98-other"),
                "deny", "RULE-dedicated-rm", null, null, itemDir),
            new("ProofScreenshotsFromProjectSession_AreAutoAllowed",
                Bash($"rm {itemDir}/.orchestrator/screenshots/world/a.png {itemDir}/.orchestrator/baselines/win/b.png"),
                "allow", "", null, null, Path.Combine(workRoot, "realplus")),
            new("LedgerFromProjectSession_IsRefused",
                Bash($"rm {itemDir}/.orchestrator/progress.md"),
                "deny", "RULE-item-folder-owned", null, null, Path.Combine(workRoot, "realplus")),
            new("ProofMixedWithLedger_IsRefused",
                Bash($"rm {itemDir}/.orchestrator/screenshots/a.png {itemDir}/.orchestrator/spec.md"),
                "deny", "RULE-dedicated-rm", null, null, Path.Combine(workRoot, "realplus")),
            new("OtherProjectWorktreeFromProjectSession_IsRefused",
                Bash($"rm {itemDir}/.orchestrator/screenshots/a.png"),
                "deny", "RULE-dedicated-rm", null, null, Path.Combine(workRoot, "eGov")),
            new("RepoGitDirectoryInsideLaunchFolder_IsRefused",
                BashIn(repo, "rm -rf .git"), "deny", "RULE-worktree-rm", null, hookTmp, repo),
            new("RepoGitSubpathInsideLaunchFolder_IsRefused",
                BashIn(repo, "rm .git/config"), "deny", "RULE-worktree-rm", null, hookTmp, repo),
            new("ProbeInsideLaunchFolder_IsAutoAllowed",
                BashIn(repo, "rm __probe.ts"), "allow", "", null, hookTmp, repo),
            new("LaunchFolderItself_IsRefused",
                BashIn(repo, $"rm -rf {repo}/"), "deny", "RULE-dedicated-rm", null, hookTmp, repo),

            new("CompoundRemoval_IsRefused",
                Bash("rm foo.txt && ls"), "deny", "RULE-dedicated-rm"),
            new("GlobRemoval_IsRefused",
                Bash("rm *.tmp"), "deny", "RULE-dedicated-rm"),
            new("UntrackedRemovalOutsideSafeRoots_IsRefused",
                ProjectBash("rm notes.txt"), "deny", "RULE-dedicated-rm"),
            new("FindWithDelete_IsRefused",
                Bash("find . -name '*.tmp' -delete"), "deny", "RULE-dedicated-rm"),
            new("XargsRemoval_IsRefused",
                Bash("ls | xargs rm"), "deny", "RULE-dedicated-rm"),
            new("EnvAssignmentPrefixedRemoval_IsRefused",
                ProjectBash("FOO=1 rm x.txt"), "deny", "RULE-dedicated-rm"),
            new("MultipleEnvAssignmentsBeforeRemoval_IsRefused",
                ProjectBash("A=1 B=2 rm x.txt"), "deny", "RULE-dedicated-rm"),
            new("EnvAssignmentBeforeProbeRemoval_IsAutoAllowed",
                Bash("FOO=1 rm __probe.ts"), "allow", ""),
            new("PowerShellRemoveItem_IsRefused",
                PowerShell("Remove-Item x.txt"), "deny", "RULE-dedicated-rm"),
            new("PipedRemoveItem_IsRefused",
                Bash("gci | Remove-Item"), "deny", "RULE-dedicated-rm"),

            new("OrdinaryBuild_Passes",
                Bash("bun run build"), "pass", ""),
            new("SedInPlaceOnProjectFile_IsRefused",
                ProjectBash("sed -i '' s/a/b/ x.ts"), "deny", "RULE-edit-tools-only"),
            new("TeeToProjectFile_IsRefused",
                ProjectBash("cmd | tee out.txt"), "deny", "RULE-edit-tools-only"),
            new("RedirectToProjectFile_IsRefused",
                ProjectBash("echo hi > out.txt"), "deny", "RULE-edit-tools-only"),
            new("AppendAfterEscapedQuotesAndApostrophe_IsRefused",
                ProjectBash("cd .orchestrator && printf '%s\\n' \"- note \\\"quoted\\\" and Let's go\" >> spec.md"), "deny", "RULE-edit-tools-only"),
            new("FdRedirectToProjectFile_IsRefused",
                ProjectBash("swift build 2> build.log"), "deny", "RULE-edit-tools-only"),
            new("HeredocRedirectedToProjectFile_IsRefused",
                ProjectBash("cat <<EOF > notes.md\nhi\nEOF"), "deny", "RULE-edit-tools-only"),
            new("TruncateProjectFile_IsRefused",
                ProjectBash("truncate -s 0 log.txt"), "deny", "RULE-edit-tools-only"),
            new("RedirectToTmpDirVariable_IsAutoAllowed",
                ProjectBash("swift build > \"$TMPDIR/build.log\" 2>&1"), "allow", ""),
            new("RedirectToAbsoluteTemp_IsAutoAllowed",
                ProjectBash($"swift build > {tmp}/build.log"), "allow", ""),
            new("RelativeRedirectAfterCdIntoTemp_IsAutoAllowed",
                ProjectBash($"cd {tmp} && cargo build > build.log 2>&1"), "allow", ""),
            new("RelativeRedirectAfterSubshellCdIntoTemp_IsRefused",
                ProjectBash($"(cd {tmp}) ; echo hi > out.txt"), "deny", "RULE-edit-tools-only"),
            new("RedirectToNonTempFolderNamedTmp_IsRefused",
                ProjectBash("cargo build > /d/tmp/b11/build0.log 2>&1"), "deny", "RULE-edit-tools-only"),
            new("RedirectToSlashTmp_IsAutoAllowed",
                ProjectBash("swift build > /tmp/build.log"), "allow", ""),
            new("TeeToDevNull_SaysNothing",
                ProjectBash("cmd | tee /dev/null"), "pass", ""),
            new("RedirectToDevNull_SaysNothing",
                ProjectBash("echo hi > /dev/null 2>&1"), "pass", ""),
            new("QuotedArrowInPattern_SaysNothing",
                ProjectBash("rg -n '->' src && echo \"a > b\""), "pass", ""),
            new("HeredocBodyHoldingRedirect_SaysNothing",
                ProjectBash("git commit -F - <<'EOF'\nfix a > b\nEOF"), "pass", ""),
            new("InterpreterOneLiner_SaysNothing",
                ProjectBash("node -e 'console.log(1)'"), "pass", ""),
            new("SedWithoutInPlace_SaysNothing",
                ProjectBash("sed -n 1,5p x.ts"), "pass", ""),

            new("WhileTrueSleepLoop_IsRefused",
                Bash("while true; do curl -s localhost:8080/health; sleep 30; done"), "deny", "RULE-no-polling-loop"),
            new("WhileColonSleepLoop_IsRefused",
                Bash("while :\ndo\n  gh run view 1\n  sleep 10\ndone"), "deny", "RULE-no-polling-loop"),
            new("UntilSleepLoop_IsRefused",
                Bash("until test -f done.flag; do sleep 5; done"), "deny", "RULE-no-polling-loop"),
            new("ForeverForSleepLoop_IsRefused",
                Bash("for ((;;)); do date; sleep 1; done"), "deny", "RULE-no-polling-loop"),
            new("BashCWrappedSleepLoop_IsRefused",
                Bash("bash -c 'while true; do sleep 30; done'"), "deny", "RULE-no-polling-loop"),
            new("ShCWrappedUntilLoop_IsRefused",
                Bash("sh -c \"until curl -sf x; do sleep 2; done\""), "deny", "RULE-no-polling-loop"),
            new("WatchInterval_IsRefused",
                Bash("watch -n 5 kubectl get pods"), "deny", "RULE-no-polling-loop"),
            new("PlainSleep_SaysNothing",
                Bash("sleep 5"), "pass", ""),
            new("ForInLoopWithoutSleep_SaysNothing",
                Bash("for f in *.txt; do echo $f; done"), "pass", ""),
            new("WhileReadWithoutSleep_SaysNothing",
                Bash("cat list | while read line; do echo $line; done"), "pass", ""),

            new("LongForegroundBashTimeout_IsRefused",
                Timed("Bash", "dotnet test", 600000, null), "deny", "RULE-short-foreground"),
            new("LongForegroundPowerShellTimeout_IsRefused",
                Timed("PowerShell", "dotnet test", 120000, false), "deny", "RULE-short-foreground"),
            new("LongBackgroundTimeout_IsRefused",
                Timed("Bash", "dotnet test", 600000, true), "deny", "RULE-short-foreground"),
            new("ShortForegroundTimeout_SaysNothing",
                Timed("Bash", "dotnet build", 30000, null), "pass", ""),
            // TEST_POINT: an omitted timeout is the harness default (120 s), not the agent's choice, so it is allowed.
            new("OmittedTimeout_SaysNothing",
                Bash("dotnet build"), "pass", ""),

            new("NarrationComment_IsRefused",
                Write("/tmp/x.ts", "// increment the counter by one\ncount += 1;\n"),
                "deny", "RULE-comment-policy"),
            new("TaggedComment_Passes",
                Write("/tmp/x.ts", "// TODO: wire this up\nconst a = 1;\n"), "pass", ""),
            new("ToolDirective_Passes",
                Write("/tmp/x.ts", "// eslint-disable-next-line\nconst a = 1;\n"), "pass", ""),
            new("SwiftToolsVersionDirective_Passes",
                Write("/tmp/Package.swift", "// swift-tools-version: 6.0\nimport PackageDescription\n"), "pass", ""),
            new("AsciiDividerComment_IsInert",
                Write("/tmp/x.ts", "// ----------\nconst a = 1;\n"), "pass", ""),
            new("BoxDrawingDividerComment_IsInert",
                Write("/tmp/x.ts", "// \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\nconst a = 1;\n"),
                "pass", ""),
            new("EmDashDividerComment_IsInert",
                Write("/tmp/x.ts", "// \u2014\u2014\u2014\u2014\u2014\u2014\nconst a = 1;\n"),
                "pass", ""),
            new("BannerWithWordsBetweenRules_IsRefused",
                Write("/tmp/x.ts", "// \u2500\u2500 Helpers \u2500\u2500\nconst a = 1;\n"),
                "deny", "RULE-comment-policy"),
            new("JsDocParamBlock_IsRefused",
                Write("/tmp/x.ts", "/**\n * Adds two numbers.\n * @param a first\n */\nfunction f(a) {}\n"),
                "deny", "RULE-comment-policy"),
            new("DoubleSlashInsideStringLiteral_IsNotAComment",
                Write("/tmp/x.ts", "const url = \"http://x.com//path\";\n"), "pass", ""),
            new("TrailingSingleWordLabel_IsInert",
                Write("/tmp/x.ts", "} // end\n"), "pass", ""),
            new("TrailingIdentifierLabel_IsInert",
                Write("/tmp/x.cs", "#endif // FEATURE_X\n"), "pass", ""),
            new("TrailingMultiWordNarration_IsRefused",
                Write("/tmp/x.ts", "const a = 1; // set a\n"),
                "deny", "RULE-comment-policy"),
            new("TrailingPunctuatedNarration_IsRefused",
                Write("/tmp/x.ts", "const a = 1; // set a, then bump the counter\n"),
                "deny", "RULE-comment-policy"),
            new("TagOnFirstLineOfWrappedRun_CoversWholeRun",
                Write("/tmp/x.ts", "// PERF: hot path\n// so we cache it\nconst a = 1;\n"), "pass", ""),
            new("CSharpDocSummary_IsRefused",
                Write("/tmp/x.cs", "/// <summary>Adds.</summary>\npublic int F() => 1;\n"),
                "deny", "RULE-comment-policy"),
            new("SwiftNestedBlockComment_IsRefused",
                Write("/tmp/x.swift", "/* outer /* inner */ still */\nlet a = 1\n"),
                "deny", "RULE-comment-policy"),
            new("KotlinKDoc_IsRefused",
                Write("/tmp/x.kt", "/** Loads the listings from the gateway. */\nval a = 1\n"),
                "deny", "RULE-comment-policy"),
            new("KotlinNarration_IsRefused",
                Write("/tmp/build.gradle.kts", "// apply the android plugin\nplugins { }\n"),
                "deny", "RULE-comment-policy"),
            new("KotlinRawStringHoldingSlashes_IsNotAComment",
                Write("/tmp/x.kt", "val s = \"\"\"https://x.com // \"quoted\" \"\"\"\nval a = 1\n"), "pass", ""),
            new("KotlinBacktickTestNameWithSlashes_IsNotAComment",
                Write("/tmp/x.kt", "fun `sign in // then relaunch`() {}\n"), "pass", ""),
            new("KotlinTaggedComment_Passes",
                Write("/tmp/x.kt", "// TODO: wire the map\nval a = 1\n"), "pass", ""),
            new("SwiftRawStringHoldingSlashes_IsNotAComment",
                Write("/tmp/x.swift", "let s = #\"not // a comment\"#\nlet a = 1\n"), "pass", ""),
            new("RustNarration_IsRefused",
                Write("/tmp/x.rs", "// increment the counter\ncount += 1;\n"),
                "deny", "RULE-comment-policy"),
            new("RustDocComment_IsRefused",
                Write("/tmp/x.rs", "/// Adds two numbers.\nfn add(a: i32, b: i32) -> i32 { a + b }\n"),
                "deny", "RULE-comment-policy"),
            new("RustInnerDocComment_IsRefused",
                Write("/tmp/lib.rs", "//! The renderer crate.\nmod a;\n"),
                "deny", "RULE-comment-policy"),
            new("RustNestedBlockComment_IsRefused",
                Write("/tmp/x.rs", "/* outer /* inner */ still */\nlet a = 1;\n"),
                "deny", "RULE-comment-policy"),
            new("RustTaggedComment_Passes",
                Write("/tmp/x.rs", "// PERF: avoids a per-frame allocation\nlet a = 1;\n"), "pass", ""),
            new("RustRawStringHoldingSlashes_IsNotAComment",
                Write("/tmp/x.rs", "let s = r#\"not // a \"comment\"\"#;\nlet b = br\"//\";\n"), "pass", ""),
            // TEST_POINT: read as quotes, the lifetimes would pair up and expose the slashes inside the string.
            new("RustLifetimesAroundSlashString_AreNotQuotes",
                Write("/tmp/x.rs", "fn f<'a>(s: &'a str) -> &'a str { \"x // y\" }\n"), "pass", ""),
            new("RustCharLiteralsHoldingQuotes_AreNotStrings",
                Write("/tmp/x.rs", "let q = '\"'; let e = '\\''; let s = \"//\";\n"), "pass", ""),
            new("RustCommentAfterLifetime_IsRefused",
                Write("/tmp/x.rs", "fn f<'a>(s: &'a str) {} // set up the thing\n"),
                "deny", "RULE-comment-policy"),
            new("RustSafetyComment_Passes",
                Write("/tmp/x.rs", "// SAFETY: the slice outlives the mapping\nunsafe { f(p) };\n"), "pass", ""),
            new("WgslNarration_IsRefused",
                Write("/tmp/x.wgsl", "// sample the shadow map\nlet a = 1.0;\n"),
                "deny", "RULE-comment-policy"),
            new("WgslNestedBlockComment_IsRefused",
                Write("/tmp/x.wgsl", "/* outer /* inner */ still */\nlet a = 1.0;\n"),
                "deny", "RULE-comment-policy"),
            new("WgslTaggedComment_Passes",
                Write("/tmp/x.wgsl", "// PERF: one fetch per texel\nlet a = 1.0;\n"), "pass", ""),
            new("PythonHashNarration_IsRefused",
                Write("/tmp/x.py", "# increments the counter\nx += 1\n"),
                "deny", "RULE-comment-policy"),
            new("PythonNoqaDirective_Passes",
                Write("/tmp/x.py", "# noqa: E501\nx = 1\n"), "pass", ""),
            new("PythonDocstringArgsSection_IsRefused",
                Write("/tmp/x.py",
                    "def f(a, b):\n    \"\"\"Add two numbers.\n\n    Args:\n        a: the first number\n        b: the second number\n\n    Returns:\n        the sum\n    \"\"\"\n    return a + b\n"),
                "deny", "RULE-comment-policy"),
            new("BlockCommentWithStarLeaders_IsRefused",
                Write("/tmp/x.ts", "/*\n * describes the thing\n */\nconst a = 1;\n"),
                "deny", "RULE-comment-policy"),
            new("CleanCode_Passes",
                Write("/tmp/x.ts", "export const x = 1;\n"), "pass", ""),
            new("EmptyContent_Passes",
                Write("/tmp/x.ts", ""), "pass", ""),
            new("NonCodeExtension_Passes",
                Write("/tmp/x.md", "// not code\n"), "pass", ""),
            new("EditToolPayload_IsPoliced",
                Edit("/tmp/x.ts", "// narrate this\nconst a = 1;\n"),
                "deny", "RULE-comment-policy"),
            new("MultiEditPayload_IsPoliced",
                MultiEdit("/tmp/x.ts", "// one\n", "// two\n"),
                "deny", "RULE-comment-policy"),

            new("UnregisteredWorkaroundInTs_IsRefused",
                Write("/tmp/x.ts", "// WORKAROUND: upstream returns 204\nreturn null;\n"),
                "deny", "RULE-workaround-ledger"),
            new("UnregisteredWorkaroundInCs_IsRefused",
                Write("/tmp/x.cs", "// WORKAROUND: api quirk\nvar a = 1;\n"),
                "deny", "RULE-workaround-ledger"),

            new("UnpolicedTool_ProducesNoOutput",
                Read("/tmp/x.ts"), "pass", ""),
            new("MalformedPayload_FailsClosed",
                "NOT JSON", "deny", "RULE-guard-fail-closed"),
        ];
    }
}
