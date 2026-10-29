using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed class EditItem
{
    [JsonPropertyName("new_string")] public string? NewString { get; set; }
}

internal sealed class ToolInput
{
    [JsonPropertyName("command")] public string? Command { get; set; }

    [JsonPropertyName("file_path")] public string? FilePath { get; set; }

    [JsonPropertyName("content")] public string? Content { get; set; }

    [JsonPropertyName("new_string")] public string? NewString { get; set; }

    [JsonPropertyName("edits")] public List<EditItem>? Edits { get; set; }

    [JsonPropertyName("timeout")] public JsonElement? Timeout { get; set; }
}

internal sealed class Payload
{
    [JsonPropertyName("tool_name")] public string? ToolName { get; set; }

    [JsonPropertyName("tool_input")] public ToolInput? ToolInput { get; set; }

    [JsonPropertyName("cwd")] public string? Cwd { get; set; }
}

internal sealed class HookOutput
{
    [JsonPropertyName("hookEventName")] public string HookEventName { get; set; } = "PreToolUse";

    [JsonPropertyName("permissionDecision")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PermissionDecision { get; set; }

    [JsonPropertyName("permissionDecisionReason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PermissionDecisionReason { get; set; }

    [JsonPropertyName("additionalContext")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AdditionalContext { get; set; }
}

internal sealed class HookEnvelope
{
    [JsonPropertyName("hookSpecificOutput")] public HookOutput HookSpecificOutput { get; set; } = new();
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Payload))]
[JsonSerializable(typeof(HookEnvelope))]
internal sealed partial class HookJson : JsonSerializerContext;

internal static class Added
{
    public static string Text(ToolInput? ti)
    {
        if (ti is null)
        {
            return "";
        }
        if (ti.Content is not null)
        {
            return ti.Content;
        }
        if (ti.NewString is not null)
        {
            return ti.NewString;
        }
        if (ti.Edits is not null)
        {
            return string.Join("\n\n", ti.Edits.Select(e => e?.NewString ?? ""));
        }
        return "";
    }
}
