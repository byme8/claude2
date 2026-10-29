using System.Text.RegularExpressions;

internal sealed partial class CommentGuard : Law
{
    public override string Name
    {
        get { return "comment"; }
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
        ".swift",
        ".kt", ".kts",
        ".c", ".h", ".cpp", ".cc", ".cxx", ".hpp", ".hh", ".hxx", ".inl",
        ".py", ".pyi",
        ".rs", ".wgsl",
    ];

    [GeneratedRegex(@"\b(WORKAROUND|PERF|BUG|FIXME|TODO|DEPRECATED|TEST_POINT|SAFETY)\b\s*:",
        RegexOptions.IgnoreCase)]
    private static partial Regex Justify();

    [GeneratedRegex("""
        ^(?:
            eslint\b | @ts- | @flow\b | tslint: | prettier- | biome-       # JS/TS linters
          | noinspection\b | global\b | globals\b | jshint\b | jslint\b
          | type-coverage: | [cv]8\s+ignore | istanbul\s+ignore | deno-
          | sourceMappingURL=
          | \#region | \#endregion | \#pragma | \#nullable | \#define      # C-family pragmas
          | \#if | \#elif | \#else | \#endif | \#warning | \#error
          | swiftlint: | swiftformat: | swift-format-ignore                # Swift tooling
          | swift-tools-version:
          | sourcery: | periphery:
          | noqa\b | type:\s*ignore | pylint: | pyright: | mypy:           # Python tooling
          | ruff: | flake8: | isort: | fmt:\s*(on|off) | coding[:=]
          | !/                                                             # shebang tail
        )
        """, RegexOptions.IgnorePatternWhitespace | RegexOptions.IgnoreCase)]
    private static partial Regex Directive();

    [GeneratedRegex(@"^[-=*#/~_!.\s─-╿—–]+\z")]
    private static partial Regex Divider();

    [GeneratedRegex(@"^!?\s*[A-Za-z_][A-Za-z0-9_:]*\z")]
    private static partial Regex ClosingLabel();

    [GeneratedRegex(@"^\s*(async\s+def|def|class)\b")]
    private static partial Regex DefLine();

    [GeneratedRegex(@":\s*(#.*)?\z")]
    private static partial Regex ColonEnd();

    [GeneratedRegex(@"^\s*(?:[rRbBuU]{0,2})(""""""|''')")]
    private static partial Regex DocOpener();

    [GeneratedRegex(@"^[ \t]*\*+", RegexOptions.Multiline)]
    private static partial Regex BlockStar();

    [GeneratedRegex(@"(""""""|''')")]
    private static partial Regex TripleQuote();

    [GeneratedRegex("""
        ^\s*(
            Args | Arguments | Parameters | Params | Returns | Return
          | Yields | Raises | Attributes | Keyword\ Args | Other\ Parameters
        )\s*:\s*\z
        """, RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex DocSection();

    [GeneratedRegex("""
        ^\s*(
            :param\b | :type\b | :returns?\b | :rtype\b | :raises?\b       # reStructuredText
          | @param\b | @returns?\b | @type\b | @rtype\b | @raises?\b       # epydoc / JSDoc
        )
        """, RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex DocField();

    protected override GuardResult Evaluate(ToolInput? input, string cwd)
    {
        var filePath = input?.FilePath ?? "";
        var extension = Src.Ext(filePath);
        if (!CodeExt.Contains(extension))
        {
            return Pass();
        }

        var text = Added.Text(input);
        if (text.Trim().Length == 0)
        {
            return Pass();
        }

        var isPython = extension is ".py" or ".pyi";

        if (isPython)
        {
            var sections = DocstringBoilerplate(text);
            if (sections.Count > 0)
            {
                return Decide("deny", "RULE-comment-policy",
                    $@"comment-guard: remove these docstring section(s) — they restate the signature:
{string.Join("\n", sections.Select(s => "  " + s))}

Args:/Returns:/Raises:/Yields:/Parameters/:param:/@param blocks name the types and
parameters the signature already declares. Delete the section. Keep the prose above
it only if it says WHY — a constraint, a surprising choice, an invariant.
A one-line docstring is fine. A docstring that lists arguments is not.");
            }
        }

        var docstringUnits = isPython
            ? ExtractDocstrings(text).Select(d => new Unit([d], "docstring")).ToList()
            : [];

        var units = Src.GroupUnits(Src.ExtractComments(text, Src.For(extension)))
            .Concat(docstringUnits)
            .Where(IsPolicedUnit)
            .ToList();
        if (units.Count == 0)
        {
            return Pass();
        }

        var offenders = units.Where(u => !Justify().IsMatch(UnitBody(u))).ToList();
        if (offenders.Count == 0)
        {
            return Pass();
        }

        var list = string.Join("\n", offenders.Select(u =>
        {
            var joined = string.Join(" ", u.Comments.Select(c => c.Text));
            var squashed = Js.Squash(joined);
            return "  " + (squashed.Length > 160 ? squashed[..160] : squashed);
        }));

        return Decide("deny", "RULE-comment-policy",
            $@"This call breaks a law.

Comment law: code says what it does; prose only says what code cannot. These violate it:
{list}

The action is: DELETE and resubmit. No exceptions, no judgement call. Section dividers / banners (""// ── Helpers ──"", ""// MARK: - Helpers""), restatements of the code, and ""what it does"" narration all get removed.

Relocating the text is the same violation. Do not move it into a docstring, a doc comment, a longer name, a commit message, or a test description. Docstrings are policed by this same rule — reaching for one to carry a rejected comment is evasion, not compliance.

Do NOT add a tag to silence this. A tag is ONLY honest when the line states something the code genuinely cannot — a surprising reason, an invariant a reader would otherwise break — for example a WORKAROUND: tag explaining a quirk a downstream API forces on you.
If you are reaching for a tag to make the block pass, the answer was DELETE. When unsure, DELETE.

Accepted tags (only for a real, code-can't-say-it reason): WORKAROUND: PERF: BUG: FIXME: TODO: DEPRECATED:
WORKAROUND: additionally requires a ledger entry — see workaround-guard. It is the only tag for ""the code is shaped wrong because something external forces it"".
TEST_POINT: is accepted for a deliberate test-case rationale (the ""why this case exists"" narration) — not for restating what the test code does.
SAFETY: is accepted only directly above a Rust unsafe block or impl, stating the invariant that makes it sound.
Doc leaders earn no pass either — Swift and C# triple-slash summaries, JSDoc param/returns: they restate the types and signature the code already declares. Let the code say it. Only tool directives (eslint, ts, prettier, biome, swiftlint, swiftformat) are never flagged.");
    }

    private static GuardResult Pass()
    {
        return Decide("pass", "", "");
    }

    public static string CommentBody(string text)
    {
        var t = text.Trim();
        var docstringQuote = t.StartsWith("\"\"\"") ? "\"\"\"" : t.StartsWith("'''") ? "'''" : "";

        if (docstringQuote.Length > 0)
        {
            t = t[docstringQuote.Length..];
            if (t.TrimEnd().EndsWith(docstringQuote))
            {
                t = t.TrimEnd()[..^docstringQuote.Length];
            }
            return t.Trim();
        }

        if (t.StartsWith('#'))
        {
            t = t.TrimStart('#');
        }
        else if (t.StartsWith("//"))
        {
            t = t.TrimStart('/');
        }
        else if (t.StartsWith("/*"))
        {
            t = t[2..];
            if (t.EndsWith("*/"))
            {
                t = t[..^2];
            }
            t = BlockStar().Replace(t, " ");
        }
        return t.Trim();
    }

    private static string UnitBody(Unit unit)
    {
        return string.Join(" ", unit.Comments.Select(c => CommentBody(c.Text))).Trim();
    }

    private static bool IsInert(Comment c)
    {
        var body = CommentBody(c.Text);
        if (body.Length == 0)
        {
            return true;
        }
        if (Directive().IsMatch(body))
        {
            return true;
        }
        if (!c.Standalone && !body.Contains('\n') && ClosingLabel().IsMatch(body))
        {
            return true;
        }

        return body.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .All(l => Divider().IsMatch(l));
    }

    private static bool IsPolicedUnit(Unit unit)
    {
        return unit.Comments.Any(c => !IsInert(c));
    }

    private static List<Comment> ExtractDocstrings(string src)
    {
        var lines = src.Split('\n');
        var results = new List<Comment>();

        for (var i = 0; i < lines.Length; i++)
        {
            if (!DefLine().IsMatch(lines[i]))
            {
                continue;
            }

            var signatureEnd = i;
            while (signatureEnd < lines.Length && !ColonEnd().IsMatch(lines[signatureEnd]))
            {
                signatureEnd++;
            }

            var start = signatureEnd + 1;
            if (start >= lines.Length)
            {
                continue;
            }

            var opener = DocOpener().Match(lines[start]);
            if (!opener.Success)
            {
                continue;
            }

            var quote = opener.Groups[1].Value;
            var body = new List<string> { lines[start] };
            var end = start;
            var singleLine = lines[start].Trim() != quote && lines[start].TrimEnd().EndsWith(quote);

            if (!singleLine)
            {
                end = start + 1;
                while (end < lines.Length && !lines[end].Contains(quote))
                {
                    body.Add(lines[end]);
                    end++;
                }
                if (end < lines.Length)
                {
                    body.Add(lines[end]);
                }
            }

            results.Add(new Comment(string.Join("\n", body), false, start, true, "docstring"));
            i = end;
        }
        return results;
    }

    private static List<string> DocstringBoilerplate(string src)
    {
        var found = new List<string>();
        var pos = 0;

        while (pos < src.Length)
        {
            var m = TripleQuote().Match(src, pos);
            if (!m.Success)
            {
                break;
            }

            var quote = m.Groups[1].Value;
            var bodyStart = m.Index + 3;
            if (bodyStart > src.Length)
            {
                break;
            }

            var close = src.IndexOf(quote, bodyStart, StringComparison.Ordinal);
            if (close == -1)
            {
                break;
            }

            var body = src[bodyStart..close];
            foreach (var line in body.Split('\n'))
            {
                if (DocSection().IsMatch(line) || DocField().IsMatch(line))
                {
                    var trimmed = line.Trim();
                    found.Add(trimmed.Length > 80 ? trimmed[..80] : trimmed);
                }
            }
            pos = close + 3;
        }

        return found.Distinct().ToList();
    }
}
