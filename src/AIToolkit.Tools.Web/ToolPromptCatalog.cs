using System.Globalization;

namespace AIToolkit.Tools.Web;

/// <summary>
/// Centralizes the tool descriptions and prompt guidance emitted by the web tool family.
/// </summary>
/// <remarks>
/// <see cref="WebTools"/> uses this catalog when building host guidance, and <see cref="WebAIFunctionFactory"/> uses
/// it to keep the externally visible <c>web_fetch</c> and <c>web_search</c> descriptions stable even if the internal
/// implementation changes.
/// </remarks>
internal static class ToolPromptCatalog
{
    /// <summary>
    /// Appends a guidance block to an existing system prompt.
    /// </summary>
    /// <param name="currentSystemPrompt">The existing prompt text, if any.</param>
    /// <param name="guidance">The guidance block to append.</param>
    /// <returns>
    /// The original prompt when it already contains text followed by the appended guidance, or just
    /// <paramref name="guidance"/> when the original prompt is empty.
    /// </returns>
    public static string AppendSystemPromptSection(string? currentSystemPrompt, string guidance)
    {
        if (string.IsNullOrWhiteSpace(currentSystemPrompt))
        {
            return guidance;
        }

        return string.Join("\n\n", currentSystemPrompt, guidance);
    }

    /// <summary>
    /// Gets the model-facing description for the shared <c>web_fetch</c> tool.
    /// </summary>
    /// <remarks>
    /// The description intentionally calls out caching, redirect confirmation, and the preference for host-provided web
    /// tooling because those behaviors materially affect how agents should decide between tool families.
    /// </remarks>
    public static string WebFetchDescription =>
        """
        - Fetches content from a specified URL and returns normalized content for the agent to analyze
        - Takes a URL and an optional prompt as input
        - Fetches the URL content and converts HTML to markdown
        - Uses the prompt as a relevance hint to trim the returned content when helpful
        - Returns the fetched content plus HTTP metadata
        - Use this tool when you need to retrieve and analyze web content

        Usage notes:
          - IMPORTANT: If an MCP-provided or host-provided web fetch tool is available, prefer using that tool instead of this one, as it may have fewer restrictions.
          - The URL must be a fully-formed valid URL
          - HTTP URLs will be automatically upgraded to HTTPS
          - The prompt should describe what information you want to extract from the page
          - This tool is read-only and does not modify any files
          - Results may be truncated if the content is very large
          - Includes a self-cleaning 15-minute cache for faster responses when repeatedly accessing the same URL
          - When a URL redirects to a different host, the tool will return the redirect URL so you can make an explicit follow-up request
          - For GitHub URLs, prefer using GitHub-specific tools when your host provides them
        """;

    /// <summary>
    /// Gets the model-facing description for the shared <c>web_search</c> tool.
    /// </summary>
    /// <remarks>
    /// The description includes the mandatory sources guidance so models consistently cite URLs when search results
    /// materially inform an answer.
    /// </remarks>
    public static string WebSearchDescription =>
        $"""
        - Allows the agent to search the web and use the results to inform responses
        - Provides up-to-date information for current events and recent data
        - Returns structured search result information including titles, URLs, and snippets
        - Use this tool for accessing information beyond the model's knowledge cutoff

        CRITICAL REQUIREMENT - You MUST follow this:
          - After answering the user's question, you MUST include a \"Sources:\" section at the end of your response
          - In the Sources section, list all relevant URLs from the search results as markdown hyperlinks: [Title](URL)
          - This is MANDATORY - never skip including sources in your response when web_search materially informed the answer

        Usage notes:
          - Domain filtering is supported to include or block specific websites

        IMPORTANT - Use the correct year in search queries:
          - The current month is {DateTimeOffset.Now.ToString("MMMM yyyy", CultureInfo.InvariantCulture)}. You MUST use this year when searching for recent information, documentation, or current events.
          - Example: If the user asks for latest React docs, search for React documentation with the current year, not last year
        """;

    /// <summary>
    /// Creates the reusable system-prompt guidance block for the web tools.
    /// </summary>
    /// <returns>A concise prompt section that teaches a host when to use <c>web_fetch</c> and <c>web_search</c>.</returns>
    public static string GetWebSystemPromptGuidance()
    {
        var currentMonthYear = DateTimeOffset.Now.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

        return string.Join(
            "\n",
            [
                "# Using web tools",
                "- Use web_search for fresh or current information.",
                $"- The current month is {currentMonthYear}. Use this year for freshness-sensitive searches.",
                "- Use web_fetch for a specific public URL.",
            ]);
    }
}
