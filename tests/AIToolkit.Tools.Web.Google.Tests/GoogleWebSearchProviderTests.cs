using System.Net;
using System.Text;

namespace AIToolkit.Tools.Web.Google.Tests;

[TestClass]
public class GoogleWebSearchProviderTests
{
    private static readonly string[] ExpectedToolNames = ["web_fetch", "web_search"];

    [TestMethod]
    public async Task SearchAsyncParsesGoogleResponseAndBuildsFilteredQuery()
    {
        string? capturedUri = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedUri = request.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "searchInformation": {
                        "searchTime": 0.42,
                        "totalResults": "123"
                      },
                      "items": [
                        {
                          "title": ".NET docs",
                          "link": "https://learn.microsoft.com/dotnet",
                          "snippet": "Official docs",
                          "displayLink": "learn.microsoft.com"
                        }
                      ]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var provider = new GoogleWebSearchProvider(
            new GoogleWebSearchOptions
            {
                ApiKey = "key",
                SearchEngineId = "engine",
            },
            new HttpClient(handler));

        var result = await provider.SearchAsync(new WebSearchRequest("dotnet tools", ["learn.microsoft.com"], null, 3));

        Assert.IsNotNull(capturedUri);
        StringAssert.Contains(capturedUri, "key=key");
        StringAssert.Contains(capturedUri, "cx=engine");
        StringAssert.Contains(capturedUri, "site%3Alearn.microsoft.com");
        Assert.AreEqual("google", result.Provider);
        Assert.HasCount(1, result.Results);
        Assert.AreEqual(123, result.TotalEstimatedMatches);
        Assert.AreEqual("https://learn.microsoft.com/dotnet", result.Results[0].Url);
        Assert.AreEqual(420d, result.DurationMilliseconds, 0.1d);
    }

    [TestMethod]
    public void CreateFunctionsExposesWebTools()
    {
        var functions = GoogleWebTools.CreateFunctions(
            new GoogleWebSearchOptions
            {
                ApiKey = "key",
                SearchEngineId = "engine",
            });

        CollectionAssert.AreEquivalent(ExpectedToolNames, functions.Select(static function => function.Name).ToArray());
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request));
    }
}