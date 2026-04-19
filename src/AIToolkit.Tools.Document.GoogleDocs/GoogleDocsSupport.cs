using System.Globalization;

namespace AIToolkit.Tools.Document.GoogleDocs;

internal static class GoogleDocsSupport
{
    internal const string GoogleDocsMimeType = "application/vnd.google-apps.document";
    internal const string DocxMimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    internal const string PayloadProviderKey = "aitoolkitProvider";
    internal const string PayloadProviderValue = "google-docs";
    internal const string PayloadDocumentIdKey = "aitoolkitDocumentId";

    public static string CreateDocumentReference(string documentId) =>
        $"gdocs://documents/{Uri.EscapeDataString(documentId)}";

    public static string CreateFolderReference(string folderId, string title) =>
        $"gdocs://folders/{Uri.EscapeDataString(folderId)}/documents/{Uri.EscapeDataString(title)}";

    public static string NormalizeVersion(long? version, DateTimeOffset? modifiedTimeUtc) =>
        version is long numericVersion
            ? numericVersion.ToString(CultureInfo.InvariantCulture)
            : modifiedTimeUtc?.UtcTicks.ToString(CultureInfo.InvariantCulture)
            ?? string.Empty;
}