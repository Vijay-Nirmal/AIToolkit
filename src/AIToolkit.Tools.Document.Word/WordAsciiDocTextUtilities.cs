namespace AIToolkit.Tools.Document.Word;

/// <summary>
/// Provides low-level text normalization helpers shared by the Word AsciiDoc pipeline.
/// </summary>
/// <remarks>
/// Parser, renderer, importer, and payload code all normalize line endings through this helper so round-trip comparisons
/// do not depend on the original platform.
/// </remarks>
internal static class WordAsciiDocTextUtilities
{
    /// <summary>
    /// Normalizes Windows and classic Mac line endings to line-feed form.
    /// </summary>
    /// <param name="value">The text to normalize.</param>
    /// <returns>The supplied text with all line endings converted to <c>\n</c>.</returns>
    public static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
