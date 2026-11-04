using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using RoslynLib.Domain.Models;
using RoslynLib.Infrastructure;

namespace RoslynMcp.Tools;

internal sealed class RoslynAnalysisTools
{
    private const int DefaultMaxResults = 100;

    private readonly RoslynWorkspaceTargetSession _workspaceSession;
    private readonly RoslynCliAnalysisService _analysisService;

    public RoslynAnalysisTools(
        RoslynWorkspaceTargetSession workspaceSession,
        RoslynCliAnalysisService analysisService)
    {
        _workspaceSession = workspaceSession;
        _analysisService = analysisService;
    }

    [McpServerTool(Title = "Load target", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Load a .sln, .slnx, project file, or single .cs file into the server session. Call this first; all other analysis tools run against the loaded target. "
        + "Loading the same path again reuses the session. File edits on disk are synchronized automatically before every query, so reloading after edits is not needed.")]
    public Task<WorkspaceSessionStatusView> LoadTarget(
        [Description("Absolute path to a .sln, .slnx, .csproj, .vbproj, .fsproj, or .cs file.")] string targetPath,
        [Description("Rebuild the workspace from scratch even if the same target is already loaded.")] bool forceReload = false,
        CancellationToken cancellationToken = default)
        => InvokeAsync(() => _workspaceSession.LoadTargetAsync(targetPath, forceReload, cancellationToken));

    [McpServerTool(Title = "Release target", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Release the currently loaded target and free its memory.")]
    public Task<WorkspaceSessionReleaseView> ReleaseTarget(CancellationToken cancellationToken = default)
        => InvokeAsync(() => _workspaceSession.ReleaseTargetAsync(cancellationToken));

    [McpServerTool(Title = "Get loaded target", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Return the loaded target, its project names, target directory (output paths are relative to it), last file synchronization, and workspace load diagnostics.")]
    public Task<WorkspaceSessionStatusView> GetLoadedTarget(CancellationToken cancellationToken = default)
        => InvokeAsync(() => _workspaceSession.GetStatusAsync(cancellationToken));

    [McpServerTool(Title = "Find callers", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Find every call site of a method, with the calling member and a source snippet.")]
    public Task<CallersResult> FindCallers(
        [Description(ToolParameterText.Symbol)] string? symbol = null,
        [Description(ToolParameterText.File)] string? file = null,
        [Description(ToolParameterText.Line)] int? line = null,
        [Description(ToolParameterText.Column)] int? column = null,
        [Description(ToolParameterText.Exclude)] string[]? exclude = null,
        [Description(ToolParameterText.MaxResults)] int maxResults = DefaultMaxResults,
        CancellationToken cancellationToken = default)
        => RunQueryAsync(_analysisService.FindCallersAsync, cancellationToken, new QueryArguments(symbol, file, line, column, exclude, maxResults));

    [McpServerTool(Title = "Find usages", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Find every reference to a symbol (type, member, field, ...), with the containing member and a source snippet.")]
    public Task<UsagesResult> FindUsages(
        [Description(ToolParameterText.Symbol)] string? symbol = null,
        [Description(ToolParameterText.File)] string? file = null,
        [Description(ToolParameterText.Line)] int? line = null,
        [Description(ToolParameterText.Column)] int? column = null,
        [Description(ToolParameterText.Exclude)] string[]? exclude = null,
        [Description(ToolParameterText.MaxResults)] int maxResults = DefaultMaxResults,
        CancellationToken cancellationToken = default)
        => RunQueryAsync(_analysisService.FindUsagesAsync, cancellationToken, new QueryArguments(symbol, file, line, column, exclude, maxResults));

    [McpServerTool(Title = "Find implementations", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Find implementations of an interface, abstract type, or interface/abstract member.")]
    public Task<ImplementationsResult> FindImplementations(
        [Description(ToolParameterText.Symbol)] string? symbol = null,
        [Description(ToolParameterText.File)] string? file = null,
        [Description(ToolParameterText.Line)] int? line = null,
        [Description(ToolParameterText.Column)] int? column = null,
        [Description(ToolParameterText.Exclude)] string[]? exclude = null,
        [Description(ToolParameterText.MaxResults)] int maxResults = DefaultMaxResults,
        CancellationToken cancellationToken = default)
        => RunQueryAsync(_analysisService.FindImplementationsAsync, cancellationToken, new QueryArguments(symbol, file, line, column, exclude, maxResults));

    [McpServerTool(Title = "Find overrides", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Find overrides of a virtual/abstract member, or all derived types of a base type.")]
    public Task<OverridesResult> FindOverrides(
        [Description(ToolParameterText.Symbol)] string? symbol = null,
        [Description(ToolParameterText.File)] string? file = null,
        [Description(ToolParameterText.Line)] int? line = null,
        [Description(ToolParameterText.Column)] int? column = null,
        [Description(ToolParameterText.Exclude)] string[]? exclude = null,
        [Description(ToolParameterText.MaxResults)] int maxResults = DefaultMaxResults,
        CancellationToken cancellationToken = default)
        => RunQueryAsync(_analysisService.FindOverridesAsync, cancellationToken, new QueryArguments(symbol, file, line, column, exclude, maxResults));

    [McpServerTool(Title = "Get symbol info", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Get metadata for one symbol: full signature, return type, XML doc summary, kind, accessibility, modifiers, base type, interfaces, attributes, "
        + "member counts, and the line range of every declaration (partial types have several). Cheaper than reading the file when you only need the API shape.")]
    public Task<SymbolInfoResult> GetSymbolInfo(
        [Description(ToolParameterText.Symbol)] string? symbol = null,
        [Description(ToolParameterText.File)] string? file = null,
        [Description(ToolParameterText.Line)] int? line = null,
        [Description(ToolParameterText.Column)] int? column = null,
        CancellationToken cancellationToken = default)
        => RunQueryAsync(_analysisService.GetSymbolInfoAsync, cancellationToken, new QueryArguments(symbol, file, line, column, MaxResults: 0));

    [McpServerTool(Title = "Get symbol source", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Return the source code of a symbol's declaration (including its XML doc comment) with 1-based start/end lines, without reading the whole file. "
        + "Partial types return one entry per declaration.")]
    public Task<SymbolSourceResult> GetSymbolSource(
        [Description(ToolParameterText.Symbol)] string? symbol = null,
        [Description(ToolParameterText.File)] string? file = null,
        [Description(ToolParameterText.Line)] int? line = null,
        [Description(ToolParameterText.Column)] int? column = null,
        [Description("Maximum lines returned per declaration (default 200; 0 means no limit). A declaration cut short has truncated = true; endLine still reports the real end.")] int maxLines = 200,
        [Description(ToolParameterText.Exclude)] string[]? exclude = null,
        [Description("Maximum number of declarations to return (default 20; 0 means no limit).")] int maxResults = 20,
        CancellationToken cancellationToken = default)
    {
        var options = CreateOptions(new QueryArguments(symbol, file, line, column, exclude, maxResults));
        return RunTargetQueryAsync(
            (loadedTarget, token) => _analysisService.GetSymbolSourceAsync(loadedTarget, options, maxLines > 0 ? maxLines : null, token),
            cancellationToken);
    }

    [McpServerTool(Title = "Get document symbols", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Return the outline of a source file: namespaces, types, and members in file order with kind, accessibility, 1-based start/end lines, and nesting depth.")]
    public Task<DocumentSymbolsResult> GetDocumentSymbols(
        [Description("Source file to outline (absolute, or relative to the target directory).")] string file,
        [Description(ToolParameterText.Filter)] SymbolKindFilter? filter = null,
        [Description(ToolParameterText.Visibility)] VisibilityFilter visibility = VisibilityFilter.All,
        [Description("Maximum number of symbols to return (default 300; 0 means no limit).")] int maxResults = 300,
        CancellationToken cancellationToken = default)
        => RunQueryAsync(_analysisService.GetDocumentSymbolsAsync, cancellationToken, new QueryArguments(File: file, MaxResults: maxResults, Filter: filter, Visibility: visibility));

    [McpServerTool(Title = "Find callees", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("List the methods and constructors called from a method, constructor, property, or event body (outgoing calls), with each callee's definition location and the lines it is called on. C# only.")]
    public Task<CalleesResult> FindCallees(
        [Description(ToolParameterText.Symbol)] string? symbol = null,
        [Description(ToolParameterText.File)] string? file = null,
        [Description(ToolParameterText.Line)] int? line = null,
        [Description(ToolParameterText.Column)] int? column = null,
        [Description("Only return callees declared in the loaded target's source (skip framework and package APIs).")] bool sourceOnly = false,
        [Description(ToolParameterText.Exclude)] string[]? exclude = null,
        [Description(ToolParameterText.MaxResults)] int maxResults = DefaultMaxResults,
        CancellationToken cancellationToken = default)
    {
        var options = CreateOptions(new QueryArguments(symbol, file, line, column, exclude, maxResults));
        return RunTargetQueryAsync(
            (loadedTarget, token) => _analysisService.FindCalleesAsync(loadedTarget, options, sourceOnly, token),
            cancellationToken);
    }

    [McpServerTool(Title = "Search symbols", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Search source symbols by wildcard pattern ('*' any characters, '?' one character, case-insensitive) against the simple name, signature, or fully qualified name. "
        + "Use it to discover exact names before calling other tools.")]
    public Task<SymbolSearchResult> SearchSymbols(
        [Description("Wildcard pattern, for example '*Service', 'Order*', 'MyApp.Services.*'.")] string symbol,
        [Description(ToolParameterText.Filter)] SymbolKindFilter? filter = null,
        [Description(ToolParameterText.Visibility)] VisibilityFilter visibility = VisibilityFilter.All,
        [Description(ToolParameterText.Exclude)] string[]? exclude = null,
        [Description(ToolParameterText.MaxResults)] int maxResults = DefaultMaxResults,
        CancellationToken cancellationToken = default)
        => RunQueryAsync(_analysisService.SearchSymbolsAsync, cancellationToken, new QueryArguments(symbol, Exclude: exclude, MaxResults: maxResults, Filter: filter, Visibility: visibility));

    [McpServerTool(Title = "List members", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("List the members of a type, or the types and child namespaces of a namespace, with their declaration locations.")]
    public Task<SymbolListResult> ListMembers(
        [Description(ToolParameterText.Symbol)] string? symbol = null,
        [Description(ToolParameterText.Filter)] SymbolKindFilter? filter = null,
        [Description(ToolParameterText.Visibility)] VisibilityFilter visibility = VisibilityFilter.All,
        [Description(ToolParameterText.Exclude)] string[]? exclude = null,
        [Description(ToolParameterText.File)] string? file = null,
        [Description(ToolParameterText.Line)] int? line = null,
        [Description(ToolParameterText.Column)] int? column = null,
        [Description(ToolParameterText.MaxResults)] int maxResults = DefaultMaxResults,
        CancellationToken cancellationToken = default)
        => RunQueryAsync(_analysisService.ListMembersAsync, cancellationToken, new QueryArguments(symbol, file, line, column, exclude, maxResults, filter, visibility));

    [McpServerTool(Title = "Get type hierarchy", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Get a type's base type chain, directly implemented interfaces, derived types, and (for interfaces) implementations.")]
    public Task<SymbolHierarchyResult> GetHierarchy(
        [Description(ToolParameterText.Symbol)] string? symbol = null,
        [Description(ToolParameterText.Exclude)] string[]? exclude = null,
        [Description(ToolParameterText.File)] string? file = null,
        [Description(ToolParameterText.Line)] int? line = null,
        [Description(ToolParameterText.Column)] int? column = null,
        CancellationToken cancellationToken = default)
        => RunQueryAsync(_analysisService.GetHierarchyAsync, cancellationToken, new QueryArguments(symbol, file, line, column, exclude, MaxResults: 0));

    [McpServerTool(Title = "Project dependency graph", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Return each project in the loaded target with the projects it references.")]
    public Task<DependencyGraphResult> AnalyzeDependencies(CancellationToken cancellationToken = default)
        => RunTargetQueryAsync(_analysisService.AnalyzeDependencyGraphAsync, cancellationToken);

    [McpServerTool(Title = "Namespace dependencies", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Count references between namespaces (source namespace -> target namespace) across the loaded target.")]
    public Task<NamespaceDependenciesResult> AnalyzeNamespaceDependencies(
        [Description("Optional wildcard pattern; an edge is kept when its source or target namespace matches, for example 'MyApp.Domain*'.")] string? filter = null,
        [Description(ToolParameterText.Exclude)] string[]? exclude = null,
        [Description(ToolParameterText.MaxResults)] int maxResults = DefaultMaxResults,
        CancellationToken cancellationToken = default)
        => RunQueryAsync(_analysisService.AnalyzeNamespaceDependenciesAsync, cancellationToken, new QueryArguments(Exclude: exclude, MaxResults: maxResults, RawFilter: filter));

    [McpServerTool(Title = "Find project cycles", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Detect cycles in project references.")]
    public Task<DependencyCyclesResult> FindCycles(CancellationToken cancellationToken = default)
        => RunTargetQueryAsync(_analysisService.FindDependencyCyclesAsync, cancellationToken);

    [McpServerTool(Title = "NuGet dependencies", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("List PackageReference items declared in each project file.")]
    public Task<NugetDependenciesResult> GetNugetDeps(CancellationToken cancellationToken = default)
        => RunTargetQueryAsync(_analysisService.GetNugetDependenciesAsync, cancellationToken);

    [McpServerTool(Title = "Find unused symbols", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Find source symbols with no references inside the loaded target. Public API, reflection, and DI registrations can produce false positives; this can be slow on large solutions.")]
    public Task<UnusedSymbolsResult> FindUnused(
        [Description(ToolParameterText.Filter)] SymbolKindFilter? filter = null,
        [Description(ToolParameterText.Visibility)] VisibilityFilter visibility = VisibilityFilter.All,
        [Description(ToolParameterText.Exclude)] string[]? exclude = null,
        [Description(ToolParameterText.MaxResults)] int maxResults = DefaultMaxResults,
        CancellationToken cancellationToken = default)
        => RunQueryAsync(_analysisService.FindUnusedSymbolsAsync, cancellationToken, new QueryArguments(Exclude: exclude, MaxResults: maxResults, Filter: filter, Visibility: visibility));

    [McpServerTool(Title = "Analyze complexity", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("List methods and local functions whose cyclomatic complexity is at least the threshold, most complex first.")]
    public Task<ComplexityAnalysisResult> AnalyzeComplexity(
        [Description("Minimum cyclomatic complexity to report.")] int threshold = 10,
        [Description(ToolParameterText.Exclude)] string[]? exclude = null,
        [Description(ToolParameterText.MaxResults)] int maxResults = DefaultMaxResults,
        CancellationToken cancellationToken = default)
    {
        var options = CreateOptions(new QueryArguments(Exclude: exclude, MaxResults: maxResults));
        return RunTargetQueryAsync(
            (loadedTarget, token) => _analysisService.AnalyzeComplexityAsync(loadedTarget, threshold, options, token),
            cancellationToken);
    }

    [McpServerTool(Title = "Find obsolete usages", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Find usages of [Obsolete] APIs (compiler diagnostics CS0612, CS0618, CS0619).")]
    public Task<ObsoleteUsagesResult> FindObsolete(
        [Description(ToolParameterText.Exclude)] string[]? exclude = null,
        [Description(ToolParameterText.MaxResults)] int maxResults = DefaultMaxResults,
        CancellationToken cancellationToken = default)
        => RunQueryAsync(_analysisService.FindObsoleteUsagesAsync, cancellationToken, new QueryArguments(Exclude: exclude, MaxResults: maxResults));

    [McpServerTool(Title = "Get diagnostics", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Return compiler diagnostics, errors first. Pass file to check a single document quickly after editing it; "
        + "pass project to limit to some projects. Analyzer diagnostics are excluded unless includeAnalyzers is true (much slower).")]
    public Task<DiagnosticsResult> GetDiagnostics(
        [Description("Which severities to return.")] DiagnosticSeverityFilter severity = DiagnosticSeverityFilter.Error,
        [Description("Optional source file to check (absolute, or relative to the target directory).")] string? file = null,
        [Description(ToolParameterText.Project)] string? project = null,
        [Description("Also run the projects' Roslyn analyzers.")] bool includeAnalyzers = false,
        [Description(ToolParameterText.Exclude)] string[]? exclude = null,
        [Description(ToolParameterText.MaxResults)] int maxResults = DefaultMaxResults,
        CancellationToken cancellationToken = default)
    {
        var options = CreateOptions(new QueryArguments(File: file, Exclude: exclude, MaxResults: maxResults, Project: project));
        return RunTargetQueryAsync(
            (loadedTarget, token) => _analysisService.QueryDiagnosticsAsync(loadedTarget, options, ToOptionValue(severity), includeAnalyzers, token),
            cancellationToken);
    }

    [McpServerTool(Title = "Run analyzers", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Run compiler diagnostics plus the projects' Roslyn analyzers, returning errors, warnings, and info messages (errors first). Slower than get_diagnostics.")]
    public Task<DiagnosticsResult> RunAnalyzers(
        [Description("Optional source file to analyze (absolute, or relative to the target directory).")] string? file = null,
        [Description(ToolParameterText.Project)] string? project = null,
        [Description(ToolParameterText.Exclude)] string[]? exclude = null,
        [Description(ToolParameterText.MaxResults)] int maxResults = DefaultMaxResults,
        CancellationToken cancellationToken = default)
        => RunQueryAsync(_analysisService.RunAnalyzersAsync, cancellationToken, new QueryArguments(File: file, Exclude: exclude, MaxResults: maxResults, Project: project));

    // The MCP SDK only forwards McpException messages to the client; anything else becomes a generic
    // "An error occurred invoking ..." message, which leaves the agent unable to correct its call.
    private static async Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not McpException)
        {
            // This is a local developer tool, so surfacing the underlying message is more useful than hiding it.
            var message = ex is InvalidOperationException or ArgumentException or FileNotFoundException or NotSupportedException
                ? ex.Message
                : $"{ex.GetType().Name}: {ex.Message}";
            throw new McpException(message, ex);
        }
    }

    private Task<TResult> RunTargetQueryAsync<TResult>(
        Func<LoadedWorkspaceTarget, CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken)
        => InvokeAsync(() => _workspaceSession.RunWithLoadedTargetAsync(action, cancellationToken));

    private Task<TResult> RunQueryAsync<TResult>(
        Func<LoadedWorkspaceTarget, RoslynCliQueryOptions, CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken,
        QueryArguments arguments)
    {
        var options = CreateOptions(arguments);
        return RunTargetQueryAsync(
            (loadedTarget, token) => action(loadedTarget, options, token),
            cancellationToken);
    }

    private static RoslynCliQueryOptions CreateOptions(QueryArguments arguments)
    {
        return new RoslynCliQueryOptions
        {
            Symbol = arguments.Symbol,
            Filter = arguments.Filter is { } filter ? ToOptionValue(filter) : arguments.RawFilter,
            Visibility = ToOptionValue(arguments.Visibility),
            Excludes = arguments.Exclude?
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .ToList()
                .AsReadOnly()
                ?? [],
            File = arguments.File,
            Line = arguments.Line,
            Column = arguments.Column,
            Project = arguments.Project,
            MaxResults = arguments.MaxResults > 0 ? arguments.MaxResults : null,
        };
    }

    // RoslynLib option values are the lower-case enum names (class, public, error, ...).
    private static string ToOptionValue<TEnum>(TEnum value)
        where TEnum : struct, Enum
        => value.ToString().ToLowerInvariant();

    private sealed record QueryArguments(
        string? Symbol = null,
        string? File = null,
        int? Line = null,
        int? Column = null,
        string[]? Exclude = null,
        int MaxResults = DefaultMaxResults,
        SymbolKindFilter? Filter = null,
        VisibilityFilter Visibility = VisibilityFilter.All,
        string? Project = null,
        string? RawFilter = null);
}
