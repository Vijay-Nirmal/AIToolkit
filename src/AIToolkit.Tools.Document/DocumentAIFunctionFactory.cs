using Microsoft.Extensions.AI;
using System.Reflection;

namespace AIToolkit.Tools.Document;

/// <summary>
/// Maps the internal document tool service methods to the public <c>document_*</c> AI function names.
/// </summary>
internal sealed class DocumentAIFunctionFactory(DocumentToolService toolService, DocumentToolsOptions options)
{
    private readonly DocumentToolService _toolService = toolService ?? throw new ArgumentNullException(nameof(toolService));
    private readonly DocumentToolsOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    public IReadOnlyList<AIFunction> CreateAll() =>
    [
        CreateReadFile(),
        CreateWriteFile(),
        CreateEditFile(),
        CreateGrepSearch(),
    ];

    public AIFunction CreateReadFile() =>
        Create(nameof(DocumentToolService.ReadFileAsync), "document_read_file", ToolPromptCatalog.GetDocumentReadFileDescription(_options));

    public AIFunction CreateWriteFile() =>
        Create(nameof(DocumentToolService.WriteFileAsync), "document_write_file", ToolPromptCatalog.GetDocumentWriteFileDescription(_options));

    public AIFunction CreateEditFile() =>
        Create(nameof(DocumentToolService.EditFileAsync), "document_edit_file", ToolPromptCatalog.GetDocumentEditFileDescription(_options));

    public AIFunction CreateGrepSearch() =>
        Create(nameof(DocumentToolService.GrepSearchAsync), "document_grep_search", ToolPromptCatalog.GetDocumentGrepSearchDescription(_options));

    private AIFunction Create(string methodName, string name, string description)
    {
        var method = typeof(DocumentToolService).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"Method '{methodName}' was not found on {nameof(DocumentToolService)}.");

        return AIFunctionFactory.Create(
            method,
            _toolService,
            new AIFunctionFactoryOptions
            {
                Name = name,
                Description = description,
                SerializerOptions = ToolJsonSerializerOptions.CreateWeb(),
            });
    }
}