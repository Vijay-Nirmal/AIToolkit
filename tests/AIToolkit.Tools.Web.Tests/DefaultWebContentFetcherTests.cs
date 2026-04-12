using System.Net;
using System.Text;

namespace AIToolkit.Tools.Web.Tests;

[TestClass]
public class DefaultWebContentFetcherTests
{
    [TestMethod]
    public async Task FetchConvertsHtmlToMarkdownAndAppliesPromptSelection()
    {
        var handler = new StubHttpMessageHandler(static _ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    <html>
                      <head>
                        <title>Sample Article</title>
                        <meta name="description" content="Short summary" />
                      </head>
                      <body>
                        <h1>Overview</h1>
                        <p>Alpha details that are not important.</p>
                        <h2>Target Section</h2>
                        <p>Beta details and gamma notes appear here.</p>
                      </body>
                    </html>
                    """,
                    Encoding.UTF8,
                    "text/html")
            });

        var fetcher = new DefaultWebContentFetcher(
            new HttpClient(handler),
            new WebToolsOptions
            {
                MaxFetchCharacters = 256,
            });

        var result = await fetcher.FetchAsync(
            new WebContentFetchRequest(
                Url: "https://example.com/article",
                Prompt: "beta gamma",
                MaxCharacters: 120));

        Assert.AreEqual(WebContentFormat.Markdown, result.Format);
        Assert.IsTrue(result.PromptApplied);
        StringAssert.Contains(result.Content, "Beta details");
        StringAssert.Contains(result.Content, "Sample Article");
    }

    [TestMethod]
    public async Task FetchReturnsRedirectForDifferentHost()
    {
        var handler = new StubHttpMessageHandler(static _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("https://docs.example.net/start");
            return response;
        });

        var fetcher = new DefaultWebContentFetcher(new HttpClient(handler), new WebToolsOptions());
        var result = await fetcher.FetchAsync(new WebContentFetchRequest("https://example.com/start"));

        Assert.IsTrue(result.RedirectRequiresConfirmation);
        Assert.AreEqual("https://docs.example.net/start", result.RedirectUrl);
    }

    [TestMethod]
    public async Task FetchRejectsLocalhostUrls()
    {
        var fetcher = new DefaultWebContentFetcher(new WebToolsOptions());

        try
        {
            _ = await fetcher.FetchAsync(new WebContentFetchRequest("http://localhost/test"));
            Assert.Fail("Expected an InvalidOperationException for localhost URLs.");
        }
        catch (InvalidOperationException)
        {
        }
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request));
    }
}