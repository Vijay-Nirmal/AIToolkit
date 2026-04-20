using Azure.Core;

namespace AIToolkit.Tools.Workbook.Excel;

/// <summary>
/// Configures Microsoft 365 hosted Excel workbook support for OneDrive and SharePoint.
/// </summary>
/// <remarks>
/// These settings are used only by the hosted-workbook resolver path. Local Excel file handling works without Microsoft
/// Graph credentials.
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// var options = new ExcelWorkbookM365Options
/// {
///     Credential = new DefaultAzureCredential(),
///     Scopes = ["https://graph.microsoft.com/.default"],
/// };
/// ]]></code>
/// </example>
public sealed class ExcelWorkbookM365Options
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

