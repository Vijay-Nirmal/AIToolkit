using Google.Apis.Auth.OAuth2;
using Google.Apis.Http;

namespace AIToolkit.Tools.Document.GoogleDocs;

/// <summary>
/// Configures the authenticated Google Drive workspace used for Google Docs references.
/// </summary>
/// <remarks>
/// Google Docs support ultimately talks to Google Drive for URL resolution, DOCX export, conversion uploads, and managed
/// payload sidecars. Supply either a credential, a preconfigured HTTP initializer, an API key for public-read scenarios,
/// or an internal test client.
/// </remarks>
public sealed class GoogleDocsWorkspaceOptions
{
    /// <summary>
    /// Gets or sets the caller-supplied credential used to authenticate Google Drive requests.
    /// </summary>
    public GoogleCredential? Credential { get; init; }

    /// <summary>
    /// Gets or sets a preconfigured Google HTTP client initializer such as <c>UserCredential</c> for installed-app OAuth flows.
    /// </summary>
    public IConfigurableHttpClientInitializer? HttpClientInitializer { get; init; }

    /// <summary>
    /// Gets or sets the Google API key used for public, key-authorized Google Drive and Docs requests.
    /// </summary>
    /// <remarks>
    /// API keys are typically suitable only for publicly shared Google Docs and generally do not support create, update, or appData-backed payload operations.
    /// </remarks>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Gets or sets the Google API scopes to request.
    /// </summary>
    /// <remarks>
    /// When omitted, the workspace client uses Drive and Drive AppData scopes that support hosted document conversion plus
    /// managed-payload sidecars.
    /// </remarks>
    public IEnumerable<string>? Scopes { get; init; }

    /// <summary>
    /// Gets or sets the optional Google API application name.
    /// </summary>
    public string? ApplicationName { get; init; }

    internal IGoogleDocsWorkspaceClient? Client { get; init; }
}
