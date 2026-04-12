namespace AIToolkit.Tools;

/// <summary>
/// Configures the generic <c>task_*</c> tools created by <see cref="TaskTools"/>.
/// </summary>
public sealed class TaskToolsOptions
{
    /// <summary>
    /// Gets or sets the maximum number of tasks returned by <c>task_list</c>.
    /// </summary>
    public int MaxListResults { get; init; } = 200;
}