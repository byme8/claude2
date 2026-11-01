using System.Diagnostics;

internal static class OnEdit
{
    public const string ProjectHook = "tools/format-on-edit.ts";

    private static readonly string[] Tools = ["Write", "Edit", "MultiEdit"];

    public static int Run(string toolName, ToolInput? input, string cwd)
    {
        var raw = input?.FilePath ?? "";
        if (!Tools.Contains(toolName) || raw.Length == 0)
        {
            return 0;
        }

        var file = Path.GetFullPath(Path.IsPathRooted(raw) ? raw : Path.Combine(cwd, raw));
        if (!File.Exists(file) || FindHook(file) is not { } hook)
        {
            return 0;
        }

        var psi = new ProcessStartInfo("bun")
        {
            WorkingDirectory = Path.GetDirectoryName(Path.GetDirectoryName(hook))!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(hook);
        psi.ArgumentList.Add(file);

        Process proc;
        try
        {
            proc = Process.Start(psi)!;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return 0;
        }

        using (proc)
        {
            var stdout = proc.StandardOutput.ReadToEndAsync();
            var stderr = proc.StandardError.ReadToEndAsync();
            proc.WaitForExit();
            if (proc.ExitCode != 2)
            {
                return 0;
            }
            Console.Error.Write(stderr.Result.Length > 0 ? stderr.Result : stdout.Result);
            return 2;
        }
    }

    private static string? FindHook(string file)
    {
        for (var dir = Path.GetDirectoryName(file); dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (Directory.Exists(Path.Combine(dir, ".git")) || File.Exists(Path.Combine(dir, ".git")))
            {
                var hook = Path.Combine(dir, ProjectHook);
                return File.Exists(hook) ? hook : null;
            }
        }
        return null;
    }
}
