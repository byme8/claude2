internal sealed record GuardResult(string Name, string Decision, string Reason, string Context)
{
    public static GuardResult Pass(string context = "")
    {
        return new GuardResult("", "pass", "", context);
    }

    public static GuardResult Deny(string reason)
    {
        return new GuardResult("", "deny", reason, "");
    }

    public GuardResult Named(string name)
    {
        return this with { Name = name };
    }
}

internal abstract class Law
{
    public abstract string Name { get; }

    public abstract IReadOnlyList<string> Tools { get; }

    protected abstract GuardResult Evaluate(ToolInput? input, string cwd);

    public GuardResult Run(ToolInput? input, string cwd)
    {
        return Evaluate(input, cwd).Named(Name);
    }

    protected static GuardResult Allow()
    {
        return new GuardResult("", "allow", "", "");
    }

    protected static GuardResult Decide(string decision, string rule, string reason)
    {
        return new GuardResult("", decision, rule.Length > 0 ? $"[{rule}] {reason}" : reason, "");
    }
}

internal static class Laws
{
    private static readonly Law[] All =
    [
        new BashGuard(),
        new CommentGuard(),
        new WorkaroundGuard(),
        new EditToolsGuard(),
        new PollingLoopGuard(),
        new ShortForegroundGuard(),
    ];

    private static readonly HashSet<string> Disabled =
        (Environment.GetEnvironmentVariable("CLAUDE_PLUGIN_OPTION_DISABLED_LAWS") ?? "")
            .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static List<Law> For(string toolName)
    {
        return All.Where(l => l.Tools.Contains(toolName) && !Disabled.Contains(l.Name)).ToList();
    }
}
