using Microsoft.Extensions.AI;
using System.Reflection;

namespace AIToolkit.Tools.Web;

/// <summary>
/// Maps the internal web tool service methods to the public <c>web_*</c> AI function names.
/// </summary>
internal sealed class WebAIFunctionFactory(WebToolService toolService)
{
    private readonly WebToolService _toolService = toolService ?? throw new ArgumentNullException(nameof(toolService));

    public IReadOnlyList<AIFunction> CreateAll() =>
    [
        CreateFetch(),
        CreateSearch(),
    ];

    public AIFunction CreateFetch() =>
        Create(nameof(WebToolService.FetchAsync), "web_fetch", ToolPromptCatalog.WebFetchDescription);

    public AIFunction CreateSearch() =>
        Create(nameof(WebToolService.SearchAsync), "web_search", ToolPromptCatalog.WebSearchDescription);

    private AIFunction Create(string methodName, string name, string description)
    {
        var method = typeof(WebToolService).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"Method '{methodName}' was not found on {nameof(WebToolService)}.");

        return AIFunctionFactory.Create(
            method,
            _toolService,
            new AIFunctionFactoryOptions
            {
                Name = name,
                Description = description,
                SerializerOptions = ToolJsonSerializerOptions.CreateWeb(),
            });
    }
}