internal static class Js
{
    private static readonly char[] QuoteChars = ['"', '\''];
    private static readonly char[] GlobChars = ['*', '?'];
    private static readonly char[] GlobOrVarChars = ['*', '?', '$'];
    private static readonly char[] ShellControlChars = [';', '&', '|', '\n', '>', '<'];

    public static char At(string s, int i)
    {
        return i >= 0 && i < s.Length ? s[i] : '\0';
    }

    public static bool StartsAt(string s, int i, string needle)
    {
        return i >= 0
            && i + needle.Length <= s.Length
            && string.CompareOrdinal(s, i, needle, 0, needle.Length) == 0;
    }

    public static string StripQuotes(string t)
    {
        if (t.Length > 0 && QuoteChars.Contains(t[0]))
        {
            t = t[1..];
        }
        if (t.Length > 0 && QuoteChars.Contains(t[^1]))
        {
            t = t[..^1];
        }
        return t;
    }

    public static string[] Words(string s)
    {
        return s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }

    public static string LastSegment(string token)
    {
        var parts = token.Split('/', '\\');
        return parts.Length == 0 ? "" : parts[^1];
    }

    public static string TrimExe(string token)
    {
        return token.EndsWith(".exe", StringComparison.Ordinal) ? token[..^4] : token;
    }

    public static bool HasGlob(string s)
    {
        return s.IndexOfAny(GlobChars) >= 0;
    }

    public static bool HasGlobOrVar(string s)
    {
        return s.IndexOfAny(GlobOrVarChars) >= 0;
    }

    public static bool HasSubstitution(string s)
    {
        return s.Contains("$(", StringComparison.Ordinal)
            || s.Contains('`')
            || s.Contains("${", StringComparison.Ordinal);
    }

    public static bool HasShellControl(string s)
    {
        return HasSubstitution(s) || s.IndexOfAny(ShellControlChars) >= 0;
    }

    public static bool IsEnvAssignment(string token)
    {
        var eq = token.IndexOf('=');
        if (eq <= 0)
        {
            return false;
        }

        var name = token[..eq];
        if (char.IsAsciiDigit(name[0]))
        {
            return false;
        }
        return name.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_');
    }

    public static bool IsEnvAssignmentLead(string token)
    {
        var eq = token.IndexOf('=');
        if (eq <= 0)
        {
            return false;
        }

        var name = token[..eq];
        if (!char.IsAsciiLetter(name[0]) && name[0] != '_')
        {
            return false;
        }
        return name.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_');
    }

    public static bool IsDriveLetter(string token)
    {
        return token.Length > 1 && char.IsAsciiLetter(token[0]) && token[1] == ':';
    }

    public static bool IsClaudeSession(string segment)
    {
        const string prefix = "claude-";
        if (!segment.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var digits = segment[prefix.Length..];
        return digits.Length > 0 && digits.All(char.IsAsciiDigit);
    }

    public static string Squash(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        var inRun = false;

        foreach (var ch in s)
        {
            if (char.IsWhiteSpace(ch))
            {
                inRun = true;
                continue;
            }
            if (inRun && sb.Length > 0)
            {
                sb.Append(' ');
            }
            inRun = false;
            sb.Append(ch);
        }
        return sb.ToString();
    }
}

internal static class Fs
{
    public static bool Exists(string p)
    {
        return File.Exists(p) || Directory.Exists(p);
    }

    private static string? LinkTarget(string p)
    {
        try
        {
            var di = new DirectoryInfo(p);
            if (di.Exists)
            {
                return di.LinkTarget;
            }

            var fi = new FileInfo(p);
            if (fi.Exists)
            {
                return fi.LinkTarget;
            }
        }
        catch
        {
            return null;
        }
        return null;
    }

    public static string RealPath(string path)
    {
        var full = Path.GetFullPath(path);
        var parts = full.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var cur = "/";

        foreach (var part in parts)
        {
            cur = Path.Combine(cur, part);
            for (var i = 0; i < 40; i++)
            {
                var target = LinkTarget(cur);
                if (target is null)
                {
                    break;
                }
                cur = Path.IsPathRooted(target)
                    ? target
                    : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(cur) ?? "/", target));
            }
        }
        return cur;
    }

    public static string Canonical(string path)
    {
        var cur = Path.GetFullPath(path);
        var tail = new List<string>();

        for (var hops = 0; hops < 64; hops++)
        {
            if (Exists(cur))
            {
                break;
            }

            var parent = Path.GetDirectoryName(cur);
            if (string.IsNullOrEmpty(parent) || parent == cur)
            {
                break;
            }

            tail.Insert(0, Path.GetFileName(cur));
            cur = parent;
        }

        try
        {
            cur = RealPath(cur);
        }
        catch
        {
            cur = Path.GetFullPath(cur);
        }

        if (tail.Count == 0)
        {
            return cur;
        }

        foreach (var t in tail)
        {
            cur = Path.Combine(cur, t);
        }
        return cur;
    }
}

internal static class Env
{
    public static string Home
    {
        get { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); }
    }

    public static string TempDir
    {
        get { return Path.TrimEndingDirectorySeparator(Path.GetTempPath()); }
    }
}
