using System.Globalization;
using System.Text.Json;

internal sealed class ShortForegroundGuard : Law
{
    private const double LimitMs = 60000;

    public override string Name
    {
        get { return "short-foreground"; }
    }

    public override IReadOnlyList<string> Tools
    {
        get { return ["Bash", "PowerShell"]; }
    }

    protected override GuardResult Evaluate(ToolInput? input, string cwd)
    {
        var timeout = Number(input?.Timeout);
        if (timeout is null || timeout <= LimitMs)
        {
            return GuardResult.Pass();
        }

        return Decide("deny", "RULE-short-foreground", $@"This call sets a tool timeout of {timeout.Value.ToString(CultureInfo.InvariantCulture)} ms. A tool timeout is at most {LimitMs.ToString(CultureInfo.InvariantCulture)} ms, in the foreground or the background: a longer one only means waiting on a stuck command until it dies.

Keep the timeout ≤ {LimitMs.ToString(CultureInfo.InvariantCulture)}. For longer work, run the command with run_in_background: true and no long tool timeout, let the command enforce its own deadline (e.g. bun tools/build.ts --timeout 600), and act on its completion notification.");
    }

    private static double? Number(JsonElement? element)
    {
        if (element is not { } e)
        {
            return null;
        }
        if (e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out var n))
        {
            return n;
        }
        if (e.ValueKind == JsonValueKind.String
            && double.TryParse(e.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
        {
            return s;
        }
        return null;
    }
}
