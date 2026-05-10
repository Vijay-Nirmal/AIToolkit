using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace AIToolkit.Agent.Office;

/// <summary>
/// Provides AIToolkit-owned Microsoft.Extensions.AI entry points over the vendored OfficeCLI engine.
/// </summary>
/// <remarks>
/// This type intentionally stays outside the mirrored upstream files so stable <c>AIFunction</c> wrappers can grow
/// without creating sync conflicts with the upstream OfficeCLI codebase.
/// </remarks>
public static class OfficeAgentTools
{
    private const string SystemPromptGuidance = """
        Use the office tool to create, inspect, and modify .docx, .xlsx, and .pptx files.
        When a skill loader is available and no Office skill has been loaded yet, you MUST load the main office skill (skill named `office`) before loading any specialized office-* skill.
        If command syntax, DOM paths, property names, or value formats are unclear, call office with the help command before making changes.
        Prefer structured tool use over guessing, and briefly state which file you changed or inspected.
        """;
    private static readonly JsonSerializerOptions ToolSerializerOptions = CreateToolSerializerOptions();

    /// <summary>
    /// Gets concise prompt guidance for hosts that expose the unified <c>office</c> tool.
    /// </summary>
    /// <returns>A short system prompt that explains when and how to use the <c>office</c> tool.</returns>
    public static string GetSystemPromptGuidance() => SystemPromptGuidance;

    /// <summary>
    /// Creates the full Office tool set exposed by this package.
    /// </summary>
    /// <returns>The unified <c>office</c> AI function in registration order.</returns>
    public static IReadOnlyList<AIFunction> CreateFunctions() =>
    [
        CreateFunction(),
    ];

    /// <summary>
    /// Creates the unified <c>office</c> AI function.
    /// </summary>
    /// <returns>An AI function that mirrors the vendored <c>officecli</c> MCP tool behavior.</returns>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var office = OfficeAgentTools.CreateFunction();
    /// var result = await office.InvokeAsync(new AIFunctionArguments
    /// {
    ///     ["command"] = "help",
    ///     ["format"] = "docx",
    /// });
    /// ]]></code>
    /// </example>
    public static AIFunction CreateFunction() => new OfficeAIFunctionFactory(new OfficeToolService()).CreateOffice();

    /// <summary>
    /// Creates the unified <c>office</c> AI tool.
    /// </summary>
    /// <returns>The same tool instance returned by <see cref="CreateFunction"/>, surfaced as an <see cref="AITool"/>.</returns>
    public static AITool CreateTool() => CreateFunction();

    internal static JsonSerializerOptions SerializerOptions => ToolSerializerOptions;

    private static JsonSerializerOptions CreateToolSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
