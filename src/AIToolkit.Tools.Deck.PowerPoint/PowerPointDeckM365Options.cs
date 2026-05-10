using Azure.Core;

namespace AIToolkit.Tools.Deck.PowerPoint;

/// <summary>
/// Configures Microsoft 365 hosted PowerPoint presentation support for OneDrive and SharePoint.
/// </summary>
/// <remarks>
/// These settings are used only by the hosted-presentation resolver path. Local PowerPoint file handling works without
/// Microsoft Graph credentials.
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// var options = new PowerPointDeckM365Options
/// {
///     Credential = new DefaultAzureCredential(),
///     Scopes = ["https://graph.microsoft.com/.default"],
/// };
/// ]]></code>
/// </example>
public sealed class PowerPointDeckM365Options
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


