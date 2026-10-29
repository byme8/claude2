#!/usr/bin/env dotnet
#:property Nullable=enable

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

var root = Setting("CLAUDE_PLUGIN_ROOT", 0) ?? Directory.GetCurrentDirectory();
var data = Setting("CLAUDE_PLUGIN_DATA", 1) ?? Path.Combine(root, ".data");
var bin = Path.Combine(data, "bin");
var stampPath = Path.Combine(bin, ".hash");
var log = Path.Combine(data, "build.log");
Directory.CreateDirectory(data);

var sources = Directory.GetFiles(Path.Combine(root, "src"), "*.cs")
    .Append(Path.Combine(root, "law.cs"))
    .OrderBy(p => p, StringComparer.Ordinal)
    .ToList();
var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
    string.Join("\n", sources.Select(p => Path.GetFileName(p) + "\n" + File.ReadAllText(p).ReplaceLineEndings("\n"))))))[..16];

using var gate = AcquireLock(Path.Combine(data, "build.lock"));

if (IsCurrent())
{
    return 0;
}

var staging = Path.Combine(data, $"build-{hash}");
if (Directory.Exists(staging))
{
    Directory.Delete(staging, true);
}

var output = new StringBuilder();
var built = Publish(staging, aot: true, output) || Publish(staging, aot: false, output);
File.WriteAllText(log, output.ToString());

if (!built)
{
    Console.WriteLine($"law: the guard build failed, so every guarded tool call is refused. See {log}. The build needs the .NET 10 SDK on PATH.");
    return 0;
}

Install(staging);
File.WriteAllText(stampPath, hash);
Directory.Delete(staging, true);
return 0;

static string? Setting(string name, int position)
{
    var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
    if (args.Length > position && args[position].Length > 0)
    {
        return args[position];
    }
    return Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;
}

bool IsCurrent()
{
    return File.Exists(Path.Combine(bin, "law.exe"))
        && File.Exists(stampPath)
        && File.ReadAllText(stampPath).Trim() == hash;
}

bool Publish(string outDir, bool aot, StringBuilder sink)
{
    var psi = new ProcessStartInfo("dotnet")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    foreach (var a in new[] { "publish", Path.Combine(root, "law.cs"), "-o", outDir, $"-p:PublishAot={(aot ? "true" : "false")}" })
    {
        psi.ArgumentList.Add(a);
    }

    sink.AppendLine($"== dotnet publish (PublishAot={aot})");
    using var proc = Process.Start(psi)!;
    var stdout = proc.StandardOutput.ReadToEndAsync();
    var stderr = proc.StandardError.ReadToEndAsync();
    proc.WaitForExit();
    sink.AppendLine(stdout.Result).AppendLine(stderr.Result);

    var apphost = Path.Combine(outDir, OperatingSystem.IsWindows() ? "law.exe" : "law");
    return proc.ExitCode == 0 && File.Exists(apphost);
}

void Install(string from)
{
    Directory.CreateDirectory(bin);
    var stamp = DateTime.UtcNow.Ticks;

    foreach (var stale in Directory.GetFiles(bin, "*.old-*"))
    {
        TryDelete(stale);
    }

    foreach (var file in Directory.GetFiles(from))
    {
        var name = Path.GetFileName(file);
        if (name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".dbg", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        var target = Path.Combine(bin, name == "law" ? "law.exe" : name);
        if (File.Exists(target))
        {
            File.Move(target, $"{target}.old-{stamp}");
        }
        File.Move(file, target);
    }
}

static void TryDelete(string path)
{
    try
    {
        File.Delete(path);
    }
    catch (IOException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }
}

static FileStream AcquireLock(string path)
{
    while (true)
    {
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            Thread.Sleep(500);
        }
    }
}
