using System.Net;
using System.Text;

namespace AIToolkit.Tools.Web.DuckDuckGo.Tests;

[TestClass]
public class DuckDuckGoWebSearchProviderTests
{
    private static readonly string[] ExpectedToolNames = ["web_fetch", "web_search"];

    [TestMethod]
    public async Task SearchAsyncParsesHtmlResultsAndDecodesRedirectUrls()
    {
        string? requestUri = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            requestUri = request.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    <html>
                      <body class="body--html">
                        <div class="serp__results">
                          <div id="links" class="results">
                            <div class="result results_links results_links_deep web-result">
                              <div class="links_main links_deep result__body">
                                <h2 class="result__title">
                                  <a rel="nofollow" class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Flearn.microsoft.com%2Fen%2Dus%2Fdotnet%2Fcore%2Fwhats%2Dnew%2Fdotnet%2D10%2Foverview&amp;rut=test">What's new in .NET 10 | Microsoft Learn</a>
                                </h2>
                                <div class="result__extras">
                                  <div class="result__extras__url">
                                    <span class="result__icon"></span>
                                    <a class="result__url" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Flearn.microsoft.com%2Fen%2Dus%2Fdotnet%2Fcore%2Fwhats%2Dnew%2Fdotnet%2D10%2Foverview&amp;rut=test">
                                      learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/overview
                                    </a>
                                    <span>&nbsp; &nbsp; 2025-11-07T00:00:00.0000000</span>
                                  </div>
                                </div>
                                <a class="result__snippet" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Flearn.microsoft.com%2Fen%2Dus%2Fdotnet%2Fcore%2Fwhats%2Dnew%2Fdotnet%2D10%2Foverview&amp;rut=test">Learn about the new features introduced in .NET 10 for the runtime, libraries, and SDK.</a>
                              </div>
                            </div>
                          </div>
                        </div>
                      </body>
                    </html>
                    """,
                    Encoding.UTF8,
                    "text/html")
            };
        });

        var provider = new DuckDuckGoWebSearchProvider(httpClient: new HttpClient(handler));
        var result = await provider.SearchAsync(new WebSearchRequest("dotnet", ["learn.microsoft.com"], null, 5));

        Assert.IsNotNull(requestUri);
        StringAssert.Contains(requestUri, "q=dotnet");
        Assert.AreEqual("duckduckgo", result.Provider);
        Assert.HasCount(1, result.Results);
        Assert.AreEqual("https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/overview", result.Results[0].Url);
        Assert.AreEqual("learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/overview", result.Results[0].DisplayUrl);
        Assert.AreEqual(new DateTimeOffset(2025, 11, 7, 0, 0, 0, TimeSpan.Zero), result.Results[0].PublishedAt);
    }

    [TestMethod]
    public void CreateFunctionsExposesWebTools()
    {
        var functions = DuckDuckGoWebTools.CreateFunctions();

        CollectionAssert.AreEquivalent(ExpectedToolNames, functions.Select(static function => function.Name).ToArray());
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request));
    }
}