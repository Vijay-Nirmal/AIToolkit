namespace AIToolkit.Tools.Sql;

/// <summary>
/// Represents the common success and message fields returned by SQL tool operations.
/// </summary>
/// <remarks>
/// Provider-specific tool result records inherit from this base record so AI hosts receive a predictable success flag and optional diagnostic
/// message alongside provider-shaped payloads.
/// </remarks>
/// <param name="Success"><see langword="true"/> when the tool call completed successfully; otherwise, <see langword="false"/>.</param>
/// <param name="Message">An optional provider message, typically used for errors or additional context.</param>
public abstract record SqlToolResult(bool Success, string? Message = null);
