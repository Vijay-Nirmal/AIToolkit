namespace AIToolkit.Tools.Document.Word;

internal static class WordAsciiDocTextUtilities
{
    public static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}