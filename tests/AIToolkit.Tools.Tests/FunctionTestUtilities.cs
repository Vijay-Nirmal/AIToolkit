using Microsoft.Extensions.AI;
using System.Reflection;
using System.Text.Json;

namespace AIToolkit.Tools.Tests;

internal static class FunctionTestUtilities
{
    internal sealed record ToolFunctionSet(
        IReadOnlyList<AIFunction> WorkspaceFunctions,
        IReadOnlyList<AIFunction> TaskFunctions)
    {
        public IReadOnlyList<AIFunction> AllFunctions => [.. WorkspaceFunctions, .. TaskFunctions];
    }

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static IReadOnlyList<AIFunction> CreateWorkspaceFunctions(string? workingDirectory = null, ITaskToolStore? taskStore = null) =>
        WorkspaceTools.CreateFunctions(CreateWorkspaceOptions(workingDirectory), taskStore);

    public static IReadOnlyList<AIFunction> CreateTaskFunctions(ITaskToolStore? taskStore = null) =>
        TaskTools.CreateFunctions(
            new TaskToolsOptions
            {
                MaxListResults = 100,
            },
            taskStore);

    public static ToolFunctionSet CreateFunctionSet(string? workingDirectory = null)
    {
        var taskStore = new InMemoryTaskToolStore(32_000);
        return new ToolFunctionSet(
            CreateWorkspaceFunctions(workingDirectory, taskStore),
            CreateTaskFunctions(taskStore));
    }

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

    public static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AIToolkit.Tools.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static string GetLongRunningCommand() => "sleep 5";

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