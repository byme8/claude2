internal static class Allowlist
{
    private static readonly string[] ConfigPaths =
    [
        Path.Combine(Env.Home, ".config", "claude-law", "allow.list"),
        Path.Combine(Env.Home, ".claude", "hooks", "bash-guard-allow.list"),
    ];

    private static string ConfigPath
    {
        get { return ConfigPaths.FirstOrDefault(File.Exists) ?? ConfigPaths[0]; }
    }

    private static readonly string[] Separators = [";", "|", "&", "\n"];
    private static readonly string[] Launchers = ["bash", "sh", "zsh", "env"];

    public static HashSet<string> AllowedScripts()
    {
        string text;
        try
        {
            text = File.ReadAllText(ConfigPath);
        }
        catch
        {
            return [];
        }

        return text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(s => Path.IsPathRooted(s) ? s : Path.GetFullPath(s))
            .ToHashSet();
    }

    private static string FirstCommandSegment(string line)
    {
        var cut = line.Length;
        foreach (var sep in Separators)
        {
            var i = line.IndexOf(sep, StringComparison.Ordinal);
            if (i != -1 && i < cut)
            {
                cut = i;
            }
        }
        return line[..cut].Trim();
    }

    private static string LeadingToken(string command)
    {
        var segment = FirstCommandSegment((command ?? "").Trim());
        var tokens = Js.Words(segment).ToList();
        while (tokens.Count > 1 && Launchers.Contains(tokens[0]) && !tokens[1].Contains('='))
        {
            tokens.RemoveAt(0);
        }

        var first = tokens.Count > 0 ? tokens[0] : "";
        if (Js.IsEnvAssignment(first))
        {
            return "";
        }
        return first;
    }

    public static string LeadingScriptPath(string command, string cwd)
    {
        var first = LeadingToken(command);
        if (first.Length == 0)
        {
            return "";
        }

        if (cwd.Length > 0 && !Path.IsPathRooted(first))
        {
            return Path.GetFullPath(Path.Combine(cwd, first));
        }
        return Path.GetFullPath(first);
    }
}
