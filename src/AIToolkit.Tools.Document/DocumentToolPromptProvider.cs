namespace AIToolkit.Tools.Document;

/// <summary>
/// Contributes provider-specific guidance to the generic <c>document_*</c> tool descriptions and system prompt text.
/// </summary>
public interface IDocumentToolPromptProvider
{
    /// <summary>
    /// Gets the provider-specific prompt contribution.
    /// </summary>
    DocumentToolPromptContribution GetPromptContribution();
}

/// <summary>
/// Holds provider-specific prompt lines that are merged into document tool descriptions and system prompt guidance.
/// </summary>
public sealed record DocumentToolPromptContribution(
    IEnumerable<string>? ReadFileDescriptionLines = null,
    IEnumerable<string>? WriteFileDescriptionLines = null,
    IEnumerable<string>? EditFileDescriptionLines = null,
    IEnumerable<string>? GrepSearchDescriptionLines = null,
    IEnumerable<string>? SystemPromptLines = null);