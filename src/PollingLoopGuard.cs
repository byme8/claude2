using System.Text.RegularExpressions;

internal sealed partial class PollingLoopGuard : Law
{
    [GeneratedRegex(@"(?:^|[\s;&|(""'])(?:while|until)\s[\s\S]*?(?:;|\n)\s*do\b[\s\S]*?\bdone\b")]
    private static partial Regex ConditionLoop();

    [GeneratedRegex(@"(?:^|[\s;&|(""'])for\s*\(\(\s*[^;)]*;\s*;[^)]*\)\)")]
    private static partial Regex ForeverFor();

    [GeneratedRegex(@"(?:^|[;&|(\n""']|\b(?:then|do|exec|-c)\s)\s*watch\s")]
    private static partial Regex Watch();

    [GeneratedRegex(@"(?:^|[\s;&|(""'])sleep\b")]
    private static partial Regex Sleep();

    public override string Name
    {
        get { return "polling-loop"; }
    }

    public override IReadOnlyList<string> Tools
    {
        get { return ["Bash"]; }
    }

    protected override GuardResult Evaluate(ToolInput? input, string cwd)
    {
        var command = input?.Command ?? "";
        string? shape = null;
        if (Watch().IsMatch(command))
        {
            shape = "watch";
        }
        else if (ConditionLoop().Matches(command).Any(m => Sleep().IsMatch(m.Value)))
        {
            shape = "a while/until loop with sleep";
        }
        else if (ForeverFor().IsMatch(command) && Sleep().IsMatch(command))
        {
            shape = "a for ((;;)) loop with sleep";
        }

        if (shape is null)
        {
            return GuardResult.Pass();
        }

        return Decide("deny", "RULE-no-polling-loop", $@"This command polls with {shape}. Never wait in a shell polling loop.

Wait on an event instead: run the job with run_in_background and wait for its completion notification, use the Monitor tool with an until-condition, or schedule a check with CronCreate / ScheduleWakeup.");
    }
}
