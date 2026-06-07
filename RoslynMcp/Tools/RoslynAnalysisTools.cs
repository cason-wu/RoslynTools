using System.ComponentModel;
using ModelContextProtocol.Server;
using RoslynLib.Domain.Models;
using RoslynLib.Infrastructure;

namespace RoslynMcp.Tools;

public sealed record DiagnosticsBundleResult(
    DiagnosticsResult Errors,
    DiagnosticsResult Warnings);

internal sealed class RoslynAnalysisTools
{
    private readonly RoslynWorkspaceTargetSession _workspaceSession;
    private readonly RoslynCliAnalysisService _analysisService;

    public RoslynAnalysisTools(
        RoslynWorkspaceTargetSession workspaceSession,
        RoslynCliAnalysisService analysisService)
    {
        _workspaceSession = workspaceSession;
        _analysisService = analysisService;
    }

    [McpServerTool]
    [Description("Load a solution, project, or C# source document into the server session. Subsequent analysis tools reuse the loaded target until release_target is called.")]
    public Task<WorkspaceSessionStatusView> LoadTarget(
        [Description("Path to a .sln, .slnx, supported project file, or .cs document.")] string targetPath)
        => _workspaceSession.LoadTargetAsync(targetPath);

    [McpServerTool]
    [Description("Release the currently loaded target and dispose the cached Roslyn workspace.")]
    public Task<WorkspaceSessionReleaseView> ReleaseTarget()
        => _workspaceSession.ReleaseTargetAsync();

    [McpServerTool]
    [Description("Return information about the target currently loaded in the server session.")]
    public Task<WorkspaceSessionStatusView> GetLoadedTarget()
        => _workspaceSession.GetStatusAsync();

    [McpServerTool]
    [Description("Find all callers of a method symbol in the loaded target.")]
    public Task<CallersResult> FindCallers(
        [Description("Symbol name or fully qualified method name. Optional when file and line are provided.")] string? symbol = null,
        [Description("Optional file path used together with line to resolve the target symbol.")] string? file = null,
        [Description("Optional 1-based line number used with file.")] int? line = null,
        [Description("Optional glob-like exclude patterns.")] string[]? exclude = null)
        => RunQueryAsync(_analysisService.FindCallersAsync, symbol, exclude: exclude, file: file, line: line);

    [McpServerTool]
    [Description("Find all usage locations for a symbol in the loaded target.")]
    public Task<UsagesResult> FindUsages(
        [Description("Symbol name or fully qualified name. Optional when file and line are provided.")] string? symbol = null,
        [Description("Optional file path used together with line to resolve the target symbol.")] string? file = null,
        [Description("Optional 1-based line number used with file.")] int? line = null,
        [Description("Optional glob-like exclude patterns.")] string[]? exclude = null)
        => RunQueryAsync(_analysisService.FindUsagesAsync, symbol, exclude: exclude, file: file, line: line);

    [McpServerTool]
    [Description("Find implementations of an interface, abstract type, or abstract/interface member in the loaded target.")]
    public Task<ImplementationsResult> FindImplementations(
        [Description("Symbol name or fully qualified name. Optional when file and line are provided.")] string? symbol = null,
        [Description("Optional file path used together with line to resolve the target symbol.")] string? file = null,
        [Description("Optional 1-based line number used with file.")] int? line = null,
        [Description("Optional glob-like exclude patterns.")] string[]? exclude = null)
        => RunQueryAsync(_analysisService.FindImplementationsAsync, symbol, exclude: exclude, file: file, line: line);

    [McpServerTool]
    [Description("Find overrides of a virtual member or derived types of a base type in the loaded target.")]
    public Task<OverridesResult> FindOverrides(
        [Description("Symbol name or fully qualified name. Optional when file and line are provided.")] string? symbol = null,
        [Description("Optional file path used together with line to resolve the target symbol.")] string? file = null,
        [Description("Optional 1-based line number used with file.")] int? line = null,
        [Description("Optional glob-like exclude patterns.")] string[]? exclude = null)
        => RunQueryAsync(_analysisService.FindOverridesAsync, symbol, exclude: exclude, file: file, line: line);

    [McpServerTool]
    [Description("Get detailed metadata for a symbol in the loaded target.")]
    public Task<SymbolInfoResult> GetSymbolInfo(
        [Description("Symbol name or fully qualified name. Optional when file and line are provided.")] string? symbol = null,
        [Description("Optional file path used together with line to resolve the target symbol.")] string? file = null,
        [Description("Optional 1-based line number used with file.")] int? line = null)
        => RunQueryAsync(_analysisService.GetSymbolInfoAsync, symbol, file: file, line: line);

    [McpServerTool]
    [Description("Search symbols in the loaded target using a wildcard pattern.")]
    public Task<SymbolSearchResult> SearchSymbols(
        [Description("Wildcard pattern for symbol name or fully qualified name.")] string symbol,
        [Description("Optional symbol kind filter.")] string? filter = null,
        [Description("Optional visibility filter: public, internal, private, or all.")] string visibility = "all",
        [Description("Optional glob-like exclude patterns.")] string[]? exclude = null)
        => RunQueryAsync(_analysisService.SearchSymbolsAsync, symbol, filter, visibility, exclude);

    [McpServerTool]
    [Description("List members of a type or namespace in the loaded target.")]
    public Task<SymbolListResult> ListMembers(
        [Description("Type or namespace symbol name. Optional when file and line are provided.")] string? symbol = null,
        [Description("Optional symbol kind filter.")] string? filter = null,
        [Description("Optional visibility filter: public, internal, private, or all.")] string visibility = "all",
        [Description("Optional glob-like exclude patterns.")] string[]? exclude = null,
        [Description("Optional file path used together with line to resolve the target symbol.")] string? file = null,
        [Description("Optional 1-based line number used with file.")] int? line = null)
        => RunQueryAsync(_analysisService.ListMembersAsync, symbol, filter, visibility, exclude, file, line);

    [McpServerTool]
    [Description("Get inheritance and implementation hierarchy for a type in the loaded target.")]
    public Task<SymbolHierarchyResult> GetHierarchy(
        [Description("Type symbol name. Optional when file and line are provided.")] string? symbol = null,
        [Description("Optional glob-like exclude patterns.")] string[]? exclude = null,
        [Description("Optional file path used together with line to resolve the target symbol.")] string? file = null,
        [Description("Optional 1-based line number used with file.")] int? line = null)
        => RunQueryAsync(_analysisService.GetHierarchyAsync, symbol, exclude: exclude, file: file, line: line);

    [McpServerTool]
    [Description("Return the project dependency graph for the loaded target.")]
    public Task<DependencyGraphResult> AnalyzeDependencies()
        => RunTargetQueryAsync(_analysisService.AnalyzeDependencyGraphAsync);

    [McpServerTool]
    [Description("Analyze namespace-level dependencies in the loaded target.")]
    public Task<NamespaceDependenciesResult> AnalyzeNamespaceDependencies(
        [Description("Optional wildcard filter applied to source or target namespace names.")] string? filter = null,
        [Description("Optional glob-like exclude patterns.")] string[]? exclude = null)
        => RunQueryAsync(_analysisService.AnalyzeNamespaceDependenciesAsync, filter: filter, exclude: exclude);

    [McpServerTool]
    [Description("Detect project dependency cycles in the loaded target.")]
    public Task<DependencyCyclesResult> FindCycles()
        => RunTargetQueryAsync(_analysisService.FindDependencyCyclesAsync);

    [McpServerTool]
    [Description("List NuGet package references for the loaded target.")]
    public Task<NugetDependenciesResult> GetNugetDeps()
        => RunTargetQueryAsync(_analysisService.GetNugetDependenciesAsync);

    [McpServerTool]
    [Description("Find potentially unused symbols in the loaded target.")]
    public Task<UnusedSymbolsResult> FindUnused(
        [Description("Optional symbol kind filter.")] string? filter = null,
        [Description("Optional visibility filter: public, internal, private, or all.")] string visibility = "all",
        [Description("Optional glob-like exclude patterns.")] string[]? exclude = null)
        => RunQueryAsync(_analysisService.FindUnusedSymbolsAsync, filter: filter, visibility: visibility, exclude: exclude);

    [McpServerTool]
    [Description("Analyze cyclomatic complexity in the loaded target.")]
    public Task<ComplexityAnalysisResult> AnalyzeComplexity(
        [Description("Complexity threshold. Methods below this number are omitted.")] int threshold = 10,
        [Description("Optional glob-like exclude patterns.")] string[]? exclude = null)
    {
        var options = CreateOptions(exclude: exclude);
        return _workspaceSession.RunWithLoadedTargetAsync((loadedTarget, cancellationToken) =>
            _analysisService.AnalyzeComplexityAsync(loadedTarget, threshold, options, cancellationToken));
    }

    [McpServerTool]
    [Description("Find usages of obsolete APIs in the loaded target.")]
    public Task<ObsoleteUsagesResult> FindObsolete(
        [Description("Optional glob-like exclude patterns.")] string[]? exclude = null)
        => RunQueryAsync(_analysisService.FindObsoleteUsagesAsync, exclude: exclude);

    [McpServerTool]
    [Description("Return compiler errors and warnings for the loaded target.")]
    public async Task<DiagnosticsBundleResult> GetDiagnostics(
        [Description("Optional glob-like exclude patterns.")] string[]? exclude = null)
    {
        var options = CreateOptions(exclude: exclude);

        return await _workspaceSession.RunWithLoadedTargetAsync(async (loadedTarget, cancellationToken) =>
        {
            var errors = await _analysisService.GetErrorsAsync(loadedTarget, options, cancellationToken).ConfigureAwait(false);
            var warnings = await _analysisService.GetWarningsAsync(loadedTarget, options, cancellationToken).ConfigureAwait(false);
            return new DiagnosticsBundleResult(errors, warnings);
        }).ConfigureAwait(false);
    }

    [McpServerTool]
    [Description("Run analyzers and compiler diagnostics for the loaded target.")]
    public Task<DiagnosticsResult> RunAnalyzers(
        [Description("Optional glob-like exclude patterns.")] string[]? exclude = null)
        => RunQueryAsync(_analysisService.RunAnalyzersAsync, exclude: exclude);

    private Task<TResult> RunTargetQueryAsync<TResult>(
        Func<LoadedWorkspaceTarget, CancellationToken, Task<TResult>> action)
    {
        return _workspaceSession.RunWithLoadedTargetAsync(action);
    }

    private Task<TResult> RunQueryAsync<TResult>(
        Func<LoadedWorkspaceTarget, RoslynCliQueryOptions, CancellationToken, Task<TResult>> action,
        string? symbol = null,
        string? filter = null,
        string visibility = "all",
        string[]? exclude = null,
        string? file = null,
        int? line = null)
    {
        var options = CreateOptions(symbol, filter, visibility, exclude, file, line);
        return _workspaceSession.RunWithLoadedTargetAsync(
            (loadedTarget, cancellationToken) => action(loadedTarget, options, cancellationToken));
    }

    private static RoslynCliQueryOptions CreateOptions(
        string? symbol = null,
        string? filter = null,
        string visibility = "all",
        string[]? exclude = null,
        string? file = null,
        int? line = null)
    {
        return new RoslynCliQueryOptions
        {
            Symbol = symbol,
            Filter = filter,
            Visibility = visibility,
            Excludes = exclude?
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .ToList()
                .AsReadOnly()
                ?? [],
            File = file,
            Line = line,
        };
    }
}
