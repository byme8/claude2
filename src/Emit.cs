using System.Text.Encodings.Web;
using System.Text.Json;

internal static class Emit
{
    private static readonly JsonSerializerOptions Options =
        new(HookJson.Default.Options) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly HookJson Context = new(Options);

    public static void Decision(string decision, string reason, string context)
    {
        Write(new HookOutput
        {
            PermissionDecision = decision,
            PermissionDecisionReason = string.IsNullOrEmpty(reason) ? null : reason,
            AdditionalContext = string.IsNullOrEmpty(context) ? null : context,
        });
    }

    public static void ContextOnly(string context)
    {
        Write(new HookOutput { AdditionalContext = context });
    }

    private static void Write(HookOutput output)
    {
        Console.Out.Write(JsonSerializer.Serialize(
            new HookEnvelope { HookSpecificOutput = output }, Context.HookEnvelope));
    }
}
