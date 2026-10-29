using System.Text.RegularExpressions;

internal static partial class Paths
{
    private static StringComparison Comparison
    {
        get { return OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal; }
    }

    [GeneratedRegex(@"^/([A-Za-z])(?:/|$)")]
    private static partial Regex MsysDrive();

    public static string Slashed(string path)
    {
        var s = path.Replace('\\', '/');
        return s.Length > 1 ? s.TrimEnd('/') : s;
    }

    public static bool Same(string a, string b)
    {
        return Slashed(a).Equals(Slashed(b), Comparison);
    }

    public static bool IsUnder(string path, string root)
    {
        var p = Slashed(path);
        var r = Slashed(root);
        if (r == "/")
        {
            return p.StartsWith('/');
        }
        return p.Equals(r, Comparison) || p.StartsWith(r + "/", Comparison);
    }

    public static string ExpandHome(string token)
    {
        if (token == "~")
        {
            return Env.Home;
        }
        if (token.StartsWith("~/") || token.StartsWith("~\\"))
        {
            return Env.Home + token[1..];
        }
        if (token == "$HOME")
        {
            return Env.Home;
        }
        if (token.StartsWith("$HOME/"))
        {
            return Env.Home + token[5..];
        }
        if (string.Equals(token, "$env:USERPROFILE", StringComparison.OrdinalIgnoreCase))
        {
            return Env.Home;
        }
        if (token.Length > 16
            && token.StartsWith("$env:USERPROFILE", StringComparison.OrdinalIgnoreCase)
            && (token[16] == '/' || token[16] == '\\'))
        {
            return Env.Home + token[16..];
        }
        return token;
    }

    public static string ExpandTempDir(string token)
    {
        if (token == "$TMPDIR")
        {
            return Env.TempDir;
        }
        if (token.StartsWith("$TMPDIR/"))
        {
            return Env.TempDir + token[7..];
        }
        return token;
    }

    public static string FromMsys(string token)
    {
        if (!OperatingSystem.IsWindows())
        {
            return token;
        }
        if (token == "/tmp" || token.StartsWith("/tmp/"))
        {
            return Env.TempDir + token[4..];
        }
        var drive = MsysDrive().Match(token);
        return drive.Success ? $"{char.ToUpperInvariant(drive.Groups[1].Value[0])}:/{token[drive.Length..]}" : token;
    }

    public static string Expand(string token)
    {
        return FromMsys(ExpandHome(ExpandTempDir(token)));
    }

    public static string Absolute(string baseDir, string expanded)
    {
        var full = Path.IsPathRooted(expanded)
            ? expanded
            : Path.Combine(baseDir.Length > 0 ? FromMsys(baseDir) : Directory.GetCurrentDirectory(), expanded);
        return Slashed(Fs.Canonical(full));
    }

    public static bool HasParentStep(string path)
    {
        return path.Split('/', '\\').Contains("..");
    }
}
