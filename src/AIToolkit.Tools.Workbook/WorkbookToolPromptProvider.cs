namespace AIToolkit.Tools.Workbook;

/// <summary>
/// Contributes provider-specific guidance to the generic <c>workbook_*</c> tool descriptions and system prompt text.
/// </summary>
/// <remarks>
/// Provider packages use this extension point to document supported reference formats, syntax rules, and behavioral
/// caveats without replacing the shared prompt catalog entirely.
/// </remarks>
public interface IWorkbookToolPromptProvider
{
    /// <summary>
    /// Gets the provider-specific prompt contribution.
    /// </summary>
    /// <returns>The guidance fragment to merge into shared tool descriptions and system prompts.</returns>
    WorkbookToolPromptContribution GetPromptContribution();
}

/// <summary>
/// Holds provider-specific prompt lines that are merged into workbook tool descriptions and system prompt guidance.
/// </summary>
/// <remarks>
/// Each collection is optional. <see cref="ToolPromptCatalog"/> merges supplied lines with duplicate elimination so
/// provider packages can focus only on what differs from the shared generic guidance.
/// </remarks>
/// <param name="ReadFileDescriptionLines">Additional read guidance.</param>
/// <param name="WriteFileDescriptionLines">Additional write guidance.</param>
/// <param name="EditFileDescriptionLines">Additional edit guidance.</param>
/// <param name="GrepSearchDescriptionLines">Additional search guidance.</param>
/// <param name="SpecificationLookupDescriptionLines">Additional spec-lookup guidance.</param>
/// <param name="SystemPromptLines">Additional system-prompt guidance.</param>
public sealed record WorkbookToolPromptContribution(
    IEnumerable<string>? ReadFileDescriptionLines = null,
    IEnumerable<string>? WriteFileDescriptionLines = null,
    IEnumerable<string>? EditFileDescriptionLines = null,
    IEnumerable<string>? GrepSearchDescriptionLines = null,
    IEnumerable<string>? SpecificationLookupDescriptionLines = null,
    IEnumerable<string>? SystemPromptLines = null);
