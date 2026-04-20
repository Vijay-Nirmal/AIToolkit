using Microsoft.Extensions.AI;
using System.Reflection;

namespace AIToolkit.Tools.Workbook;

/// <summary>
/// Maps <see cref="WorkbookToolService"/> methods to the stable public <c>workbook_*</c> AI function names.
/// </summary>
/// <remarks>
/// This factory keeps the external tool names and descriptions stable while allowing the implementation to remain
/// internal and service-oriented.
/// </remarks>
internal sealed class WorkbookAIFunctionFactory(WorkbookToolService toolService, WorkbookToolsOptions options)
{
    private readonly WorkbookToolService _toolService = toolService ?? throw new ArgumentNullException(nameof(toolService));
    private readonly WorkbookToolsOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>
    /// Creates the complete generic workbook tool set.
    /// </summary>
    /// <returns>The read, write, edit, grep, and spec-lookup functions in the order exposed by this package.</returns>
    public IReadOnlyList<AIFunction> CreateAll() =>
    [
        CreateReadFile(),
        CreateWriteFile(),
        CreateEditFile(),
        CreateGrepSearch(),
        CreateSpecificationLookup(),
    ];

    /// <summary>
    /// Creates the <c>workbook_read_file</c> function.
    /// </summary>
    public AIFunction CreateReadFile() =>
        Create(nameof(WorkbookToolService.ReadFileAsync), "workbook_read_file", ToolPromptCatalog.GetWorkbookReadFileDescription(_options));

    /// <summary>
    /// Creates the <c>workbook_write_file</c> function.
    /// </summary>
    public AIFunction CreateWriteFile() =>
        Create(nameof(WorkbookToolService.WriteFileAsync), "workbook_write_file", ToolPromptCatalog.GetWorkbookWriteFileDescription(_options));

    /// <summary>
    /// Creates the <c>workbook_edit_file</c> function.
    /// </summary>
    public AIFunction CreateEditFile() =>
        Create(nameof(WorkbookToolService.EditFileAsync), "workbook_edit_file", ToolPromptCatalog.GetWorkbookEditFileDescription(_options));

    /// <summary>
    /// Creates the <c>workbook_grep_search</c> function.
    /// </summary>
    public AIFunction CreateGrepSearch() =>
        Create(nameof(WorkbookToolService.GrepSearchAsync), "workbook_grep_search", ToolPromptCatalog.GetWorkbookGrepSearchDescription(_options));

    /// <summary>
    /// Creates the <c>workbook_spec_lookup</c> function.
    /// </summary>
    public AIFunction CreateSpecificationLookup() =>
        Create(nameof(WorkbookToolService.SpecificationLookupAsync), "workbook_spec_lookup", ToolPromptCatalog.GetWorkbookSpecificationLookupDescription(_options));

    private AIFunction Create(string methodName, string name, string description)
    {
        var method = typeof(WorkbookToolService).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"Method '{methodName}' was not found on {nameof(WorkbookToolService)}.");

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
