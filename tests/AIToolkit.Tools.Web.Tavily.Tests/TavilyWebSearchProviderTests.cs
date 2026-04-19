using System.Net;
using System.Text;

namespace AIToolkit.Tools.Web.Tavily.Tests;

/// <summary>
/// Verifies Tavily-specific request construction and result normalization.
/// </summary>
[TestClass]
public class TavilyWebSearchProviderTests
{
    /// <summary>
    /// Confirms Tavily requests are posted as JSON, the provider summary is surfaced, and duration metadata is converted.
    /// </summary>
    [TestMethod]
    public async Task SearchAsyncParsesTavilyResponseAndPostsJsonBody()
    {
        string? authorization = null;
        string? requestBody = null;
        var handler = new StubHttpMessageHandler(async request =>
        {
            authorization = request.Headers.Authorization?.ToString();
            requestBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "answer": "summary answer",
                      "results": [
                        {
                          "title": "Tavily result",
                          "url": "https://example.com/source",
                          "content": "snippet",
                          "published_date": "2026-04-11T00:00:00Z"
                        }
                      ],
                      "response_time": "1.67"
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var provider = new TavilyWebSearchProvider(
            new TavilyWebSearchOptions
            {
                ApiKey = "tvly-key",
                IncludeAnswer = true,
            },
            new HttpClient(handler));

        var result = await provider.SearchAsync(new WebSearchRequest("latest ai", ["example.com"], null, 2));

        Assert.AreEqual("Bearer tvly-key", authorization);
        StringAssert.Contains(requestBody!, "\"include_domains\":[\"example.com\"]");
        Assert.AreEqual("summary answer", result.Summary);
        Assert.AreEqual(1670d, result.DurationMilliseconds, 0.1d);
        Assert.AreEqual("https://example.com/source", result.Results[0].Url);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _responder(request);
    }
}
