using Google.Apis.Auth.OAuth2;
using Google.Apis.Http;

namespace AIToolkit.Tools.Workbook.GoogleSheets;

/// <summary>
/// Configures the authenticated Google Drive workspace used for Google Sheets references.
/// </summary>
public sealed class GoogleSheetsWorkspaceOptions
{
    /// <summary>
    /// Gets or sets the caller-supplied credential used to authenticate Google Drive and Google Sheets requests.
    /// </summary>
    public GoogleCredential? Credential { get; init; }

    /// <summary>
    /// Gets or sets a preconfigured Google HTTP client initializer such as <c>UserCredential</c> for installed-app OAuth flows.
    /// </summary>
    public IConfigurableHttpClientInitializer? HttpClientInitializer { get; init; }

    /// <summary>
    /// Gets or sets the Google API key used for public, key-authorized Google Drive and Sheets requests.
    /// </summary>
    /// <remarks>
    /// API keys are typically suitable only for publicly shared spreadsheets and generally do not support create, update,
    /// or appData-backed payload operations.
    /// </remarks>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Gets or sets the Google API scopes to request.
    /// Defaults include Drive, Drive appData, and Spreadsheets scopes so hosted workbook payloads and native sheet features can round-trip together.
    /// </summary>
    public IEnumerable<string>? Scopes { get; init; }

    /// <summary>
    /// Gets or sets the optional Google API application name.
    /// </summary>
    public string? ApplicationName { get; init; }

    internal IGoogleSheetsWorkspaceClient? Client { get; init; }
}
