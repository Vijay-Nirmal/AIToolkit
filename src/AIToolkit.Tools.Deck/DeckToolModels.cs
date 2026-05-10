namespace AIToolkit.Tools.Deck;

/// <summary>
/// Represents the common success and message fields returned by Deck tool operations.
/// </summary>
/// <param name="Success"><see langword="true"/> when the tool call completed successfully; otherwise, <see langword="false"/>.</param>
/// <param name="Message">An optional message, typically used for errors or additional context.</param>
public abstract record DeckToolResult(bool Success, string? Message = null);

/// <summary>
/// Summarizes one slide returned by <c>deck_read_file</c>.
/// </summary>
/// <param name="SlideNumber">The 1-based slide number.</param>
/// <param name="Title">The logical slide title from the slide heading.</param>
public sealed record DeckReadSlideSummary(
    int SlideNumber,
    string Title);

/// <summary>
/// Represents the outcome of reading canonical DeckDoc from a supported deck.
/// </summary>
/// <param name="Success"><see langword="true"/> when the read completed successfully.</param>
/// <param name="Path">The resolved path or provider reference that was read.</param>
/// <param name="TotalSlideCount">The total number of slides in the deck.</param>
/// <param name="ReturnedSlideOffset">The 1-based first slide included in <paramref name="Content"/>.</param>
/// <param name="ReturnedSlideCount">The number of slides included in <paramref name="Content"/>.</param>
/// <param name="Slides">The returned slide summaries.</param>
/// <param name="Content">The selected canonical DeckDoc content with original line numbers.</param>
/// <param name="ProviderName">The stable handler name that processed the deck.</param>
/// <param name="IsPartialView"><see langword="true"/> when the read returned only a slide subset.</param>
/// <param name="PreservesDeckDocRoundTrip">Whether future reads are expected to recover the same DeckDoc exactly.</param>
/// <param name="Message">Optional provider guidance or error information.</param>
public sealed record DeckReadFileToolResult(
    bool Success,
    string Path,
    int TotalSlideCount,
    int ReturnedSlideOffset,
    int ReturnedSlideCount,
    DeckReadSlideSummary[] Slides,
    string Content,
    string ProviderName,
    bool IsPartialView,
    bool PreservesDeckDocRoundTrip,
    string? Message = null)
    : DeckToolResult(Success, Message);

/// <summary>
/// Represents the outcome of writing a Deck file from DeckDoc.
/// </summary>
/// <param name="Success"><see langword="true"/> when the write completed successfully.</param>
/// <param name="Path">The resolved path or provider reference that was written.</param>
/// <param name="ChangeType">Whether the write created a new Deck or updated an existing one.</param>
/// <param name="CharacterCount">The number of canonical DeckDoc characters submitted for the write.</param>
/// <param name="OriginalDeckDoc">The canonical DeckDoc snapshot that was read before the write, when applicable.</param>
/// <param name="DeckDoc">The canonical DeckDoc recovered after the write completed.</param>
/// <param name="Patch">A compact diff between the previous and updated DeckDoc snapshots.</param>
/// <param name="ProviderName">The stable handler name that processed the Deck.</param>
/// <param name="PreservesDeckDocRoundTrip">
/// Whether future reads are expected to recover the same DeckDoc without best-effort conversion loss.
/// </param>
/// <param name="Message">Optional provider guidance or error information.</param>
public sealed record DeckWriteFileToolResult(
    bool Success,
    string Path,
    string ChangeType,
    int CharacterCount,
    string? OriginalDeckDoc,
    string? DeckDoc,
    string? Patch,
    string ProviderName,
    bool PreservesDeckDocRoundTrip,
    string? Message = null)
    : DeckToolResult(Success, Message);

/// <summary>
/// Represents the outcome of applying a deterministic DeckDoc edit to a Deck file.
/// </summary>
/// <param name="Success"><see langword="true"/> when the edit completed successfully.</param>
/// <param name="Path">The resolved path or provider reference that was edited.</param>
/// <param name="ChangesApplied">The number of exact replacements applied.</param>
/// <param name="CharacterCount">The length of the updated canonical DeckDoc snapshot.</param>
/// <param name="OriginalDeckDoc">The canonical DeckDoc snapshot used as the edit baseline.</param>
/// <param name="UpdatedDeckDoc">The canonical DeckDoc recovered after the edit completed.</param>
/// <param name="Patch">A compact diff between the original and updated DeckDoc snapshots.</param>
/// <param name="ProviderName">The stable handler name that processed the Deck.</param>
/// <param name="PreservesDeckDocRoundTrip">
/// Whether future reads are expected to recover the same DeckDoc without best-effort conversion loss.
/// </param>
/// <param name="Message">Optional provider guidance or error information.</param>
public sealed record DeckEditFileToolResult(
    bool Success,
    string Path,
    int ChangesApplied,
    int CharacterCount,
    string? OriginalDeckDoc,
    string? UpdatedDeckDoc,
    string? Patch,
    string ProviderName,
    bool PreservesDeckDocRoundTrip,
    string? Message = null)
    : DeckToolResult(Success, Message);

/// <summary>
/// Represents a single grep-style Deck content match.
/// </summary>
/// <param name="Path">The local relative path or resolver-backed Deck reference that matched.</param>
/// <param name="Offset">The 1-based DeckDoc line offset that matched.</param>
/// <param name="LineText">The matching line.</param>
/// <param name="ContextBefore">Context lines that appeared before the match.</param>
/// <param name="ContextAfter">Context lines that appeared after the match.</param>
public sealed record DeckGrepMatch(
    string Path,
    int Offset,
    int? SlideNumber,
    string? SlideTitle,
    string LineText,
    string[] ContextBefore,
    string[] ContextAfter);

/// <summary>
/// Represents the outcome of a grep-style Deck content search.
/// </summary>
/// <param name="Success"><see langword="true"/> when the search completed successfully.</param>
/// <param name="RootDirectory">The workspace root used for relative-path normalization.</param>
/// <param name="Matches">The canonical DeckDoc matches that were found.</param>
/// <param name="Truncated">Whether additional matches were omitted because the configured result limit was reached.</param>
/// <param name="SupportedExtensions">The locally searchable extensions exposed by registered handlers.</param>
/// <param name="Message">Optional provider guidance or error information.</param>
public sealed record DeckGrepSearchToolResult(
    bool Success,
    string RootDirectory,
    DeckGrepMatch[] Matches,
    bool Truncated,
    string[] SupportedExtensions,
    string? Message = null)
    : DeckToolResult(Success, Message);

/// <summary>
/// Represents a DeckDoc specification section returned by <c>Deck_spec_lookup</c>.
/// </summary>
/// <param name="SectionId">Stable identifier for the matched section.</param>
/// <param name="Title">Human-readable title for the section.</param>
/// <param name="Keywords">Keywords associated with the section.</param>
/// <param name="Content">Exact guidance lines that should be returned for the section.</param>
/// <param name="CommonlyUsedWith">Related DeckDoc features or subfeatures that are commonly authored together with the matched section.</param>
public sealed record DeckSpecificationMatch(
    string SectionId,
    string Title,
    string[] Keywords,
    string[] Content,
    string[] CommonlyUsedWith);

/// <summary>
/// Represents the outcome of a DeckDoc specification lookup.
/// </summary>
/// <param name="Success"><see langword="true"/> when the lookup completed successfully.</param>
/// <param name="Query">The caller-supplied keyword query when one was provided.</param>
/// <param name="Matches">Matched DeckDoc guidance sections ordered by relevance.</param>
/// <param name="Message">Optional guidance or error information.</param>
public sealed record DeckSpecificationLookupToolResult(
    bool Success,
    string Query,
    DeckSpecificationMatch[] Matches,
    string? Message = null)
    : DeckToolResult(Success, Message);

/// <summary>
/// Describes a slide-aware edit operation applied by <c>deck_edit_file</c>.
/// </summary>
/// <param name="Action">The action name: <c>replace</c>, <c>insert_before</c>, <c>insert_after</c>, or <c>delete</c>.</param>
/// <param name="SlideNumber">The 1-based target slide number.</param>
/// <param name="SlideText">The replacement or inserted DeckDoc slide block when the action requires one.</param>
public sealed record DeckSlideEditOperation(
    string Action,
    int SlideNumber,
    string? SlideText = null);

/// <summary>
/// Represents the outcome of creating or registering an asset.
/// </summary>
/// <param name="Success"><see langword="true"/> when the asset was created successfully.</param>
/// <param name="Asset">The stored asset metadata when available.</param>
/// <param name="Message">Optional guidance or error information.</param>
public sealed record DeckAssetCreateToolResult(
    bool Success,
    DeckAssetRecord? Asset,
    string? Message = null)
    : DeckToolResult(Success, Message);

/// <summary>
/// Represents the outcome of searching stored assets.
/// </summary>
/// <param name="Success"><see langword="true"/> when the search completed successfully.</param>
/// <param name="Query">The caller-supplied query text.</param>
/// <param name="SessionId">The optional session identifier used during the search.</param>
/// <param name="Assets">The matching assets.</param>
/// <param name="Message">Optional guidance or error information.</param>
public sealed record DeckAssetSearchToolResult(
    bool Success,
    string Query,
    string? SessionId,
    DeckAssetRecord[] Assets,
    string? Message = null)
    : DeckToolResult(Success, Message);

/// <summary>
/// Summarizes a template returned by the template-list tool.
/// </summary>
/// <param name="Name">The template name.</param>
/// <param name="Description">The template description.</param>
/// <param name="Source">The template source label.</param>
public sealed record DeckTemplateSummary(
    string Name,
    string Description,
    string Source);

/// <summary>
/// Represents the outcome of listing available templates.
/// </summary>
/// <param name="Success"><see langword="true"/> when the list completed successfully.</param>
/// <param name="Query">The caller-supplied query text.</param>
/// <param name="Templates">The matching templates.</param>
/// <param name="Message">Optional guidance or error information.</param>
public sealed record DeckTemplateListToolResult(
    bool Success,
    string Query,
    DeckTemplateSummary[] Templates,
    string? Message = null)
    : DeckToolResult(Success, Message);

/// <summary>
/// Represents the outcome of retrieving one named template.
/// </summary>
/// <param name="Success"><see langword="true"/> when the template lookup completed successfully.</param>
/// <param name="Name">The template name.</param>
/// <param name="Description">The template description.</param>
/// <param name="Source">The template source label.</param>
/// <param name="DeckDoc">The template DeckDoc content.</param>
/// <param name="Message">Optional guidance or error information.</param>
public sealed record DeckTemplateGetToolResult(
    bool Success,
    string Name,
    string? Description,
    string? Source,
    string? DeckDoc,
    string? Message = null)
    : DeckToolResult(Success, Message);

