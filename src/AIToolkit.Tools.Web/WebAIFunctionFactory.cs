using Microsoft.Extensions.AI;
using System.Reflection;

namespace AIToolkit.Tools.Web;

/// <summary>
/// Bridges <see cref="WebToolService"/> methods into stable public AI function definitions.
/// </summary>
/// <remarks>
/// This factory keeps tool names, descriptions, and serializer settings separate from the service implementation so the
/// public <c>web_fetch</c> and <c>web_search</c> contracts stay stable even if the underlying method names or service
/// dependencies evolve.
/// </remarks>
/// <seealso cref="WebToolService"/>
/// <seealso cref="ToolPromptCatalog"/>
internal sealed class WebAIFunctionFactory(WebToolService toolService)
{
    private readonly WebToolService _toolService = toolService ?? throw new ArgumentNullException(nameof(toolService));

    /// <summary>
    /// Creates the full shared web tool set.
    /// </summary>
    /// <returns>The <c>web_fetch</c> and <c>web_search</c> functions in registration order.</returns>
    public IReadOnlyList<AIFunction> CreateAll() =>
    [
        CreateFetch(),
        CreateSearch(),
    ];

    /// <summary>
    /// Creates the shared <c>web_fetch</c> function.
    /// </summary>
    /// <returns>An AI function that invokes <see cref="WebToolService.FetchAsync"/>.</returns>
    public AIFunction CreateFetch() =>
        Create(nameof(WebToolService.FetchAsync), "web_fetch", ToolPromptCatalog.WebFetchDescription);

    /// <summary>
    /// Creates the shared <c>web_search</c> function.
    /// </summary>
    /// <returns>An AI function that invokes <see cref="WebToolService.SearchAsync"/>.</returns>
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
