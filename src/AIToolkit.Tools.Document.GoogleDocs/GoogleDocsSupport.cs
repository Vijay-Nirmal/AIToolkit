using System.Globalization;

namespace AIToolkit.Tools.Document.GoogleDocs;

/// <summary>
/// Collects Google Docs-specific constants and reference-format helpers.
/// </summary>
/// <remarks>
/// These helpers keep MIME types, managed payload metadata keys, and normalized hosted-reference shapes consistent across
/// the resolver and Drive client layers.
/// </remarks>
internal static class GoogleDocsSupport
{
    internal const string GoogleDocsMimeType = "application/vnd.google-apps.document";
    internal const string DocxMimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    internal const string PayloadProviderKey = "aitoolkitProvider";
    internal const string PayloadProviderValue = "google-docs";
    internal const string PayloadDocumentIdKey = "aitoolkitDocumentId";

    /// <summary>
    /// Creates the canonical <c>gdocs://documents/{documentId}</c> reference for an existing Google Doc.
    /// </summary>
    public static string CreateDocumentReference(string documentId) =>
        $"gdocs://documents/{Uri.EscapeDataString(documentId)}";

    /// <summary>
    /// Creates the canonical folder-and-title reference used to create or locate a Google Doc in a Drive folder.
    /// </summary>
    public static string CreateFolderReference(string folderId, string title) =>
        $"gdocs://folders/{Uri.EscapeDataString(folderId)}/documents/{Uri.EscapeDataString(title)}";

    /// <summary>
    /// Normalizes Google Drive version metadata into the string form stored in read-state tracking.
    /// </summary>
    public static string NormalizeVersion(long? version, DateTimeOffset? modifiedTimeUtc) =>
        version is long numericVersion
            ? numericVersion.ToString(CultureInfo.InvariantCulture)
            : modifiedTimeUtc?.UtcTicks.ToString(CultureInfo.InvariantCulture)
            ?? string.Empty;
}
