namespace AIToolkit.Tools;

/// <summary>
/// Configures the generic <c>task_*</c> tools created by <see cref="TaskTools"/>.
/// </summary>
/// <remarks>
/// The task tool family intentionally has a small option surface because most behavior is owned by the shared
/// <see cref="ITaskToolStore"/> implementation. Use this type when you need to tighten or relax how much
/// information <c>task_list</c> returns in one call.
/// </remarks>
/// <seealso cref="TaskTools"/>
/// <seealso cref="ITaskToolStore"/>
public sealed class TaskToolsOptions
{
    /// <summary>
    /// Gets or sets the maximum number of tasks returned by <c>task_list</c>.
    /// </summary>
    /// <remarks>
    /// Larger requests are clamped so tool calls cannot bypass the host's configured session limit.
    /// </remarks>
    public int MaxListResults { get; init; } = 200;
}
