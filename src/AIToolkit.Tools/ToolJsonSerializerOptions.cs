using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace AIToolkit.Tools;

/// <summary>
/// Creates resolver-backed serializer options for tool metadata and results.
/// </summary>
/// <remarks>
/// The tool factory layer uses these helpers so every generated <see cref="Microsoft.Extensions.AI.AIFunction"/>
/// serializes arguments and results consistently, even when reflection-based metadata is trimmed differently
/// across hosts.
/// </remarks>
internal static class ToolJsonSerializerOptions
{
    /// <summary>
    /// Creates JSON options aligned with the web defaults used by tool metadata and structured results.
    /// </summary>
    /// <returns>A new <see cref="JsonSerializerOptions"/> instance configured for web-style JSON.</returns>
    public static JsonSerializerOptions CreateWeb() =>
        new(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };

    /// <summary>
    /// Creates JSON options that emit indented output for persisted notebook content.
    /// </summary>
    /// <returns>A new <see cref="JsonSerializerOptions"/> instance configured for readable output.</returns>
    public static JsonSerializerOptions CreateIndented() =>
        new()
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            WriteIndented = true,
        };
}
