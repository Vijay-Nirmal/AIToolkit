namespace AIToolkit.Tools.Web.Tests;

[TestClass]
public class WebToolsTests
{
    private static readonly string[] ExpectedToolNames = ["web_fetch", "web_search"];
    private static readonly string[] AllowedMicrosoftDocsDomain = ["learn.microsoft.com"];

    [TestMethod]
    public void CreateFunctionsUsesExpectedToolNames()
    {
        var functions = FunctionTestUtilities.CreateFunctions();

        CollectionAssert.AreEquivalent(
            ExpectedToolNames,
            functions.Select(static function => function.Name).ToArray());
    }

    [TestMethod]
    public void GetSystemPromptGuidanceIncludesSourcesAndCurrentYear()
    {
        var prompt = WebTools.GetSystemPromptGuidance("Base prompt");

        StringAssert.Contains(prompt, "Base prompt");
        StringAssert.Contains(prompt, "# Using web tools");
        StringAssert.Contains(prompt, DateTimeOffset.Now.Year.ToString(System.Globalization.CultureInfo.InvariantCulture));
        StringAssert.Contains(prompt, "web_fetch");
        StringAssert.Contains(prompt, "web_search");
    }

    [TestMethod]
    public async Task WebSearchAppliesAllowedDomainFilters()
    {
        var provider = new StubSearchProvider(
            new WebSearchResponse(
                Provider: "stub",
                Query: "dotnet",
                Results:
                [
                    new WebSearchResult("Docs", "https://learn.microsoft.com/dotnet", "official docs"),
                    new WebSearchResult("Elsewhere", "https://example.com/post", "third-party post"),
                ],
                DurationMilliseconds: 12));

        var functions = FunctionTestUtilities.CreateFunctions(searchProvider: provider);
        var result = await FunctionTestUtilities.InvokeAsync<WebSearchToolResult>(
            functions,
            "web_search",
            FunctionTestUtilities.CreateArguments(new
            {
                query = "dotnet",
                allowedDomains = AllowedMicrosoftDocsDomain,
            }));

        Assert.IsTrue(result.Success, result.Message);
        Assert.IsNotNull(result.Result);
        Assert.HasCount(1, result.Result.Results);
        Assert.AreEqual("https://learn.microsoft.com/dotnet", result.Result.Results[0].Url);
    }

    [TestMethod]
    public async Task WebSearchFailsWithoutProvider()
    {
        var functions = FunctionTestUtilities.CreateFunctions();
        var result = await FunctionTestUtilities.InvokeAsync<WebSearchToolResult>(
            functions,
            "web_search",
            FunctionTestUtilities.CreateArguments(new
            {
                query = "dotnet 10 release notes",
            }));

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message!, "IWebSearchProvider");
    }

    [TestMethod]
    public async Task WebFetchSurfacesRedirectConfirmationMessage()
    {
        var fetcher = new StubContentFetcher(
            new WebContentFetchResponse(
                Url: "https://example.com",
                EffectiveUrl: "https://example.com",
                StatusCode: 302,
                StatusText: "Found",
                ContentType: "text/html",
                Format: WebContentFormat.Markdown,
                Content: string.Empty,
                Bytes: 0,
                DurationMilliseconds: 5,
                RedirectUrl: "https://docs.example.net/start",
                RedirectRequiresConfirmation: true));

        var functions = FunctionTestUtilities.CreateFunctions(contentFetcher: fetcher);
        var result = await FunctionTestUtilities.InvokeAsync<WebFetchToolResult>(
            functions,
            "web_fetch",
            FunctionTestUtilities.CreateArguments(new
            {
                url = "https://example.com",
            }));

        Assert.IsTrue(result.Success, result.Message);
        Assert.IsNotNull(result.Result);
        Assert.IsTrue(result.Result.RedirectRequiresConfirmation);
        Assert.AreEqual("https://docs.example.net/start", result.Result.RedirectUrl);
        StringAssert.Contains(result.Message!, "Call web_fetch again");
    }

    private sealed class StubSearchProvider(WebSearchResponse response) : IWebSearchProvider
    {
        private readonly WebSearchResponse _response = response;

        public string ProviderName => _response.Provider;

        public ValueTask<WebSearchResponse> SearchAsync(WebSearchRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_response with { Query = request.Query });
    }

    private sealed class StubContentFetcher(WebContentFetchResponse response) : IWebContentFetcher
    {
        private readonly WebContentFetchResponse _response = response;

        public ValueTask<WebContentFetchResponse> FetchAsync(WebContentFetchRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_response with { Url = request.Url });
    }
}