using Azure.Core;

namespace AIToolkit.Tools.Document.Word;

/// <summary>
/// Configures Microsoft 365 hosted Word document support for OneDrive and SharePoint.
/// </summary>
public sealed class WordDocumentM365Options
{
    /// <summary>
    /// Gets or sets the credential used to authenticate Microsoft Graph requests.
    /// </summary>
    public TokenCredential? Credential { get; init; }

    /// <summary>
    /// Gets or sets the Microsoft Graph scopes to request.
    /// </summary>
    /// <remarks>
    /// When omitted, the resolver uses <c>https://graph.microsoft.com/.default</c>.
    /// </remarks>
    public IEnumerable<string>? Scopes { get; init; }
}