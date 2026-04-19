namespace AIToolkit.Tools.Document;

/// <summary>
/// Represents the common success and message fields returned by document tool operations.
/// </summary>
/// <param name="Success"><see langword="true"/> when the tool call completed successfully; otherwise, <see langword="false"/>.</param>
/// <param name="Message">An optional message, typically used for errors or additional context.</param>
public abstract record DocumentToolResult(bool Success, string? Message = null);

/// <summary>
/// Represents the outcome of writing a document file from AsciiDoc.
/// </summary>
/// <param name="Success"><see langword="true"/> when the write completed successfully.</param>
/// <param name="Path">The resolved path or provider reference that was written.</param>
/// <param name="ChangeType">Whether the write created a new document or updated an existing one.</param>
/// <param name="CharacterCount">The number of canonical AsciiDoc characters submitted for the write.</param>
/// <param name="OriginalAsciiDoc">The canonical AsciiDoc snapshot that was read before the write, when applicable.</param>
/// <param name="AsciiDoc">The canonical AsciiDoc recovered after the write completed.</param>
/// <param name="Patch">A compact diff between the previous and updated AsciiDoc snapshots.</param>
/// <param name="ProviderName">The stable handler name that processed the document.</param>
/// <param name="PreservesAsciiDocRoundTrip">
/// Whether future reads are expected to recover the same AsciiDoc without best-effort conversion loss.
/// </param>
/// <param name="Message">Optional provider guidance or error information.</param>
public sealed record DocumentWriteFileToolResult(
    bool Success,
    string Path,
    string ChangeType,
    int CharacterCount,
    string? OriginalAsciiDoc,
    string? AsciiDoc,
    string? Patch,
    string ProviderName,
    bool PreservesAsciiDocRoundTrip,
    string? Message = null)
    : DocumentToolResult(Success, Message);

/// <summary>
/// Represents the outcome of applying a deterministic AsciiDoc edit to a document file.
/// </summary>
/// <param name="Success"><see langword="true"/> when the edit completed successfully.</param>
/// <param name="Path">The resolved path or provider reference that was edited.</param>
/// <param name="ChangesApplied">The number of exact replacements applied.</param>
/// <param name="CharacterCount">The length of the updated canonical AsciiDoc snapshot.</param>
/// <param name="OriginalAsciiDoc">The canonical AsciiDoc snapshot used as the edit baseline.</param>
/// <param name="UpdatedAsciiDoc">The canonical AsciiDoc recovered after the edit completed.</param>
/// <param name="Patch">A compact diff between the original and updated AsciiDoc snapshots.</param>
/// <param name="ProviderName">The stable handler name that processed the document.</param>
/// <param name="PreservesAsciiDocRoundTrip">
/// Whether future reads are expected to recover the same AsciiDoc without best-effort conversion loss.
/// </param>
/// <param name="Message">Optional provider guidance or error information.</param>
public sealed record DocumentEditFileToolResult(
    bool Success,
    string Path,
    int ChangesApplied,
    int CharacterCount,
    string? OriginalAsciiDoc,
    string? UpdatedAsciiDoc,
    string? Patch,
    string ProviderName,
    bool PreservesAsciiDocRoundTrip,
    string? Message = null)
    : DocumentToolResult(Success, Message);

/// <summary>
/// Represents a single grep-style document content match.
/// </summary>
/// <param name="Path">The local relative path or resolver-backed document reference that matched.</param>
/// <param name="Offset">The 1-based AsciiDoc line offset that matched.</param>
/// <param name="LineText">The matching line.</param>
/// <param name="ContextBefore">Context lines that appeared before the match.</param>
/// <param name="ContextAfter">Context lines that appeared after the match.</param>
public sealed record DocumentGrepMatch(
    string Path,
    int Offset,
    string LineText,
    string[] ContextBefore,
    string[] ContextAfter);

/// <summary>
/// Represents the outcome of a grep-style document content search.
/// </summary>
/// <param name="Success"><see langword="true"/> when the search completed successfully.</param>
/// <param name="RootDirectory">The workspace root used for relative-path normalization.</param>
/// <param name="Matches">The canonical AsciiDoc matches that were found.</param>
/// <param name="Truncated">Whether additional matches were omitted because the configured result limit was reached.</param>
/// <param name="SupportedExtensions">The locally searchable extensions exposed by registered handlers.</param>
/// <param name="Message">Optional provider guidance or error information.</param>
public sealed record DocumentGrepSearchToolResult(
    bool Success,
    string RootDirectory,
    DocumentGrepMatch[] Matches,
    bool Truncated,
    string[] SupportedExtensions,
    string? Message = null)
    : DocumentToolResult(Success, Message);
