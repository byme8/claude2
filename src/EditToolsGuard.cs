using System.Text;
using System.Text.RegularExpressions;

internal sealed partial class EditToolsGuard : Law
{
    [GeneratedRegex(@"^-[a-zA-Z]{0,3}i")]
    private static partial Regex InPlaceFlag();

    public override string Name
    {
        get { return "edit-tools"; }
    }

    public override IReadOnlyList<string> Tools
    {
        get { return ["Bash", "PowerShell"]; }
    }

    private static readonly string TempDir = Paths.Slashed(Fs.Canonical(Env.TempDir));

    private static readonly string[] ScratchRoots =
        [TempDir, Paths.Slashed(Fs.Canonical(Paths.FromMsys("/tmp")))];

    private enum Kind { Word, Separator, Redirect, Input }

    private sealed record Token(Kind Kind, string Text);

    protected override GuardResult Evaluate(ToolInput? input, string cwd)
    {
        var command = input?.Command ?? "";
        foreach (var (shape, target, dir) in Writes(Lex(command), cwd))
        {
            if (!IsScratch(target, dir))
            {
                return Decide("deny", "RULE-edit-tools-only", $@"`{shape}` writes '{target}', which is outside the OS temp directory.

Project-file changes go through the Edit or Write tools, one call per file — never a shell redirect, tee, sed -i, perl -i or truncate. Output you only want to inspect belongs in the OS temp directory: redirect it to '{TempDir}/<name>' and the command will run.");
            }
        }
        return GuardResult.Pass();
    }

    private static IEnumerable<(string Shape, string Target, string Dir)> Writes(List<Token> tokens, string cwd)
    {
        var dir = cwd;
        var outer = new Stack<string>();
        var start = 0;
        for (var i = 0; i <= tokens.Count; i++)
        {
            if (i < tokens.Count && tokens[i].Kind != Kind.Separator)
            {
                continue;
            }

            var segment = tokens.GetRange(start, i - start);
            start = i + 1;

            for (var j = 0; j + 1 < segment.Count; j++)
            {
                if (segment[j].Kind == Kind.Redirect && segment[j + 1].Kind == Kind.Word)
                {
                    yield return (segment[j].Text, segment[j + 1].Text, dir);
                }
            }

            if (segment.Count == 2 && segment[0].Text is "cd" or "Set-Location" or "pushd" && segment[1].Kind == Kind.Word)
            {
                var path = Paths.Expand(segment[1].Text);
                dir = path.Contains('$') || path.Contains('`') ? "" : Paths.Absolute(dir.Length > 0 ? dir : "/", path);
            }

            if (i < tokens.Count && tokens[i].Text == "(")
            {
                outer.Push(dir);
            }
            else if (i < tokens.Count && tokens[i].Text == ")" && outer.Count > 0)
            {
                dir = outer.Pop();
            }

            var words = new List<string>();
            for (var j = 0; j < segment.Count; j++)
            {
                if (segment[j].Kind == Kind.Word && (j == 0 || segment[j - 1].Kind == Kind.Word))
                {
                    words.Add(segment[j].Text);
                }
            }

            var at = words.FindIndex(w => !w.Contains('='));
            if (at < 0)
            {
                continue;
            }
            var program = Path.GetFileName(words[at]);
            var args = words.Skip(at + 1).ToList();

            if (program == "tee")
            {
                foreach (var file in args.Where(a => !a.StartsWith('-')))
                {
                    yield return ("tee", file, dir);
                }
                continue;
            }

            var shape = program switch
            {
                "sed" or "perl" when args.Any(a => a.StartsWith("--in-place") || InPlaceFlag().IsMatch(a)) => $"{program} -i",
                "truncate" => "truncate",
                _ => null,
            };
            if (shape is not null && args.Count > 0 && !args[^1].StartsWith('-'))
            {
                yield return (shape, args[^1], dir);
            }
        }
    }

    private static bool IsScratch(string target, string cwd)
    {
        var path = target;
        foreach (var variable in new[] { "${TMPDIR}", "$TMPDIR" })
        {
            if (path.StartsWith(variable))
            {
                path = Env.TempDir + path[variable.Length..];
            }
        }

        if (path.Contains('$') || path.Contains('`'))
        {
            return true;
        }

        if (Paths.IsUnder(path, "/dev"))
        {
            return true;
        }

        var absolute = Paths.Absolute(cwd.Length > 0 ? cwd : "/", Paths.Expand(path));
        return ScratchRoots.Any(root => Paths.IsUnder(absolute, root));
    }

    private static List<Token> Lex(string command)
    {
        var tokens = new List<Token>();
        var word = new StringBuilder();
        var inWord = false;
        var heredocs = new List<(string Delimiter, bool StripTabs)>();

        void Flush()
        {
            if (inWord)
            {
                tokens.Add(new Token(Kind.Word, word.ToString()));
            }
            word.Clear();
            inWord = false;
        }

        var i = 0;
        while (i < command.Length)
        {
            var c = command[i];
            var next = i + 1 < command.Length ? command[i + 1] : '\0';

            if (c == '\\' && i + 1 < command.Length)
            {
                word.Append(next);
                inWord = true;
                i += 2;
            }
            else if (c == '"')
            {
                i++;
                while (i < command.Length && command[i] != '"')
                {
                    if (command[i] == '\\' && i + 1 < command.Length && command[i + 1] is '"' or '\\' or '$' or '`' or '\n')
                    {
                        word.Append(command[i + 1]);
                        i += 2;
                    }
                    else
                    {
                        word.Append(command[i]);
                        i++;
                    }
                }
                inWord = true;
                i++;
            }
            else if (c == '\'')
            {
                var close = command.IndexOf(c, i + 1);
                if (close < 0)
                {
                    close = command.Length;
                }
                word.Append(command, i + 1, close - i - 1);
                inWord = true;
                i = close + 1;
            }
            else if (c == '#' && !inWord)
            {
                var end = command.IndexOf('\n', i);
                i = end < 0 ? command.Length : end;
            }
            else if (c == ' ' || c == '\t')
            {
                Flush();
                i++;
            }
            else if (c == '\n')
            {
                Flush();
                tokens.Add(new Token(Kind.Separator, "\n"));
                i = SkipHeredocBodies(command, i + 1, heredocs);
                heredocs.Clear();
            }
            else if (c == ';' || c == '|' || c == '(' || c == ')' || (c == '&' && next != '>'))
            {
                Flush();
                tokens.Add(new Token(Kind.Separator, c.ToString()));
                i++;
            }
            else if (c == '>' || c == '&')
            {
                if (inWord && word.ToString().All(char.IsDigit))
                {
                    word.Clear();
                    inWord = false;
                }
                Flush();

                var j = c == '&' ? i + 1 : i;
                var op = new StringBuilder(c == '&' ? "&" : "");
                while (j < command.Length && command[j] == '>')
                {
                    op.Append('>');
                    j++;
                }
                if (j < command.Length && command[j] == '|')
                {
                    j++;
                }

                if (j < command.Length && command[j] == '&')
                {
                    j++;
                    while (j < command.Length && (char.IsDigit(command[j]) || command[j] == '-'))
                    {
                        j++;
                    }
                }
                else if (j < command.Length && command[j] == '(')
                {
                    tokens.Add(new Token(Kind.Separator, "("));
                    j++;
                }
                else
                {
                    tokens.Add(new Token(Kind.Redirect, op.ToString()));
                }
                i = j;
            }
            else if (c == '<')
            {
                Flush();
                if (next == '<' && i + 2 < command.Length && command[i + 2] == '<')
                {
                    tokens.Add(new Token(Kind.Input, "<<<"));
                    i += 3;
                }
                else if (next == '<')
                {
                    i = ReadHeredocDelimiter(command, i + 2, heredocs);
                    tokens.Add(new Token(Kind.Input, "<<"));
                }
                else
                {
                    tokens.Add(new Token(Kind.Input, "<"));
                    i++;
                }
            }
            else
            {
                word.Append(c);
                inWord = true;
                i++;
            }
        }
        Flush();
        return tokens;
    }

    private static int ReadHeredocDelimiter(string command, int i, List<(string Delimiter, bool StripTabs)> heredocs)
    {
        var stripTabs = i < command.Length && command[i] == '-';
        if (stripTabs)
        {
            i++;
        }
        while (i < command.Length && (command[i] == ' ' || command[i] == '\t'))
        {
            i++;
        }

        var delimiter = new StringBuilder();
        while (i < command.Length && !char.IsWhiteSpace(command[i]) && command[i] is not (';' or '|' or '&' or '>' or '<' or ')'))
        {
            if (command[i] is not ('\'' or '"' or '\\'))
            {
                delimiter.Append(command[i]);
            }
            i++;
        }

        if (delimiter.Length > 0)
        {
            heredocs.Add((delimiter.ToString(), stripTabs));
        }
        return i;
    }

    private static int SkipHeredocBodies(string command, int i, List<(string Delimiter, bool StripTabs)> heredocs)
    {
        foreach (var (delimiter, stripTabs) in heredocs)
        {
            while (i < command.Length)
            {
                var end = command.IndexOf('\n', i);
                if (end < 0)
                {
                    end = command.Length;
                }
                var line = command[i..end];
                i = Math.Min(end + 1, command.Length);
                if ((stripTabs ? line.TrimStart('\t') : line) == delimiter)
                {
                    break;
                }
            }
        }
        return i;
    }
}
