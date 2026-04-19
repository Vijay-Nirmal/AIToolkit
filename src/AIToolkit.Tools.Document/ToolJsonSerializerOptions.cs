using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace AIToolkit.Tools.Document;

/// <summary>
/// Creates serializer options used for document-tool metadata and logging payloads.
/// </summary>
/// <remarks>
/// The document tools rely on web defaults plus a mutable type-info resolver so Microsoft.Extensions.AI can reflect over
/// the internal service methods that back the public AI functions.
/// </remarks>
internal static class ToolJsonSerializerOptions
{
    /// <summary>
    /// Creates JSON serializer options aligned with the web default profile.
    /// </summary>
    public static JsonSerializerOptions CreateWeb() =>
        new(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
}
