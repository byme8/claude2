using System.Text;

internal static class RefusedDeletions
{
    public static string Path
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable("LAW_REFUSED_DELETIONS_LEDGER") ?? "";
            return overridden.Length > 0
                ? overridden
                : System.IO.Path.Combine(Env.Home, ".local", "state", "claude-law", "refused-deletions.md");
        }
    }

    public static bool Record(string launch, string cwd, IReadOnlyList<string> targets, string command)
    {
        var entry = new StringBuilder()
            .Append("## ").Append(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")).Append('\n')
            .Append('\n')
            .Append("- launch folder: `").Append(launch.Length > 0 ? launch : "(unknown: no CLAUDE_PROJECT_DIR)").Append("`\n")
            .Append("- cwd: `").Append(cwd).Append("`\n")
            .Append("- refused: ").Append(string.Join(", ", targets.Select(t => $"`{t}`"))).Append('\n')
            .Append("- command:\n")
            .Append('\n');
        foreach (var line in (command ?? "").Split('\n'))
        {
            entry.Append("        ").Append(line).Append('\n');
        }
        entry.Append('\n');
        var bytes = Encoding.UTF8.GetBytes(entry.ToString());

        try
        {
            var path = Path;
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            for (var attempt = 0; attempt < 50; attempt++)
            {
                try
                {
                    using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
                    if (stream.Length == 0)
                    {
                        var header = Encoding.UTF8.GetBytes(
                            "# Refused deletions\n\nRemovals the law hook refused because they reached outside the session's launch folder. Appended by the hook; review and prune by hand.\n\n");
                        stream.Write(header);
                    }
                    stream.Seek(0, SeekOrigin.End);
                    stream.Write(bytes);
                    stream.Flush(true);
                    return true;
                }
                catch (IOException)
                {
                    Thread.Sleep(20);
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
}
