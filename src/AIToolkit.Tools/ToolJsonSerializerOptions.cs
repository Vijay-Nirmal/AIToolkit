using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace AIToolkit.Tools;

/// <summary>
/// Creates resolver-backed serializer options for tool metadata and results.
/// </summary>
internal static class ToolJsonSerializerOptions
{
    public static JsonSerializerOptions CreateWeb() =>
        new(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };

    public static JsonSerializerOptions CreateIndented() =>
        new()
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            WriteIndented = true,
        };
}