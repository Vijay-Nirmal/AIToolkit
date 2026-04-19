using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace AIToolkit.Tools.Web;

/// <summary>
/// Creates the JSON serializer settings shared by the web tool family.
/// </summary>
/// <remarks>
/// The web packages use the same serializer settings for both AI function metadata and structured tool results so that
/// runtime invocation, logging, and tests all observe the same naming and type-resolution behavior.
/// </remarks>
internal static class ToolJsonSerializerOptions
{
    /// <summary>
    /// Creates the serializer options used by the web tools.
    /// </summary>
    /// <returns>A new resolver-backed <see cref="JsonSerializerOptions"/> instance.</returns>
    /// <seealso cref="WebAIFunctionFactory"/>
    public static JsonSerializerOptions CreateWeb() =>
        new(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
}
