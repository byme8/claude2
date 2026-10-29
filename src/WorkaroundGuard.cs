using System.Text.RegularExpressions;

internal sealed partial class WorkaroundGuard : Law
{
    public override string Name
    {
        get { return "workaround"; }
    }

    public override IReadOnlyList<string> Tools
    {
        get { return ["Write", "Edit", "MultiEdit"]; }
    }

    private static readonly HashSet<string> CodeExt =
    [
        ".js", ".jsx", ".mjs", ".cjs",
        ".ts", ".tsx", ".mts", ".cts",
        ".cs",
    ];

    [GeneratedRegex(@"\bWORKAROUND\b\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex Gated();

    [GeneratedRegex(@"\bWORKAROUND\b\s*:\s*(.+)\z", RegexOptions.IgnoreCase)]
    private static partial Regex Justification();

    [GeneratedRegex(@"^\s*/+\*?|^\s*\*+/?|^\s*//+")]
    private static partial Regex LeaderStrip();

    protected override GuardResult Evaluate(ToolInput? input, string cwd)
    {
        var filePath = input?.FilePath ?? "";
        if (!CodeExt.Contains(Src.Ext(filePath)))
        {
            return Pass();
        }

        var text = Added.Text(input);
        if (text.Trim().Length == 0)
        {
            return Pass();
        }

        var gated = GroupUnits(ExtractComments(text))
            .Select(CommentProse)
            .Where(p => Gated().IsMatch(p))
            .Select(JustificationText)
            .ToList();
        if (gated.Count == 0)
        {
            return Pass();
        }

        var ledgerPath = FindLedger(filePath);
        var ledgerText = ledgerPath is not null && Fs.Exists(ledgerPath)
            ? File.ReadAllText(ledgerPath)
            : "";

        var unregistered = gated
            .Where(j => !IsRegistered(ledgerText, ledgerPath ?? "", filePath, j))
            .ToList();
        if (unregistered.Count == 0)
        {
            return Pass();
        }

        var list = string.Join("\n", unregistered.Select(j => $"  • {j}"));
        var where = ledgerPath ?? "workarounds.md (at the project root)";
        var relForShape = Path.GetRelativePath(
            Path.GetDirectoryName(ledgerPath ?? filePath) ?? ".", filePath);

        return Decide("deny", "RULE-workaround-ledger",
            $@"This call breaks a law.

Workaround law: a WORKAROUND: comment must be registered before you add it.

Unregistered in this edit:
{list}

Register each in {where} with the file path and the code line, then retry. The entry must contain a VERBATIM COPY of the comment text (the hook matches on it). Required entry shape:

    ## {relForShape}:LINE
    <paste the workaround comment text verbatim, e.g. ""WORKAROUND: ..."">
    Why it is unavoidable: <what forces it — an upstream bug, a framework constraint, a platform quirk>. What the correct fix would be once that is gone.

The ledger is a deterrent against lazy tags. A real workaround names an external cause you do not control. If you cannot write an honest ""why it is unavoidable"", the comment is not a workaround — drop the tag, or solve the underlying problem instead of papering over it with a label.");
    }

    private static GuardResult Pass()
    {
        return Decide("pass", "", "");
    }

    private sealed record RawComment(string Text, bool Standalone, string Kind);

    private static List<RawComment> ExtractComments(string src)
    {
        var results = new List<RawComment>();
        var n = src.Length;
        var i = 0;
        var lineIsBlankSoFar = true;

        while (i < n)
        {
            var c = src[i];
            var c2 = Js.At(src, i + 1);

            if (c == '"' || c == '\'' || c == '`')
            {
                var quote = c;
                lineIsBlankSoFar = false;
                i++;
                while (i < n)
                {
                    if (src[i] == '\\')
                    {
                        i += 2;
                        continue;
                    }
                    if (src[i] == quote)
                    {
                        i++;
                        break;
                    }
                    i++;
                }
                continue;
            }

            if (c == '/' && c2 == '/')
            {
                var start = i;
                var standalone = lineIsBlankSoFar;
                i += 2;
                while (i < n && src[i] != '\n')
                {
                    i++;
                }
                results.Add(new RawComment(src[start..i], standalone, "line"));
                continue;
            }

            if (c == '/' && c2 == '*')
            {
                var start = i;
                i += 2;
                while (i < n && !(Js.At(src, i) == '*' && Js.At(src, i + 1) == '/'))
                {
                    i++;
                }
                i += 2;
                results.Add(new RawComment(src[start..Math.Min(i, n)], lineIsBlankSoFar, "block"));
                lineIsBlankSoFar = false;
                continue;
            }

            if (c == '\n')
            {
                lineIsBlankSoFar = true;
            }
            else if (c != ' ' && c != '\t' && c != '\r' && c != '\f' && c != '\v')
            {
                lineIsBlankSoFar = false;
            }
            i++;
        }
        return results;
    }

    private static List<string> GroupUnits(List<RawComment> comments)
    {
        var units = new List<string>();
        List<string>? run = null;

        void Flush()
        {
            if (run is not null)
            {
                units.Add(string.Join("\n", run));
                run = null;
            }
        }

        foreach (var c in comments)
        {
            if (c.Kind == "line" && c.Standalone)
            {
                run ??= [];
                run.Add(c.Text);
                continue;
            }
            Flush();
            units.Add(c.Text);
        }

        Flush();
        return units;
    }

    private static string StripBlockEnd(string line)
    {
        var trimmed = line.TrimEnd();
        if (trimmed.EndsWith("*/", StringComparison.Ordinal))
        {
            return trimmed[..^2];
        }
        return line;
    }

    private static string CommentProse(string unit)
    {
        return string.Join(" ", unit.Split('\n')
            .Select(l => StripBlockEnd(LeaderStrip().Replace(l, "")).Trim())
            .Where(l => l.Length > 0))
            .Trim();
    }

    private static string JustificationText(string prose)
    {
        var m = Justification().Match(prose);
        if (m.Success)
        {
            return m.Groups[1].Value.Trim();
        }
        return "";
    }

    private static string? FindLedger(string startFile)
    {
        var dir = Path.GetDirectoryName(startFile) ?? "";
        for (var hops = 0; hops < 40 && dir.Length > 0; hops++)
        {
            var candidate = Path.Combine(dir, "workarounds.md");
            if (Fs.Exists(candidate))
            {
                return candidate;
            }
            if (Fs.Exists(Path.Combine(dir, ".git")))
            {
                return candidate;
            }

            var parent = Path.GetDirectoryName(dir);
            if (string.IsNullOrEmpty(parent) || parent == dir)
            {
                break;
            }
            dir = parent;
        }
        return null;
    }

    private static string Normalize(string s)
    {
        return Js.Squash(s.ToLowerInvariant());
    }

    private static bool IsRegistered(string ledgerText, string ledgerPath, string filePath, string justification)
    {
        if (justification.Length == 0)
        {
            return false;
        }

        var rel = Normalize(Path.GetRelativePath(Path.GetDirectoryName(ledgerPath) ?? ".", filePath));
        var slash = filePath.LastIndexOf('/');
        var baseName = Normalize(filePath[(slash + 1)..]);
        var hay = Normalize(ledgerText);
        var needle = Normalize(justification);

        if (!hay.Contains(needle))
        {
            return false;
        }
        return hay.Contains(rel) || hay.Contains(baseName);
    }
}
