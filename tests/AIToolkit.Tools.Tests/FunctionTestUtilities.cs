using Microsoft.Extensions.AI;
using System.Reflection;
using System.Text.Json;

namespace AIToolkit.Tools.Tests;

/// <summary>
/// Provides shared helpers for invoking workspace and task functions in tests.
/// </summary>
/// <remarks>
/// These helpers keep the tests focused on behavior by centralizing function creation, argument construction, and
/// result deserialization in one place.
/// </remarks>
internal static class FunctionTestUtilities
{
    /// <summary>
    /// Bundles the workspace and task functions that share one task store.
    /// </summary>
    internal sealed record ToolFunctionSet(
        IReadOnlyList<AIFunction> WorkspaceFunctions,
        IReadOnlyList<AIFunction> TaskFunctions)
    {
        /// <summary>
        /// Gets all registered functions in one list.
        /// </summary>
        public IReadOnlyList<AIFunction> AllFunctions => [.. WorkspaceFunctions, .. TaskFunctions];
    }

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Creates only the workspace tool functions for a test workspace.
    /// </summary>
    /// <param name="workingDirectory">The optional working directory used by workspace tools.</param>
    /// <param name="taskStore">The optional shared task store for background command tracking.</param>
    /// <returns>The workspace tool functions.</returns>
    public static IReadOnlyList<AIFunction> CreateWorkspaceFunctions(string? workingDirectory = null, ITaskToolStore? taskStore = null) =>
        WorkspaceTools.CreateFunctions(CreateWorkspaceOptions(workingDirectory), taskStore);

    /// <summary>
    /// Creates only the task tool functions for tests.
    /// </summary>
    /// <param name="taskStore">The optional shared task store.</param>
    /// <returns>The task tool functions.</returns>
    public static IReadOnlyList<AIFunction> CreateTaskFunctions(ITaskToolStore? taskStore = null) =>
        TaskTools.CreateFunctions(
            new TaskToolsOptions
            {
                MaxListResults = 100,
            },
            taskStore);

    /// <summary>
    /// Creates workspace and task functions that share the same in-memory task store.
    /// </summary>
    /// <param name="workingDirectory">The optional working directory used by workspace tools.</param>
    /// <returns>A function set with both tool families.</returns>
    public static ToolFunctionSet CreateFunctionSet(string? workingDirectory = null)
    {
        var taskStore = new InMemoryTaskToolStore(32_000);
        return new ToolFunctionSet(
            CreateWorkspaceFunctions(workingDirectory, taskStore),
            CreateTaskFunctions(taskStore));
    }

    /// <summary>
    /// Invokes a function and deserializes its structured result.
    /// </summary>
    /// <typeparam name="T">The expected result type.</typeparam>
    /// <param name="functions">The available functions.</param>
    /// <param name="name">The function name to invoke.</param>
    /// <param name="arguments">The optional invocation arguments.</param>
    /// <returns>The deserialized result.</returns>
    public static async Task<T> InvokeAsync<T>(IReadOnlyList<AIFunction> functions, string name, AIFunctionArguments? arguments = null)
    {
        var function = functions.Single(function => function.Name == name);
        var invocationResult = await function.InvokeAsync(arguments);
        return invocationResult switch
        {
            JsonElement json => json.Deserialize<T>(JsonOptions)
                ?? throw new InvalidOperationException($"Unable to deserialize {name} result."),
            T typed => typed,
            _ => throw new InvalidOperationException($"Unexpected result type '{invocationResult?.GetType().FullName ?? "null"}' for {name}.'"),
        };
    }

    /// <summary>
    /// Invokes a function that returns AI content parts.
    /// </summary>
    /// <param name="functions">The available functions.</param>
    /// <param name="name">The function name to invoke.</param>
    /// <param name="arguments">The optional invocation arguments.</param>
    /// <returns>The returned AI content parts.</returns>
    public static async Task<IReadOnlyList<AIContent>> InvokeContentAsync(IReadOnlyList<AIFunction> functions, string name, AIFunctionArguments? arguments = null)
    {
        var function = functions.Single(candidate => candidate.Name == name);
        var invocationResult = await function.InvokeAsync(arguments);
        return invocationResult switch
        {
            IEnumerable<AIContent> parts => parts.ToArray(),
            AIContent part => [part],
            _ => throw new InvalidOperationException($"Unexpected result type '{invocationResult?.GetType().FullName ?? "null"}' for {name}.'"),
        };
    }

    /// <summary>
    /// Creates <see cref="AIFunctionArguments"/> from a simple anonymous object.
    /// </summary>
    /// <param name="values">The object whose public properties become arguments.</param>
    /// <param name="services">The optional service provider to flow into invocation.</param>
    /// <returns>The constructed argument bag.</returns>
    public static AIFunctionArguments CreateArguments(object values, IServiceProvider? services = null)
    {
        var arguments = new AIFunctionArguments
        {
            Services = services,
        };

        foreach (var property in values.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            arguments[property.Name] = property.GetValue(values);
        }

        return arguments;
    }

    /// <summary>
    /// Creates a unique temporary working directory for a test.
    /// </summary>
    /// <returns>The created directory path.</returns>
    public static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AIToolkit.Tools.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>
    /// Gets a shell command that runs long enough to exercise background task behavior.
    /// </summary>
    /// <returns>A shell command string.</returns>
    public static string GetLongRunningCommand() => "sleep 5";

    /// <summary>
    /// Gets a shell command that echoes a value for command-tool assertions.
    /// </summary>
    /// <param name="value">The value to echo.</param>
    /// <returns>A shell command string.</returns>
    public static string GetEchoCommand(string value) => $"printf '{value}\\n'";

    private static WorkspaceToolsOptions CreateWorkspaceOptions(string? workingDirectory) =>
        new()
        {
            WorkingDirectory = workingDirectory,
            DefaultCommandTimeoutSeconds = 10,
            MaxCommandTimeoutSeconds = 30,
            MaxSearchResults = 100,
            MaxTaskOutputCharacters = 32_000,
        };
}
