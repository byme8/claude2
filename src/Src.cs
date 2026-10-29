internal sealed record Comment(string Text, bool Doc, int Line, bool Standalone, string Kind);

internal sealed record Unit(List<Comment> Comments, string Kind);

internal sealed record Syntax(
    HashSet<char> Quotes,
    bool NestedBlockComments,
    bool TripleQuoted,
    bool RawStringHashes,
    bool Interpolation,
    bool BacktickIdentifiers,
    bool HashLineComments,
    bool RustLiterals);

internal static class Src
{
    public static readonly Syntax CFamily = new(['"', '\'', '`'], false, false, false, false, false, false, false);
    public static readonly Syntax Swift = new(['"'], true, true, true, true, true, false, false);
    public static readonly Syntax Python = new(['"', '\''], false, true, false, false, false, true, false);
    public static readonly Syntax Kotlin = new(['"', '\''], true, true, false, false, true, false, false);
    public static readonly Syntax Rust = new(['"'], true, false, false, false, false, false, true);
    public static readonly Syntax Wgsl = new([], true, false, false, false, false, false, false);

    public static Syntax For(string extension)
    {
        return extension switch
        {
            ".swift" => Swift,
            ".rs" => Rust,
            ".wgsl" => Wgsl,
            ".kt" or ".kts" => Kotlin,
            ".py" or ".pyi" => Python,
            _ => CFamily,
        };
    }

    public static string Ext(string p)
    {
        var slash = Math.Max(p.LastIndexOf('/'), p.LastIndexOf('\\'));
        var name = slash >= 0 ? p[(slash + 1)..] : p;
        var dot = name.LastIndexOf('.');
        if (dot > 0)
        {
            return name[dot..].ToLowerInvariant();
        }
        return "";
    }

    private static int CountNewlines(string src, int from, int to)
    {
        var count = 0;
        for (var i = from; i < to; i++)
        {
            if (src[i] == '\n')
            {
                count++;
            }
        }
        return count;
    }

    public static int SkipStringLiteral(string src, int start, Syntax syntax)
    {
        var n = src.Length;
        var i = start;
        var hashes = 0;

        if (syntax.RawStringHashes && Js.At(src, i) == '#')
        {
            var h = i;
            while (h < n && src[h] == '#')
            {
                h++;
            }
            if (Js.At(src, h) != '"')
            {
                return -1;
            }
            hashes = h - i;
            i = h;
        }

        var opener = Js.At(src, i);
        if (!syntax.Quotes.Contains(opener))
        {
            return -1;
        }

        var triple = syntax.TripleQuoted && opener == '"' && Js.StartsAt(src, i, "\"\"\"");
        var closer = (triple ? "\"\"\"" : opener.ToString()) + new string('#', hashes);
        var escape = "\\" + new string('#', hashes);
        i += triple ? 3 : 1;

        while (i < n)
        {
            if (Js.StartsAt(src, i, escape))
            {
                var afterEscape = i + escape.Length;
                if (syntax.Interpolation && Js.At(src, afterEscape) == '(')
                {
                    i = SkipInterpolation(src, afterEscape, syntax);
                    continue;
                }
                i = afterEscape + 1;
                continue;
            }
            if (Js.StartsAt(src, i, closer))
            {
                return i + closer.Length;
            }
            i++;
        }
        return n;
    }

    private static int SkipInterpolation(string src, int openParen, Syntax syntax)
    {
        var n = src.Length;
        var i = openParen + 1;
        var depth = 1;

        while (i < n && depth > 0)
        {
            var c = src[i];
            if (c == '"' || (syntax.RawStringHashes && c == '#'))
            {
                var end = SkipStringLiteral(src, i, syntax);
                if (end > i)
                {
                    i = end;
                    continue;
                }
            }
            if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth--;
            }
            i++;
        }
        return i;
    }

    private static bool IsIdentChar(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_';
    }

    private static int SkipRustChar(string src, int start)
    {
        var first = Js.At(src, start + 1);
        if (first == '\\')
        {
            var k = start + 3;
            while (k < src.Length && src[k] != '\'' && src[k] != '\n')
            {
                k++;
            }
            return Js.At(src, k) == '\'' ? k + 1 : -1;
        }

        if (first is '\0' or '\n' or '\'')
        {
            return -1;
        }
        var width = char.IsHighSurrogate(first) ? 2 : 1;
        var close = start + 1 + width;
        return Js.At(src, close) == '\'' ? close + 1 : -1;
    }

    private static int SkipRustPrefixedLiteral(string src, int start)
    {
        if (IsIdentChar(Js.At(src, start - 1)))
        {
            return -1;
        }

        var i = start;
        if (Js.At(src, i) is 'b' or 'c')
        {
            i++;
        }

        if (Js.At(src, i) == 'r')
        {
            var quote = i + 1;
            while (Js.At(src, quote) == '#')
            {
                quote++;
            }
            if (Js.At(src, quote) != '"')
            {
                return -1;
            }
            var closer = "\"" + new string('#', quote - i - 1);
            var close = src.IndexOf(closer, quote + 1, StringComparison.Ordinal);
            return close < 0 ? src.Length : close + closer.Length;
        }

        if (i == start)
        {
            return -1;
        }
        return Js.At(src, i) switch
        {
            '"' => SkipStringLiteral(src, i, Rust),
            '\'' when src[start] == 'b' => SkipRustChar(src, i),
            _ => -1,
        };
    }

    public static List<Comment> ExtractComments(string src, Syntax syntax)
    {
        var results = new List<Comment>();
        var n = src.Length;
        var i = 0;
        var line = 0;
        var lineIsBlankSoFar = true;

        while (i < n)
        {
            var c = src[i];
            var c2 = Js.At(src, i + 1);

            if (syntax.RustLiterals && c is '\'' or 'r' or 'b' or 'c')
            {
                var end = c == '\'' ? SkipRustChar(src, i) : SkipRustPrefixedLiteral(src, i);
                if (end > i)
                {
                    lineIsBlankSoFar = false;
                    line += CountNewlines(src, i, end);
                    i = end;
                    continue;
                }
            }

            if (syntax.Quotes.Contains(c) || (syntax.RawStringHashes && c == '#'))
            {
                var end = SkipStringLiteral(src, i, syntax);
                if (end > i)
                {
                    lineIsBlankSoFar = false;
                    line += CountNewlines(src, i, end);
                    i = end;
                    continue;
                }
            }

            if (syntax.BacktickIdentifiers && c == '`')
            {
                lineIsBlankSoFar = false;
                i++;
                while (i < n && src[i] != '`' && src[i] != '\n')
                {
                    i++;
                }
                if (Js.At(src, i) == '`')
                {
                    i++;
                }
                continue;
            }

            if (syntax.HashLineComments && c == '#')
            {
                var hashStart = i;
                var hashLine = line;
                var hashStandalone = lineIsBlankSoFar;
                i++;
                while (i < n && src[i] != '\n')
                {
                    i++;
                }
                results.Add(new Comment(src[hashStart..i], false, hashLine, hashStandalone, "line"));
                continue;
            }

            if (c == '/' && c2 == '/')
            {
                var start = i;
                var startLine = line;
                var standalone = lineIsBlankSoFar;
                var doc = Js.At(src, i + 2) == '/';
                i += 2;
                while (i < n && src[i] != '\n')
                {
                    i++;
                }
                results.Add(new Comment(src[start..i], doc, startLine, standalone, "line"));
                continue;
            }

            if (c == '/' && c2 == '*')
            {
                var start = i;
                var startLine = line;
                var doc = Js.At(src, i + 2) == '*';
                i += 2;
                var depth = 1;
                while (i < n && depth > 0)
                {
                    if (syntax.NestedBlockComments && Js.At(src, i) == '/' && Js.At(src, i + 1) == '*')
                    {
                        depth++;
                        i += 2;
                        continue;
                    }
                    if (Js.At(src, i) == '*' && Js.At(src, i + 1) == '/')
                    {
                        depth--;
                        i += 2;
                        continue;
                    }
                    if (Js.At(src, i) == '\n')
                    {
                        line++;
                    }
                    i++;
                }
                results.Add(new Comment(src[start..Math.Min(i, n)], doc, startLine, lineIsBlankSoFar, "block"));
                lineIsBlankSoFar = false;
                continue;
            }

            if (c == '\n')
            {
                line++;
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

    public static List<Unit> GroupUnits(List<Comment> comments)
    {
        var units = new List<Unit>();
        List<Comment>? run = null;

        void Flush()
        {
            if (run is not null)
            {
                units.Add(new Unit(run, "line"));
                run = null;
            }
        }

        foreach (var c in comments)
        {
            var prev = run is not null ? run[^1] : null;
            var contiguous = run is not null
                && c.Kind == "line"
                && c.Standalone
                && prev is not null
                && c.Line == prev.Line + 1
                && c.Doc == prev.Doc;

            if (c.Kind == "line" && c.Standalone)
            {
                if (contiguous)
                {
                    run!.Add(c);
                }
                else
                {
                    Flush();
                    run = [c];
                }
                continue;
            }

            Flush();
            units.Add(new Unit([c], c.Kind));
        }

        Flush();
        return units;
    }
}
