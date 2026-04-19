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
public sealed record DocumentGrepMatch(
    string Path,
    int Offset,
    string LineText,
    string[] ContextBefore,
    string[] ContextAfter);

/// <summary>
/// Represents the outcome of a grep-style document content search.
/// </summary>
public sealed record DocumentGrepSearchToolResult(
    bool Success,
    string RootDirectory,
    DocumentGrepMatch[] Matches,
    bool Truncated,
    string[] SupportedExtensions,
    string? Message = null)
    : DocumentToolResult(Success, Message);