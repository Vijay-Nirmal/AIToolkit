namespace AIToolkit.Tools.Workbook;

/// <summary>
/// Represents the common success and message fields returned by workbook tool operations.
/// </summary>
/// <param name="Success"><see langword="true"/> when the tool call completed successfully; otherwise, <see langword="false"/>.</param>
/// <param name="Message">An optional message, typically used for errors or additional context.</param>
public abstract record WorkbookToolResult(bool Success, string? Message = null);

/// <summary>
/// Represents the outcome of writing a workbook file from WorkbookDoc.
/// </summary>
/// <param name="Success"><see langword="true"/> when the write completed successfully.</param>
/// <param name="Path">The resolved path or provider reference that was written.</param>
/// <param name="ChangeType">Whether the write created a new workbook or updated an existing one.</param>
/// <param name="CharacterCount">The number of canonical WorkbookDoc characters submitted for the write.</param>
/// <param name="OriginalWorkbookDoc">The canonical WorkbookDoc snapshot that was read before the write, when applicable.</param>
/// <param name="WorkbookDoc">The canonical WorkbookDoc recovered after the write completed.</param>
/// <param name="Patch">A compact diff between the previous and updated WorkbookDoc snapshots.</param>
/// <param name="ProviderName">The stable handler name that processed the workbook.</param>
/// <param name="PreservesWorkbookDocRoundTrip">
/// Whether future reads are expected to recover the same WorkbookDoc without best-effort conversion loss.
/// </param>
/// <param name="Message">Optional provider guidance or error information.</param>
public sealed record WorkbookWriteFileToolResult(
    bool Success,
    string Path,
    string ChangeType,
    int CharacterCount,
    string? OriginalWorkbookDoc,
    string? WorkbookDoc,
    string? Patch,
    string ProviderName,
    bool PreservesWorkbookDocRoundTrip,
    string? Message = null)
    : WorkbookToolResult(Success, Message);

/// <summary>
/// Represents the outcome of applying a deterministic WorkbookDoc edit to a workbook file.
/// </summary>
/// <param name="Success"><see langword="true"/> when the edit completed successfully.</param>
/// <param name="Path">The resolved path or provider reference that was edited.</param>
/// <param name="ChangesApplied">The number of exact replacements applied.</param>
/// <param name="CharacterCount">The length of the updated canonical WorkbookDoc snapshot.</param>
/// <param name="OriginalWorkbookDoc">The canonical WorkbookDoc snapshot used as the edit baseline.</param>
/// <param name="UpdatedWorkbookDoc">The canonical WorkbookDoc recovered after the edit completed.</param>
/// <param name="Patch">A compact diff between the original and updated WorkbookDoc snapshots.</param>
/// <param name="ProviderName">The stable handler name that processed the workbook.</param>
/// <param name="PreservesWorkbookDocRoundTrip">
/// Whether future reads are expected to recover the same WorkbookDoc without best-effort conversion loss.
/// </param>
/// <param name="Message">Optional provider guidance or error information.</param>
public sealed record WorkbookEditFileToolResult(
    bool Success,
    string Path,
    int ChangesApplied,
    int CharacterCount,
    string? OriginalWorkbookDoc,
    string? UpdatedWorkbookDoc,
    string? Patch,
    string ProviderName,
    bool PreservesWorkbookDocRoundTrip,
    string? Message = null)
    : WorkbookToolResult(Success, Message);

/// <summary>
/// Represents a single grep-style workbook content match.
/// </summary>
/// <param name="Path">The local relative path or resolver-backed workbook reference that matched.</param>
/// <param name="Offset">The 1-based WorkbookDoc line offset that matched.</param>
/// <param name="LineText">The matching line.</param>
/// <param name="ContextBefore">Context lines that appeared before the match.</param>
/// <param name="ContextAfter">Context lines that appeared after the match.</param>
public sealed record WorkbookGrepMatch(
    string Path,
    int Offset,
    string LineText,
    string[] ContextBefore,
    string[] ContextAfter);

/// <summary>
/// Represents the outcome of a grep-style workbook content search.
/// </summary>
/// <param name="Success"><see langword="true"/> when the search completed successfully.</param>
/// <param name="RootDirectory">The workspace root used for relative-path normalization.</param>
/// <param name="Matches">The canonical WorkbookDoc matches that were found.</param>
/// <param name="Truncated">Whether additional matches were omitted because the configured result limit was reached.</param>
/// <param name="SupportedExtensions">The locally searchable extensions exposed by registered handlers.</param>
/// <param name="Message">Optional provider guidance or error information.</param>
public sealed record WorkbookGrepSearchToolResult(
    bool Success,
    string RootDirectory,
    WorkbookGrepMatch[] Matches,
    bool Truncated,
    string[] SupportedExtensions,
    string? Message = null)
    : WorkbookToolResult(Success, Message);

/// <summary>
/// Represents a WorkbookDoc specification section returned by <c>workbook_spec_lookup</c>.
/// </summary>
/// <param name="SectionId">Stable identifier for the matched section.</param>
/// <param name="Title">Human-readable title for the section.</param>
/// <param name="Keywords">Keywords associated with the section.</param>
/// <param name="Content">Exact guidance lines that should be returned for the section.</param>
public sealed record WorkbookSpecificationMatch(
    string SectionId,
    string Title,
    string[] Keywords,
    string[] Content);

/// <summary>
/// Represents the outcome of a WorkbookDoc specification lookup.
/// </summary>
/// <param name="Success"><see langword="true"/> when the lookup completed successfully.</param>
/// <param name="Query">The caller-supplied keyword query.</param>
/// <param name="Matches">Matched WorkbookDoc guidance sections ordered by relevance.</param>
/// <param name="Message">Optional guidance or error information.</param>
public sealed record WorkbookSpecificationLookupToolResult(
    bool Success,
    string Query,
    WorkbookSpecificationMatch[] Matches,
    string? Message = null)
    : WorkbookToolResult(Success, Message);
