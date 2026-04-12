using System.Net;
using System.Text;

namespace AIToolkit.Tools.Web.Brave.Tests;

[TestClass]
public class BraveWebSearchProviderTests
{
    [TestMethod]
    public async Task SearchAsyncParsesBraveResponseAndExtraSnippets()
    {
        string? apiKey = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            apiKey = request.Headers.GetValues("X-Subscription-Token").Single();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "query": {
                        "more_results_available": true
                      },
                      "web": {
                        "results": [
                          {
                            "title": "Brave result",
                            "url": "https://example.com/page",
                            "description": "main snippet",
                            "extra_snippets": ["extra context"],
                            "meta_url": {
                              "hostname": "example.com"
                            }
                          }
                        ]
                      }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var provider = new BraveWebSearchProvider(
            new BraveWebSearchOptions
            {
                ApiKey = "brave-key",
                ExtraSnippets = true,
            },
            new HttpClient(handler));

        var result = await provider.SearchAsync(new WebSearchRequest("web api", ["example.com"], null, 2));

        Assert.AreEqual("brave-key", apiKey);
        Assert.IsTrue(result.Truncated);
        StringAssert.Contains(result.Results[0].Snippet!, "extra context");
        Assert.AreEqual("example.com", result.Results[0].DisplayUrl);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request));
    }
}