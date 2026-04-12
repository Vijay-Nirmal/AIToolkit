using System.Net;
using System.Text;

namespace AIToolkit.Tools.Web.Bing.Tests;

[TestClass]
public class BingWebSearchProviderTests
{
    [TestMethod]
    public async Task SearchAsyncParsesBingResponseAndSetsHeader()
    {
        string? subscriptionKey = null;
        string? requestUri = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            requestUri = request.RequestUri!.ToString();
            subscriptionKey = request.Headers.GetValues("Ocp-Apim-Subscription-Key").Single();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "webPages": {
                        "totalEstimatedMatches": 55,
                        "value": [
                          {
                            "name": "Bing result",
                            "url": "https://example.com/item",
                            "snippet": "snippet",
                            "displayUrl": "example.com/item",
                            "dateLastCrawled": "2026-04-10T00:00:00Z"
                          }
                        ]
                      }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var provider = new BingWebSearchProvider(
            new BingWebSearchOptions
            {
                ApiKey = "bing-key",
                Market = "en-US",
            },
            new HttpClient(handler));

        var result = await provider.SearchAsync(new WebSearchRequest("azure search", null, ["example.net"], 4));

        Assert.AreEqual("bing-key", subscriptionKey);
        StringAssert.Contains(requestUri!, "mkt=en-US");
        StringAssert.Contains(requestUri!, "-site%3Aexample.net");
        Assert.AreEqual(55, result.TotalEstimatedMatches);
        Assert.AreEqual("https://example.com/item", result.Results[0].Url);
        Assert.AreEqual("Bing result", result.Results[0].Title);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request));
    }
}