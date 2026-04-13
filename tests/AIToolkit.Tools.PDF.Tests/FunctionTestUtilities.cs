using Microsoft.Extensions.AI;
using System.Reflection;

namespace AIToolkit.Tools.PDF.Tests;

internal static class FunctionTestUtilities
{
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

    public static AIFunctionArguments CreateArguments(object values)
    {
        var arguments = new AIFunctionArguments();
        foreach (var property in values.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            arguments[property.Name] = property.GetValue(values);
        }

        return arguments;
    }

    public static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AIToolkit.Tools.PDF.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}