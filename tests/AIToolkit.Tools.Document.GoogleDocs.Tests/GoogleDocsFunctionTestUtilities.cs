using Microsoft.Extensions.AI;
using System.Reflection;
using System.Text.Json;

namespace AIToolkit.Tools.Document.GoogleDocs.Tests
{
    internal static class GoogleDocsFunctionTestUtilities
    {
        internal static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        public static IReadOnlyList<AIFunction> CreateFunctions(
            FakeGoogleDocsWorkspaceClient workspace,
            string? workingDirectory = null,
            GoogleDocsDocumentHandlerOptions? handlerOptions = null)
        {
            ArgumentNullException.ThrowIfNull(workspace);

            var sourceOptions = handlerOptions ?? new GoogleDocsDocumentHandlerOptions();
            var effectiveHandlerOptions = new GoogleDocsDocumentHandlerOptions
            {
                PreferManagedAsciiDocPayload = sourceOptions.PreferManagedAsciiDocPayload,
                PreferEmbeddedAsciiDoc = sourceOptions.PreferEmbeddedAsciiDoc,
                EnableBestEffortImport = sourceOptions.EnableBestEffortImport,
                PostProcessor = sourceOptions.PostProcessor,
                Workspace = new GoogleDocsWorkspaceOptions
                {
                    Client = workspace,
                },
            };

            return GoogleDocsDocumentTools.CreateFunctions(
                effectiveHandlerOptions,
                new DocumentToolsOptions
                {
                    WorkingDirectory = workingDirectory,
                    MaxReadLines = 8_000,
                    MaxSearchResults = 100,
                });
        }

        public static async Task<T> InvokeAsync<T>(IReadOnlyList<AIFunction> functions, string name, AIFunctionArguments? arguments = null)
        {
            var function = functions.Single(function => function.Name == name);
            var invocationResult = await function.InvokeAsync(arguments).ConfigureAwait(false);
            return invocationResult switch
            {
                JsonElement json => json.Deserialize<T>(JsonOptions)
                    ?? throw new InvalidOperationException($"Unable to deserialize {name} result."),
                T typed => typed,
                _ => throw new InvalidOperationException($"Unexpected result type '{invocationResult?.GetType().FullName ?? "null"}' for {name}."),
            };
        }

        public static async Task<IReadOnlyList<AIContent>> InvokeContentAsync(IReadOnlyList<AIFunction> functions, string name, AIFunctionArguments? arguments = null)
        {
            var function = functions.Single(candidate => candidate.Name == name);
            var invocationResult = await function.InvokeAsync(arguments).ConfigureAwait(false);
            return invocationResult switch
            {
                IEnumerable<AIContent> parts => parts.ToArray(),
                AIContent part => [part],
                _ => throw new InvalidOperationException($"Unexpected result type '{invocationResult?.GetType().FullName ?? "null"}' for {name}."),
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
            var directory = Path.Combine(Path.GetTempPath(), "AIToolkit.Tools.Document.GoogleDocs.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        public static string ReadAsciiDocText(IReadOnlyList<AIContent> contents)
        {
            var numberedText = contents
                .OfType<TextContent>()
                .Select(static content => content.Text)
                .Last(static text => text.Contains('\t', StringComparison.Ordinal));

            return string.Join(
                "\n",
                numberedText
                    .Split('\n')
                    .Select(static line => line[(line.IndexOf('\t', StringComparison.Ordinal) + 1)..]));
        }

        public static string NormalizeLineEndings(string value) =>
            value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

        public static string GetRepositoryRoot()
        {
            var directory = AppContext.BaseDirectory;
            while (!string.IsNullOrWhiteSpace(directory))
            {
                if (File.Exists(Path.Combine(directory, "AIToolkit.slnx")))
                {
                    return directory;
                }

                directory = Directory.GetParent(directory)?.FullName;
            }

            throw new InvalidOperationException("Unable to locate the repository root from the test output directory.");
        }

        public static string GetSpecOutlinePath() =>
            Path.Combine(GetRepositoryRoot(), "docs", "AsciiDoc-langulare-spec.adoc");

        public static string GetChecklistPath() =>
            Path.Combine(GetRepositoryRoot(), "docs", "AsciiDoc-langulare-spec-implementation-checklist.md");

        public static IReadOnlyList<string> LoadChecklistItems() =>
            File.ReadAllLines(GetChecklistPath())
                .Where(static line => line.StartsWith("- [ ] ", StringComparison.Ordinal))
                .Select(static line => line[6..])
                .Select(static line => line.Split(" Source:", StringSplitOptions.None)[0].Trim())
                .ToArray();
    }
}

namespace AIToolkit.Tools.Document.Word.Tests
{
    internal static class FunctionTestUtilities
    {
        public static string NormalizeLineEndings(string value) =>
            AIToolkit.Tools.Document.GoogleDocs.Tests.GoogleDocsFunctionTestUtilities.NormalizeLineEndings(value);

        public static string GetRepositoryRoot() =>
            AIToolkit.Tools.Document.GoogleDocs.Tests.GoogleDocsFunctionTestUtilities.GetRepositoryRoot();

        public static string GetSpecOutlinePath() =>
            AIToolkit.Tools.Document.GoogleDocs.Tests.GoogleDocsFunctionTestUtilities.GetSpecOutlinePath();

        public static string GetChecklistPath() =>
            AIToolkit.Tools.Document.GoogleDocs.Tests.GoogleDocsFunctionTestUtilities.GetChecklistPath();

        public static IReadOnlyList<string> LoadChecklistItems() =>
            AIToolkit.Tools.Document.GoogleDocs.Tests.GoogleDocsFunctionTestUtilities.LoadChecklistItems();
    }
}