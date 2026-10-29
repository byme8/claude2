#!/usr/bin/env dotnet
#:property PublishAot=true
#:property InvariantGlobalization=true
#:property Nullable=enable
#:property LangVersion=preview
#:include src/**/*.cs

using System.Text.Json;

var raw = Console.In.ReadToEnd();

Payload? payload;
try
{
    payload = JsonSerializer.Deserialize(
        string.IsNullOrWhiteSpace(raw) ? "{}" : raw, HookJson.Default.Payload);
}
catch (JsonException)
{
    Emit.Decision("deny",
        "[RULE-guard-fail-closed] law: the hook payload could not be parsed — refusing the call (fail closed).",
        "");
    return 0;
}

var toolName = payload?.ToolName ?? "";
var input = payload?.ToolInput;
var cwd = payload?.Cwd ?? "";

var applicable = Laws.For(toolName);
if (applicable.Count == 0)
{
    return 0;
}

var results = new List<GuardResult>();
foreach (var law in applicable)
{
    try
    {
        results.Add(law.Run(input, cwd));
    }
    catch (Exception)
    {
        results.Add(GuardResult.Deny(
            $"[RULE-guard-fail-closed] law: the '{law.Name}' guard threw, so the call is refused (fail closed). Fix law.cs before retrying."));
    }
}

var carried = string.Join("\n",
    results.Select(r => (r.Context ?? "").Trim()).Where(s => s.Length > 0));

var denied = results.Where(r => r.Decision == "deny").ToList();

if (denied.Count == 0)
{
    var asked = results.Where(r => r.Decision == "ask").ToList();
    if (asked.Count > 0)
    {
        Emit.Decision("ask", Join(asked), carried);
        return 0;
    }

    var allowed = results.Where(r => r.Decision == "allow").ToList();
    if (allowed.Count > 0)
    {
        Emit.Decision("allow", Join(allowed), carried);
        return 0;
    }

    if (carried.Length > 0)
    {
        Emit.ContextOnly(carried);
    }
    return 0;
}

var broken = string.Join(", ", denied.Select(r => r.Name));
var header = denied.Count == 1
    ? ""
    : $"This call breaks {denied.Count} laws ({broken}). All must be fixed.\n\n";

Emit.Decision("deny", header + Join(denied), carried);
return 0;

static string Join(List<GuardResult> rs)
{
    return string.Join("\n\n———\n\n",
        rs.Select(r => (r.Reason ?? "").Trim()).Where(s => s.Length > 0));
}
