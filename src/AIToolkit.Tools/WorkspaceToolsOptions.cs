namespace AIToolkit.Tools;

/// <summary>
/// Configures the generic workspace tools created by <see cref="WorkspaceTools"/>.
/// </summary>
public sealed class WorkspaceToolsOptions
{
    /// <summary>
    /// Gets or sets the default working directory used to resolve relative paths and shell commands.
    /// </summary>
    /// <remarks>
    /// When <see langword="null"/> or whitespace, the current process directory is used.
    /// </remarks>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Gets or sets the default command timeout in seconds for foreground shell execution.
    /// </summary>
    public int DefaultCommandTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Gets or sets the maximum allowed command timeout in seconds.
    /// </summary>
    public int MaxCommandTimeoutSeconds { get; init; } = 600;

    /// <summary>
    /// Gets or sets the maximum number of lines returned by file reads when no explicit range is provided.
    /// </summary>
    public int MaxReadLines { get; init; } = 400;

    /// <summary>
    /// Gets or sets the maximum number of results returned by glob and grep searches.
    /// </summary>
    public int MaxSearchResults { get; init; } = 200;

    /// <summary>
    /// Gets or sets the maximum number of context lines allowed around grep matches.
    /// </summary>
    public int MaxSearchContextLines { get; init; } = 3;

    /// <summary>
    /// Gets or sets the maximum number of characters retained for shell task output.
    /// </summary>
    public int MaxTaskOutputCharacters { get; init; } = 64_000;
}