using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

internal sealed record Observed(string Decision, string Rule, string Reason, string Context, string Raw);

internal sealed record Result(TestCase Case, Observed Actual);

internal static partial class Harness
{
    private static string SourceDir([CallerFilePath] string thisFile = "")
    {
        return Path.GetDirectoryName(thisFile)!;
    }

    private static readonly string TestsDir = SourceDir();

    private static readonly string HookRoot =
        Path.GetFullPath(Path.Combine(TestsDir, ".."));

    private static readonly string Binary =
        Environment.GetEnvironmentVariable("LAW_TEST_BINARY") is { Length: > 0 } candidate
            ? candidate
            : Path.Combine(HookRoot, "bin", OperatingSystem.IsWindows() ? "law.exe" : "law");

    private static readonly string ArtifactsDir = Path.Combine(TestsDir, "artifacts");

    [GeneratedRegex(@"\[(RULE-[a-z-]+)\]")]
    private static partial Regex RuleTag();

    [GeneratedRegex(@"^RULE-[a-z][a-z-]*\z")]
    public static partial Regex KebabSlug();

    public static readonly string LedgerPath =
        Path.Combine(Path.GetTempPath(), "law-refused-deletions.md");

    private static string Invoke(string exe, string[] args, string input, string? tempDir, string? launchDir)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (tempDir is not null)
        {
            psi.Environment["TMPDIR"] = tempDir;
            psi.Environment["TMP"] = tempDir;
            psi.Environment["TEMP"] = tempDir;
        }
        psi.Environment["LAW_REFUSED_DELETIONS_LEDGER"] = LedgerPath;
        psi.Environment["BACKLOG_WORK_ROOT"] = Cases.WorkRoot;
        psi.Environment.Remove("CLAUDE_PLUGIN_OPTION_DISABLED_LAWS");
        var launch = launchDir ?? Path.TrimEndingDirectorySeparator(Path.GetTempPath());
        if (launch.Length > 0)
        {
            psi.Environment["CLAUDE_PROJECT_DIR"] = launch;
        }
        else
        {
            psi.Environment.Remove("CLAUDE_PROJECT_DIR");
        }
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"could not start {exe}");

        proc.StandardInput.Write(input);
        proc.StandardInput.Close();

        var stdout = proc.StandardOutput.ReadToEnd();
        proc.StandardError.ReadToEnd();
        proc.WaitForExit(20000);
        return stdout.Trim();
    }

    private static Observed Parse(string raw)
    {
        if (raw.Length == 0)
        {
            return new Observed("pass", "", "", "", raw);
        }

        using var doc = JsonDocument.Parse(raw);
        var hook = doc.RootElement.GetProperty("hookSpecificOutput");

        var decision = hook.TryGetProperty("permissionDecision", out var d)
            ? d.GetString() ?? "pass"
            : "pass";
        var reason = hook.TryGetProperty("permissionDecisionReason", out var r)
            ? r.GetString() ?? ""
            : "";
        var context = hook.TryGetProperty("additionalContext", out var c)
            ? c.GetString() ?? ""
            : "";

        var match = RuleTag().Match(reason);
        return new Observed(decision, match.Success ? match.Groups[1].Value : "", reason, context, raw);
    }

    public static IReadOnlyList<Result> Results { get; } = Run();

    private static readonly Dictionary<string, Result> ByName =
        Results.ToDictionary(r => r.Case.Name);

    public static TheoryData<string> CaseNames
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var c in Cases.All)
            {
                data.Add(c.Name);
            }
            return data;
        }
    }

    public static (TestCase Expected, Observed Actual) Lookup(string name)
    {
        var result = ByName[name];
        return (result.Case, result.Actual);
    }

    private static List<Result> Run()
    {
        if (!File.Exists(Binary))
        {
            throw new InvalidOperationException(
                $"guard binary not found at {Binary} — run: dotnet publish law.cs -o ./bin");
        }

        File.WriteAllText(LedgerPath, "");
        var results = Cases.All
            .Select(c => new Result(c, Parse(Invoke(Binary, [], c.Payload, c.HookTempDir, c.LaunchDir))))
            .ToList();

        WriteSnapshot(results);
        return results;
    }

    private static void WriteSnapshot(List<Result> results)
    {
        Directory.CreateDirectory(ArtifactsDir);

        var lines = new List<string>
        {
            "law guard decisions. Regenerate with: dotnet run tests/law.tests.cs",
            "",
        };

        foreach (var r in results)
        {
            var rule = r.Actual.Rule.Length > 0 ? r.Actual.Rule : "-";
            var context = r.Actual.Context.Length > 0 ? "  +context" : "";
            lines.Add($"{r.Actual.Decision,-5}  {rule,-28}  {r.Case.Name}{context}");
        }

        lines.Add("");
        File.WriteAllText(Path.Combine(ArtifactsDir, "decisions.snapshot.txt"),
            string.Join("\n", lines));
    }
}
