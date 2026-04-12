using System.Globalization;

namespace AIToolkit.Tools.Web;

/// <summary>
/// Holds the Claude-inspired tool descriptions and prompt append text for the web tool family.
/// </summary>
internal static class ToolPromptCatalog
{
    public static string AppendSystemPromptSection(string? currentSystemPrompt, string guidance)
    {
        if (string.IsNullOrWhiteSpace(currentSystemPrompt))
        {
            return guidance;
        }

        return string.Join("\n\n", currentSystemPrompt, guidance);
    }

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