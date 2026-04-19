using Microsoft.Extensions.AI;
using System.Reflection;

namespace AIToolkit.Tools.Document;

/// <summary>
/// Maps <see cref="DocumentToolService"/> methods to the stable public <c>document_*</c> AI function names.
/// </summary>
/// <remarks>
/// This factory keeps the external tool names and descriptions stable while allowing the implementation to remain
/// internal and service-oriented.
/// </remarks>
internal sealed class DocumentAIFunctionFactory(DocumentToolService toolService, DocumentToolsOptions options)
{
    private readonly DocumentToolService _toolService = toolService ?? throw new ArgumentNullException(nameof(toolService));
    private readonly DocumentToolsOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>
    /// Creates the complete generic document tool set.
    /// </summary>
    /// <returns>The read, write, edit, and grep functions in the order exposed by this package.</returns>
    public IReadOnlyList<AIFunction> CreateAll() =>
    [
        CreateReadFile(),
        CreateWriteFile(),
        CreateEditFile(),
        CreateGrepSearch(),
    ];

    /// <summary>
    /// Creates the <c>document_read_file</c> function.
    /// </summary>
    public AIFunction CreateReadFile() =>
        Create(nameof(DocumentToolService.ReadFileAsync), "document_read_file", ToolPromptCatalog.GetDocumentReadFileDescription(_options));

    /// <summary>
    /// Creates the <c>document_write_file</c> function.
    /// </summary>
    public AIFunction CreateWriteFile() =>
        Create(nameof(DocumentToolService.WriteFileAsync), "document_write_file", ToolPromptCatalog.GetDocumentWriteFileDescription(_options));

    /// <summary>
    /// Creates the <c>document_edit_file</c> function.
    /// </summary>
    public AIFunction CreateEditFile() =>
        Create(nameof(DocumentToolService.EditFileAsync), "document_edit_file", ToolPromptCatalog.GetDocumentEditFileDescription(_options));

    /// <summary>
    /// Creates the <c>document_grep_search</c> function.
    /// </summary>
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
