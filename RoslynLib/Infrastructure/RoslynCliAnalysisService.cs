using System.Collections.Immutable;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using RoslynLib.Domain.Models;

namespace RoslynLib.Infrastructure;

public sealed record RoslynCliQueryOptions
{
    public string? Symbol { get; init; }
    public string? Filter { get; init; }
    public string Visibility { get; init; } = "all";
    public IReadOnlyList<string> Excludes { get; init; } = [];
    public string? File { get; init; }
    public int? Line { get; init; }
}

public sealed record SourceLocationView(
    string File,
    int Line,
    int Column,
    string Snippet);

public sealed record CallerView(
    string CallerSymbol,
    string CallerKind,
    string File,
    int Line,
    int Column,
    string Snippet);

public sealed record UsageView(
    string File,
    int Line,
    int Column,
    string Snippet,
    string ContainingSymbol,
    string ContainingNamespace,
    string ContainingType);

public sealed record ImplementationView(
    string Symbol,
    string Kind,
    string File,
    int Line,
    string ContainingAssembly);

public sealed record OverrideView(
    string Symbol,
    string Kind,
    string File,
    int Line,
    string ContainingType);

public sealed record ProjectDependencyView(
    string Name,
    string Path,
    IReadOnlyList<string> DependsOn);

public sealed record ProjectCycleView(
    IReadOnlyList<string> Projects);

public sealed record NugetPackageView(
    string PackageId,
    string Version);

public sealed record ProjectNugetDependencyView(
    string Project,
    string Path,
    IReadOnlyList<NugetPackageView> Packages);

public sealed record NamespaceDependencyView(
    string SourceNamespace,
    string TargetNamespace,
    int ReferenceCount);

public sealed record SymbolSearchEntryView(
    string Symbol,
    string FullyQualifiedName,
    string Kind,
    string Accessibility,
    string File,
    int Line);

public sealed record SymbolListEntryView(
    string Symbol,
    string Kind,
    string Accessibility,
    string TypeName,
    string File,
    int Line);

public sealed record SymbolMemberSummaryView(
    int Methods,
    int Properties,
    int Fields,
    int Events,
    int NestedTypes);

public sealed record HierarchyEntryView(
    string Symbol,
    string Kind);

public sealed record UnusedSymbolView(
    string Symbol,
    string Kind,
    string Accessibility,
    string File,
    int Line);

public sealed record ComplexityEntryView(
    string Symbol,
    string Kind,
    int Complexity,
    string File,
    int Line);

public sealed record ObsoleteUsageView(
    string DiagnosticId,
    string Message,
    string File,
    int Line,
    int Column);

public sealed record DiagnosticEntryView(
    string Id,
    string Severity,
    string Message,
    string Project,
    string File,
    int Line,
    int Column,
    string Category);

public sealed record CallersResult(
    string Command,
    string Symbol,
    string SymbolKind,
    string FullyQualifiedName,
    IReadOnlyList<CallerView> Callers,
    int TotalCount);

public sealed record UsagesResult(
    string Command,
    string Symbol,
    string SymbolKind,
    string FullyQualifiedName,
    IReadOnlyList<UsageView> Usages,
    int TotalCount);

public sealed record ImplementationsResult(
    string Command,
    string Symbol,
    string SymbolKind,
    string FullyQualifiedName,
    IReadOnlyList<ImplementationView> Implementations,
    int TotalCount);

public sealed record OverridesResult(
    string Command,
    string Symbol,
    string SymbolKind,
    string FullyQualifiedName,
    IReadOnlyList<OverrideView> Overrides,
    int TotalCount);

public sealed record DependencyGraphResult(
    string Command,
    IReadOnlyList<ProjectDependencyView> Projects);

public sealed record DependencyCyclesResult(
    string Command,
    IReadOnlyList<ProjectCycleView> Cycles,
    int TotalCount);

public sealed record NugetDependenciesResult(
    string Command,
    IReadOnlyList<ProjectNugetDependencyView> Projects,
    int TotalCount);

public sealed record NamespaceDependenciesResult(
    string Command,
    IReadOnlyList<NamespaceDependencyView> Dependencies,
    int TotalCount);

public sealed record SymbolInfoResult(
    string Command,
    string Symbol,
    string FullyQualifiedName,
    string Kind,
    string Accessibility,
    bool IsAbstract,
    bool IsSealed,
    bool IsStatic,
    string ContainingNamespace,
    string ContainingAssembly,
    string BaseType,
    IReadOnlyList<string> Interfaces,
    IReadOnlyList<string> Attributes,
    SourceLocationView Location,
    SymbolMemberSummaryView Members);

public sealed record SymbolSearchResult(
    string Command,
    string Pattern,
    IReadOnlyList<SymbolSearchEntryView> Symbols,
    int TotalCount);

public sealed record SymbolListResult(
    string Command,
    string Symbol,
    string FullyQualifiedName,
    IReadOnlyList<SymbolListEntryView> Members,
    int TotalCount);

public sealed record SymbolHierarchyResult(
    string Command,
    string Symbol,
    string FullyQualifiedName,
    string Kind,
    IReadOnlyList<HierarchyEntryView> BaseTypes,
    IReadOnlyList<HierarchyEntryView> Interfaces,
    IReadOnlyList<HierarchyEntryView> DerivedTypes,
    IReadOnlyList<HierarchyEntryView> Implementations);

public sealed record UnusedSymbolsResult(
    string Command,
    IReadOnlyList<UnusedSymbolView> UnusedSymbols,
    int TotalCount);

public sealed record ComplexityAnalysisResult(
    string Command,
    int Threshold,
    IReadOnlyList<ComplexityEntryView> Methods,
    int TotalCount);

public sealed record ObsoleteUsagesResult(
    string Command,
    IReadOnlyList<ObsoleteUsageView> ObsoleteUsages,
    int TotalCount);

public sealed record DiagnosticsResult(
    string Command,
    IReadOnlyList<DiagnosticEntryView> Diagnostics,
    int TotalCount);

public sealed class RoslynCliAnalysisService
{
    public async Task<CallersResult> FindCallersAsync(
        LoadedWorkspaceTarget workspaceTarget,
        RoslynCliQueryOptions options,
        CancellationToken cancellationToken = default)
    {
        var symbol = await ResolveSymbolAsync(workspaceTarget, options, cancellationToken).ConfigureAwait(false);
        if (symbol is not IMethodSymbol methodSymbol)
            throw new InvalidOperationException("refs callers requires a method symbol.");

        var callers = await SymbolFinder.FindCallersAsync(methodSymbol, workspaceTarget.Solution, cancellationToken)
            .ConfigureAwait(false);

        var results = new List<CallerView>();

        foreach (var caller in callers)
        {
            if (caller.CallingSymbol is null)
                continue;

            foreach (var location in caller.Locations)
            {
                var source = await CreateSourceLocationViewAsync(
                        workspaceTarget,
                        location,
                        options.Excludes,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (source is null)
                    continue;

                results.Add(new CallerView(
                    caller.CallingSymbol.ToDisplayString(),
                    RoslynSymbolHelper.GetKind(caller.CallingSymbol).ToString(),
                    source.File,
                    source.Line,
                    source.Column,
                    source.Snippet));
            }
        }

        var ordered = results
            .OrderBy(static item => item.File, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Line)
            .ThenBy(static item => item.Column)
            .ToList()
            .AsReadOnly();

        return new CallersResult(
            "refs callers",
            options.Symbol ?? RoslynSymbolHelper.FormatSymbolDisplayName(symbol),
            RoslynSymbolHelper.GetKind(symbol).ToString(),
            symbol.ToDisplayString(),
            ordered,
            ordered.Count);
    }

    public async Task<UsagesResult> FindUsagesAsync(
        LoadedWorkspaceTarget workspaceTarget,
        RoslynCliQueryOptions options,
        CancellationToken cancellationToken = default)
    {
        var symbol = await ResolveSymbolAsync(workspaceTarget, options, cancellationToken).ConfigureAwait(false);
        var references = await SymbolFinder.FindReferencesAsync(symbol, workspaceTarget.Solution, cancellationToken)
            .ConfigureAwait(false);

        var results = new List<UsageView>();

        foreach (var referencedSymbol in references)
        {
            foreach (var location in referencedSymbol.Locations)
            {
                var source = await CreateSourceLocationViewAsync(
                        workspaceTarget,
                        location.Document,
                        location.Location,
                        options.Excludes,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (source is null)
                    continue;

                var (containingSymbol, containingNamespace, containingType) = await ResolveContainingContextAsync(location, cancellationToken)
                    .ConfigureAwait(false);

                results.Add(new UsageView(
                    source.File,
                    source.Line,
                    source.Column,
                    source.Snippet,
                    containingSymbol,
                    containingNamespace,
                    containingType));
            }
        }

        var ordered = results
            .OrderBy(static item => item.File, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Line)
            .ThenBy(static item => item.Column)
            .ToList()
            .AsReadOnly();

        return new UsagesResult(
            "refs usages",
            options.Symbol ?? RoslynSymbolHelper.FormatSymbolDisplayName(symbol),
            RoslynSymbolHelper.GetKind(symbol).ToString(),
            symbol.ToDisplayString(),
            ordered,
            ordered.Count);
    }

    public async Task<ImplementationsResult> FindImplementationsAsync(
        LoadedWorkspaceTarget workspaceTarget,
        RoslynCliQueryOptions options,
        CancellationToken cancellationToken = default)
    {
        var symbol = await ResolveSymbolAsync(workspaceTarget, options, cancellationToken).ConfigureAwait(false);

        IEnumerable<ISymbol> implementations = symbol switch
        {
            INamedTypeSymbol namedTypeSymbol when namedTypeSymbol.TypeKind == TypeKind.Interface => await SymbolFinder.FindImplementationsAsync(
                    namedTypeSymbol,
                    workspaceTarget.Solution,
                    transitive: true,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false),
            INamedTypeSymbol namedTypeSymbol when namedTypeSymbol.IsAbstract => await SymbolFinder.FindDerivedClassesAsync(
                    namedTypeSymbol,
                    workspaceTarget.Solution,
                    transitive: true,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false),
            IMethodSymbol or IPropertySymbol or IEventSymbol => await FindMemberImplementationsAsync(
                    symbol,
                    workspaceTarget.Solution,
                    cancellationToken)
                .ConfigureAwait(false),
            _ => throw new InvalidOperationException("refs impls requires an interface, abstract type, or interface/abstract member symbol."),
        };

        var results = implementations
            .Where(static implementation => implementation.Locations.Any(static location => location.IsInSource))
            .Where(implementation => !RoslynSymbolHelper.IsPathExcluded(RoslynSymbolHelper.GetSourceLocation(implementation).FilePath, options.Excludes))
            .Select(implementation =>
            {
                var location = RoslynSymbolHelper.GetSourceLocation(implementation);
                return new ImplementationView(
                    implementation.ToDisplayString(),
                    RoslynSymbolHelper.GetKind(implementation).ToString(),
                    FormatOutputPath(location.FilePath, workspaceTarget),
                    location.LineNumber,
                    implementation.ContainingAssembly?.Name ?? string.Empty);
            })
            .OrderBy(static item => item.Symbol, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();

        return new ImplementationsResult(
            "refs impls",
            options.Symbol ?? RoslynSymbolHelper.FormatSymbolDisplayName(symbol),
            RoslynSymbolHelper.GetKind(symbol).ToString(),
            symbol.ToDisplayString(),
            results,
            results.Count);
    }

    public async Task<OverridesResult> FindOverridesAsync(
        LoadedWorkspaceTarget workspaceTarget,
        RoslynCliQueryOptions options,
        CancellationToken cancellationToken = default)
    {
        var symbol = await ResolveSymbolAsync(workspaceTarget, options, cancellationToken).ConfigureAwait(false);

        IEnumerable<ISymbol> overrides = symbol switch
        {
            INamedTypeSymbol namedTypeSymbol => await SymbolFinder.FindDerivedClassesAsync(
                    namedTypeSymbol,
                    workspaceTarget.Solution,
                    transitive: true,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false),
            IMethodSymbol methodSymbol => await SymbolFinder.FindOverridesAsync(
                    methodSymbol,
                    workspaceTarget.Solution,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false),
            IPropertySymbol propertySymbol => await SymbolFinder.FindOverridesAsync(
                    propertySymbol,
                    workspaceTarget.Solution,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false),
            IEventSymbol eventSymbol => await SymbolFinder.FindOverridesAsync(
                    eventSymbol,
                    workspaceTarget.Solution,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false),
            _ => throw new InvalidOperationException("refs overrides requires an overridable member or base type symbol."),
        };

        var results = overrides
            .Where(static overrideSymbol => overrideSymbol.Locations.Any(static location => location.IsInSource))
            .Where(overrideSymbol => !RoslynSymbolHelper.IsPathExcluded(RoslynSymbolHelper.GetSourceLocation(overrideSymbol).FilePath, options.Excludes))
            .Select(overrideSymbol =>
            {
                var location = RoslynSymbolHelper.GetSourceLocation(overrideSymbol);
                return new OverrideView(
                    overrideSymbol.ToDisplayString(),
                    RoslynSymbolHelper.GetKind(overrideSymbol).ToString(),
                    FormatOutputPath(location.FilePath, workspaceTarget),
                    location.LineNumber,
                    overrideSymbol.ContainingType?.ToDisplayString() ?? string.Empty);
            })
            .OrderBy(static item => item.Symbol, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();

        return new OverridesResult(
            "refs overrides",
            options.Symbol ?? RoslynSymbolHelper.FormatSymbolDisplayName(symbol),
            RoslynSymbolHelper.GetKind(symbol).ToString(),
            symbol.ToDisplayString(),
            results,
            results.Count);
    }

    public Task<DependencyGraphResult> AnalyzeDependencyGraphAsync(
        LoadedWorkspaceTarget workspaceTarget,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var projects = workspaceTarget.Projects
            .Select(project => new ProjectDependencyView(
                project.Name,
                FormatOutputPath(project.FilePath ?? string.Empty, workspaceTarget),
                project.ProjectReferences
                    .Select(reference => workspaceTarget.Solution.GetProject(reference.ProjectId)?.Name ?? reference.ProjectId.Id.ToString())
                    .OrderBy(static name => name, StringComparer.Ordinal)
                    .ToList()
                    .AsReadOnly()))
            .OrderBy(static project => project.Name, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();

        return Task.FromResult(new DependencyGraphResult("deps graph", projects));
    }

    public Task<DependencyCyclesResult> FindDependencyCyclesAsync(
        LoadedWorkspaceTarget workspaceTarget,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var graph = workspaceTarget.Projects.ToDictionary(
            static project => project.Id,
            project => project.ProjectReferences.Select(static reference => reference.ProjectId).ToList());

        var stronglyConnectedComponents = TarjanStronglyConnectedComponents(graph, cancellationToken);
        var cycles = stronglyConnectedComponents
            .Where(component => component.Count > 1 || HasSelfLoop(component[0], graph))
            .Select(component => new ProjectCycleView(
                component
                    .Select(projectId => workspaceTarget.Solution.GetProject(projectId)?.Name ?? projectId.Id.ToString())
                    .OrderBy(static name => name, StringComparer.Ordinal)
                    .ToList()
                    .AsReadOnly()))
            .OrderBy(static cycle => string.Join("|", cycle.Projects), StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();

        return Task.FromResult(new DependencyCyclesResult("deps cycles", cycles, cycles.Count));
    }

    public Task<NugetDependenciesResult> GetNugetDependenciesAsync(
        LoadedWorkspaceTarget workspaceTarget,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var projects = workspaceTarget.Projects
            .Where(static project => !string.IsNullOrWhiteSpace(project.FilePath) && File.Exists(project.FilePath))
            .Select(project => new ProjectNugetDependencyView(
                project.Name,
                FormatOutputPath(project.FilePath ?? string.Empty, workspaceTarget),
                ReadPackageReferences(project.FilePath!)
                    .OrderBy(static package => package.PackageId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static package => package.Version, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                    .AsReadOnly()))
            .OrderBy(static project => project.Project, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();

        return Task.FromResult(new NugetDependenciesResult(
            "deps nuget",
            projects,
            projects.Sum(static project => project.Packages.Count)));
    }

    public async Task<NamespaceDependenciesResult> AnalyzeNamespaceDependenciesAsync(
        LoadedWorkspaceTarget workspaceTarget,
        RoslynCliQueryOptions options,
        CancellationToken cancellationToken = default)
    {
        var edges = new Dictionary<(string Source, string Target), int>();

        foreach (var document in EnumerateSourceDocuments(workspaceTarget, options.Excludes))
        {
            if (!string.Equals(document.Project.Language, LanguageNames.CSharp, StringComparison.Ordinal))
                continue;

            var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (syntaxRoot is null || semanticModel is null)
                continue;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in EnumerateNamespaceDependencyNodes(syntaxRoot))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var symbol = semanticModel.GetSymbolInfo(node, cancellationToken).Symbol;
                if (symbol is null)
                    continue;

                var sourceSymbol = semanticModel.GetEnclosingSymbol(node.SpanStart, cancellationToken);
                var sourceNamespace = RoslynSymbolHelper.GetContainingNamespaceName(sourceSymbol);
                var targetNamespace = RoslynSymbolHelper.GetContainingNamespaceName(symbol);

                if (string.IsNullOrWhiteSpace(sourceNamespace) || string.IsNullOrWhiteSpace(targetNamespace))
                    continue;

                if (string.Equals(sourceNamespace, targetNamespace, StringComparison.Ordinal))
                    continue;

                if (!string.IsNullOrWhiteSpace(options.Filter)
                    && !RoslynSymbolHelper.WildcardMatch(sourceNamespace, options.Filter)
                    && !RoslynSymbolHelper.WildcardMatch(targetNamespace, options.Filter))
                {
                    continue;
                }

                var key = $"{sourceNamespace}|{targetNamespace}|{node.SpanStart}|{symbol.ToDisplayString()}";
                if (!seen.Add(key))
                    continue;

                var edgeKey = (sourceNamespace, targetNamespace);
                edges[edgeKey] = edges.TryGetValue(edgeKey, out var count)
                    ? count + 1
                    : 1;
            }
        }

        var results = edges
            .Select(static edge => new NamespaceDependencyView(edge.Key.Source, edge.Key.Target, edge.Value))
            .OrderBy(static edge => edge.SourceNamespace, StringComparer.Ordinal)
            .ThenBy(static edge => edge.TargetNamespace, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();

        return new NamespaceDependenciesResult("deps namespaces", results, results.Count);
    }

    public async Task<SymbolInfoResult> GetSymbolInfoAsync(
        LoadedWorkspaceTarget workspaceTarget,
        RoslynCliQueryOptions options,
        CancellationToken cancellationToken = default)
    {
        var symbol = await ResolveSymbolAsync(workspaceTarget, options, cancellationToken).ConfigureAwait(false);
        var location = RoslynSymbolHelper.GetSourceLocation(symbol);
        var namedTypeSymbol = symbol as INamedTypeSymbol;
        var members = namedTypeSymbol is null
            ? new SymbolMemberSummaryView(0, 0, 0, 0, 0)
            : new SymbolMemberSummaryView(
                namedTypeSymbol.GetMembers().OfType<IMethodSymbol>().Count(static member => !member.IsImplicitlyDeclared && member.MethodKind != MethodKind.PropertyGet && member.MethodKind != MethodKind.PropertySet),
                namedTypeSymbol.GetMembers().OfType<IPropertySymbol>().Count(),
                namedTypeSymbol.GetMembers().OfType<IFieldSymbol>().Count(static member => !member.IsImplicitlyDeclared),
                namedTypeSymbol.GetMembers().OfType<IEventSymbol>().Count(),
                namedTypeSymbol.GetTypeMembers().Length);

        return new SymbolInfoResult(
            "sym info",
            options.Symbol ?? RoslynSymbolHelper.FormatSymbolDisplayName(symbol),
            symbol.ToDisplayString(),
            RoslynSymbolHelper.GetKind(symbol).ToString(),
            symbol.DeclaredAccessibility.ToString(),
            symbol.IsAbstract,
            IsSealed(symbol),
            symbol.IsStatic,
            RoslynSymbolHelper.GetContainingNamespaceName(symbol),
            symbol.ContainingAssembly?.Name ?? string.Empty,
            namedTypeSymbol?.BaseType?.ToDisplayString() ?? string.Empty,
            namedTypeSymbol?.Interfaces.Select(static item => item.ToDisplayString()).ToList().AsReadOnly() ?? [],
            symbol.GetAttributes().Select(FormatAttributeName).ToList().AsReadOnly(),
            new SourceLocationView(
                FormatOutputPath(location.FilePath, workspaceTarget),
                location.LineNumber,
                1,
                string.Empty),
            members);
    }

    public async Task<SymbolSearchResult> SearchSymbolsAsync(
        LoadedWorkspaceTarget workspaceTarget,
        RoslynCliQueryOptions options,
        CancellationToken cancellationToken = default)
    {
        var pattern = options.Symbol;
        if (string.IsNullOrWhiteSpace(pattern))
            throw new InvalidOperationException("sym search requires --symbol.");

        var symbols = await GetAllSourceSymbolsAsync(workspaceTarget, cancellationToken).ConfigureAwait(false);
        var results = symbols
            .Where(symbol => RoslynSymbolHelper.WildcardMatch(symbol.Name, pattern)
                || RoslynSymbolHelper.WildcardMatch(symbol.ToDisplayString(), pattern)
                || RoslynSymbolHelper.WildcardMatch(RoslynSymbolHelper.FormatSymbolDisplayName(symbol), pattern))
            .Where(symbol => RoslynSymbolHelper.MatchesKind(symbol, options.Filter))
            .Where(symbol => RoslynSymbolHelper.MatchesVisibility(symbol, options.Visibility))
            .Where(symbol =>
            {
                var source = RoslynSymbolHelper.GetSourceLocation(symbol);
                return !RoslynSymbolHelper.IsPathExcluded(source.FilePath, options.Excludes);
            })
            .Select(symbol =>
            {
                var source = RoslynSymbolHelper.GetSourceLocation(symbol);
                return new SymbolSearchEntryView(
                    RoslynSymbolHelper.FormatSymbolDisplayName(symbol),
                    symbol.ToDisplayString(),
                    RoslynSymbolHelper.GetKind(symbol).ToString(),
                    symbol.DeclaredAccessibility.ToString(),
                    FormatOutputPath(source.FilePath, workspaceTarget),
                    source.LineNumber);
            })
            .OrderBy(static item => item.Symbol, StringComparer.Ordinal)
            .ThenBy(static item => item.File, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();

        return new SymbolSearchResult("sym search", pattern, results, results.Count);
    }

    public async Task<SymbolListResult> ListMembersAsync(
        LoadedWorkspaceTarget workspaceTarget,
        RoslynCliQueryOptions options,
        CancellationToken cancellationToken = default)
    {
        var symbol = await ResolveSymbolAsync(workspaceTarget, options, cancellationToken).ConfigureAwait(false);

        var members = symbol switch
        {
            INamedTypeSymbol namedTypeSymbol => namedTypeSymbol.GetMembers().Where(static member => !member.IsImplicitlyDeclared),
            INamespaceSymbol namespaceSymbol => namespaceSymbol.GetMembers(),
            _ => throw new InvalidOperationException("sym list requires a type or namespace symbol."),
        };

        var results = members
            .Where(member => RoslynSymbolHelper.MatchesVisibility(member, options.Visibility))
            .Where(member => RoslynSymbolHelper.MatchesKind(member, options.Filter))
            .Where(member =>
            {
                var source = RoslynSymbolHelper.GetSourceLocation(member);
                return !RoslynSymbolHelper.IsPathExcluded(source.FilePath, options.Excludes);
            })
            .Select(member =>
            {
                var source = RoslynSymbolHelper.GetSourceLocation(member);
                return new SymbolListEntryView(
                    RoslynSymbolHelper.FormatSymbolDisplayName(member),
                    RoslynSymbolHelper.GetKind(member).ToString(),
                    member.DeclaredAccessibility.ToString(),
                    RoslynSymbolHelper.BuildMemberTypeName(member),
                    FormatOutputPath(source.FilePath, workspaceTarget),
                    source.LineNumber);
            })
            .OrderBy(static item => item.Symbol, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();

        return new SymbolListResult(
            "sym list",
            options.Symbol ?? RoslynSymbolHelper.FormatSymbolDisplayName(symbol),
            symbol.ToDisplayString(),
            results,
            results.Count);
    }

    public async Task<SymbolHierarchyResult> GetHierarchyAsync(
        LoadedWorkspaceTarget workspaceTarget,
        RoslynCliQueryOptions options,
        CancellationToken cancellationToken = default)
    {
        var symbol = await ResolveSymbolAsync(workspaceTarget, options, cancellationToken).ConfigureAwait(false);
        if (symbol is not INamedTypeSymbol typeSymbol)
            throw new InvalidOperationException("sym hierarchy requires a type symbol.");

        var baseTypes = new List<HierarchyEntryView>();
        var current = typeSymbol.BaseType;
        while (current is not null)
        {
            baseTypes.Add(new HierarchyEntryView(current.ToDisplayString(), RoslynSymbolHelper.GetKind(current).ToString()));
            current = current.BaseType;
        }

        var interfaces = typeSymbol.Interfaces
            .Select(static item => new HierarchyEntryView(item.ToDisplayString(), RoslynSymbolHelper.GetKind(item).ToString()))
            .OrderBy(static item => item.Symbol, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();

        var derivedTypes = typeSymbol.TypeKind == TypeKind.Class
            ? await SymbolFinder.FindDerivedClassesAsync(typeSymbol, workspaceTarget.Solution, transitive: true, cancellationToken: cancellationToken)
                .ConfigureAwait(false)
            : [];
        var implementations = typeSymbol.TypeKind == TypeKind.Interface
            ? await SymbolFinder.FindImplementationsAsync(typeSymbol, workspaceTarget.Solution, transitive: true, cancellationToken: cancellationToken).ConfigureAwait(false)
            : [];

        var derivedTypeViews = derivedTypes
            .Where(static item => item.Locations.Any(static location => location.IsInSource))
            .Where(item => !RoslynSymbolHelper.IsPathExcluded(RoslynSymbolHelper.GetSourceLocation(item).FilePath, options.Excludes))
            .Select(static item => new HierarchyEntryView(item.ToDisplayString(), RoslynSymbolHelper.GetKind(item).ToString()))
            .OrderBy(static item => item.Symbol, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();

        var implementationViews = implementations
            .Where(static item => item.Locations.Any(static location => location.IsInSource))
            .Where(item => !RoslynSymbolHelper.IsPathExcluded(RoslynSymbolHelper.GetSourceLocation(item).FilePath, options.Excludes))
            .Select(static item => new HierarchyEntryView(item.ToDisplayString(), RoslynSymbolHelper.GetKind(item).ToString()))
            .OrderBy(static item => item.Symbol, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();

        return new SymbolHierarchyResult(
            "sym hierarchy",
            options.Symbol ?? RoslynSymbolHelper.FormatSymbolDisplayName(symbol),
            symbol.ToDisplayString(),
            RoslynSymbolHelper.GetKind(symbol).ToString(),
            baseTypes.AsReadOnly(),
            interfaces,
            derivedTypeViews,
            implementationViews);
    }

    public async Task<UnusedSymbolsResult> FindUnusedSymbolsAsync(
        LoadedWorkspaceTarget workspaceTarget,
        RoslynCliQueryOptions options,
        CancellationToken cancellationToken = default)
    {
        var symbols = await GetAllSourceSymbolsAsync(workspaceTarget, cancellationToken).ConfigureAwait(false);
        var candidates = symbols
            .Where(static symbol => symbol.Locations.Any(static location => location.IsInSource))
            .Where(static symbol => !symbol.IsImplicitlyDeclared)
            .Where(IsUnusedCandidate)
            .Where(symbol => RoslynSymbolHelper.MatchesKind(symbol, options.Filter))
            .Where(symbol => RoslynSymbolHelper.MatchesVisibility(symbol, options.Visibility))
            .Where(symbol =>
            {
                var source = RoslynSymbolHelper.GetSourceLocation(symbol);
                return !RoslynSymbolHelper.IsPathExcluded(source.FilePath, options.Excludes);
            })
            .ToList();

        var unusedSymbols = new List<UnusedSymbolView>();
        foreach (var symbol in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsAlwaysUsed(symbol))
                continue;

            var references = await SymbolFinder.FindReferencesAsync(symbol, workspaceTarget.Solution, cancellationToken)
                .ConfigureAwait(false);

            var hasReference = references
                .SelectMany(static reference => reference.Locations)
                .Any(location => !RoslynSymbolHelper.IsPathExcluded(location.Location.GetLineSpan().Path, options.Excludes));

            if (hasReference)
                continue;

            var source = RoslynSymbolHelper.GetSourceLocation(symbol);
            unusedSymbols.Add(new UnusedSymbolView(
                symbol.ToDisplayString(),
                RoslynSymbolHelper.GetKind(symbol).ToString(),
                symbol.DeclaredAccessibility.ToString(),
                FormatOutputPath(source.FilePath, workspaceTarget),
                source.LineNumber));
        }

        var ordered = unusedSymbols
            .OrderBy(static item => item.Symbol, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();

        return new UnusedSymbolsResult("dead unused", ordered, ordered.Count);
    }

    public async Task<ComplexityAnalysisResult> AnalyzeComplexityAsync(
        LoadedWorkspaceTarget workspaceTarget,
        int threshold,
        RoslynCliQueryOptions options,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ComplexityEntryView>();

        foreach (var document in EnumerateSourceDocuments(workspaceTarget, options.Excludes))
        {
            if (!string.Equals(document.Project.Language, LanguageNames.CSharp, StringComparison.Ordinal))
                continue;

            var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (syntaxRoot is null || semanticModel is null)
                continue;

            foreach (var declaration in syntaxRoot.DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var symbol = semanticModel.GetDeclaredSymbol(declaration, cancellationToken);
                if (symbol is null || RoslynSymbolHelper.IsPathExcluded(document.FilePath ?? string.Empty, options.Excludes))
                    continue;

                var complexity = CyclomaticComplexityCalculator.Calculate(declaration);
                if (complexity < threshold)
                    continue;

                var source = RoslynSymbolHelper.GetSourceLocation(symbol);
                results.Add(new ComplexityEntryView(
                    symbol.ToDisplayString(),
                    RoslynSymbolHelper.GetKind(symbol).ToString(),
                    complexity,
                    FormatOutputPath(source.FilePath, workspaceTarget),
                    source.LineNumber));
            }

            foreach (var localFunction in syntaxRoot.DescendantNodes().OfType<LocalFunctionStatementSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var symbol = semanticModel.GetDeclaredSymbol(localFunction, cancellationToken);
                if (symbol is null || RoslynSymbolHelper.IsPathExcluded(document.FilePath ?? string.Empty, options.Excludes))
                    continue;

                var complexity = CyclomaticComplexityCalculator.Calculate(localFunction);
                if (complexity < threshold)
                    continue;

                var source = RoslynSymbolHelper.GetSourceLocation(symbol);
                results.Add(new ComplexityEntryView(
                    symbol.ToDisplayString(),
                    RoslynSymbolHelper.GetKind(symbol).ToString(),
                    complexity,
                    FormatOutputPath(source.FilePath, workspaceTarget),
                    source.LineNumber));
            }
        }

        var ordered = results
            .OrderByDescending(static item => item.Complexity)
            .ThenBy(static item => item.Symbol, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();

        return new ComplexityAnalysisResult("dead complexity", threshold, ordered, ordered.Count);
    }

    public async Task<ObsoleteUsagesResult> FindObsoleteUsagesAsync(
        LoadedWorkspaceTarget workspaceTarget,
        RoslynCliQueryOptions options,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = await CollectCompilationDiagnosticsAsync(workspaceTarget, cancellationToken).ConfigureAwait(false);
        var obsoleteDiagnostics = diagnostics
            .Where(static diagnostic => diagnostic.Id is "CS0612" or "CS0618" or "CS0619")
            .Where(diagnostic => !RoslynSymbolHelper.IsPathExcluded(diagnostic.Location.GetLineSpan().Path, options.Excludes))
            .Select(diagnostic =>
            {
                var span = diagnostic.Location.GetLineSpan();
                return new ObsoleteUsageView(
                    diagnostic.Id,
                    diagnostic.GetMessage(),
                    FormatOutputPath(span.Path, workspaceTarget),
                    span.StartLinePosition.Line + 1,
                    span.StartLinePosition.Character + 1);
            })
            .OrderBy(static item => item.File, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Line)
            .ThenBy(static item => item.Column)
            .ToList()
            .AsReadOnly();

        return new ObsoleteUsagesResult("dead obsolete", obsoleteDiagnostics, obsoleteDiagnostics.Count);
    }

    public async Task<DiagnosticsResult> GetErrorsAsync(
        LoadedWorkspaceTarget workspaceTarget,
        RoslynCliQueryOptions options,
        CancellationToken cancellationToken = default)
        => await GetDiagnosticsAsync(
                workspaceTarget,
                options,
                DiagnosticSeverity.Error,
                "diag errors",
                cancellationToken)
            .ConfigureAwait(false);

    public async Task<DiagnosticsResult> GetWarningsAsync(
        LoadedWorkspaceTarget workspaceTarget,
        RoslynCliQueryOptions options,
        CancellationToken cancellationToken = default)
        => await GetDiagnosticsAsync(
                workspaceTarget,
                options,
                DiagnosticSeverity.Warning,
                "diag warnings",
                cancellationToken)
            .ConfigureAwait(false);

    public async Task<DiagnosticsResult> RunAnalyzersAsync(
        LoadedWorkspaceTarget workspaceTarget,
        RoslynCliQueryOptions options,
        CancellationToken cancellationToken = default)
        => await GetDiagnosticsAsync(
                workspaceTarget,
                options,
                severityFilter: null,
                command: "diag analyze",
                cancellationToken)
            .ConfigureAwait(false);

    private async Task<DiagnosticsResult> GetDiagnosticsAsync(
        LoadedWorkspaceTarget workspaceTarget,
        RoslynCliQueryOptions options,
        DiagnosticSeverity? severityFilter,
        string command,
        CancellationToken cancellationToken)
    {
        var diagnostics = await CollectCompilationDiagnosticsAsync(workspaceTarget, cancellationToken).ConfigureAwait(false);
        var filtered = diagnostics
            .Where(diagnostic => severityFilter is null
                ? diagnostic.Severity != DiagnosticSeverity.Hidden
                : diagnostic.Severity == severityFilter)
            .Where(diagnostic => diagnostic.Location == Location.None || !RoslynSymbolHelper.IsPathExcluded(diagnostic.Location.GetLineSpan().Path, options.Excludes))
            .Select(diagnostic =>
            {
                var span = diagnostic.Location == Location.None
                    ? default
                    : diagnostic.Location.GetLineSpan();
                return new DiagnosticEntryView(
                    diagnostic.Id,
                    diagnostic.Severity.ToString(),
                    diagnostic.GetMessage(),
                    ResolveProjectName(workspaceTarget, diagnostic.Location.SourceTree?.FilePath),
                    diagnostic.Location == Location.None
                        ? string.Empty
                        : FormatOutputPath(span.Path, workspaceTarget),
                    diagnostic.Location == Location.None ? 0 : span.StartLinePosition.Line + 1,
                    diagnostic.Location == Location.None ? 0 : span.StartLinePosition.Character + 1,
                    diagnostic.Descriptor.Category);
            })
            .OrderBy(static item => item.File, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Line)
            .ThenBy(static item => item.Column)
            .ToList()
            .AsReadOnly();

        return new DiagnosticsResult(command, filtered, filtered.Count);
    }

    private async Task<ISymbol> ResolveSymbolAsync(
        LoadedWorkspaceTarget workspaceTarget,
        RoslynCliQueryOptions options,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(options.File) || options.Line is not null)
        {
            if (string.IsNullOrWhiteSpace(options.File) || options.Line is null)
                throw new InvalidOperationException("--file and --line must be provided together.");

            return await ResolveSymbolByLocationAsync(
                    workspaceTarget,
                    options.File!,
                    options.Line.Value,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(options.Symbol))
            throw new InvalidOperationException("--symbol is required unless --file and --line are provided.");

        var symbols = await GetAllSourceSymbolsAsync(workspaceTarget, cancellationToken).ConfigureAwait(false);
        var matches = symbols
            .Select(symbol => new { Symbol = symbol, Score = ScoreSymbolMatch(symbol, options.Symbol!) })
            .Where(static item => item.Score < int.MaxValue)
            .OrderBy(static item => item.Score)
            .ThenBy(item => item.Symbol.ToDisplayString(), StringComparer.Ordinal)
            .ToList();

        if (matches.Count == 0)
            throw new InvalidOperationException($"Unable to resolve symbol '{options.Symbol}'.");

        return matches[0].Symbol;
    }

    private async Task<ISymbol> ResolveSymbolByLocationAsync(
        LoadedWorkspaceTarget workspaceTarget,
        string filePath,
        int lineNumber,
        CancellationToken cancellationToken)
    {
        var fullPath = ResolveDocumentPath(workspaceTarget, filePath);
        var document = workspaceTarget.Solution.Projects
            .SelectMany(static project => project.Documents)
            .FirstOrDefault(candidate => string.Equals(
                Path.GetFullPath(candidate.FilePath ?? string.Empty),
                fullPath,
                StringComparison.OrdinalIgnoreCase));

        if (document is null)
            throw new InvalidOperationException($"Unable to find document '{filePath}' in the current target.");

        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (syntaxRoot is null || semanticModel is null)
            throw new InvalidOperationException($"Unable to create semantic model for '{filePath}'.");

        var zeroBasedLine = lineNumber - 1;
        if (zeroBasedLine < 0 || zeroBasedLine >= text.Lines.Count)
            throw new InvalidOperationException($"Line '{lineNumber}' is outside the bounds of '{filePath}'.");

        var line = text.Lines[zeroBasedLine];
        var candidateNodes = syntaxRoot.DescendantNodes(line.Span)
            .OrderBy(static node => node.Span.Length)
            .ToList();

        foreach (var node in candidateNodes)
        {
            var symbol = semanticModel.GetDeclaredSymbol(node, cancellationToken)
                ?? semanticModel.GetSymbolInfo(node, cancellationToken).Symbol;

            if (symbol is not null)
                return symbol;
        }

        var enclosingSymbol = semanticModel.GetEnclosingSymbol(line.Start, cancellationToken);
        return enclosingSymbol ?? throw new InvalidOperationException($"No symbol found at {filePath}:{lineNumber}.");
    }

    private async Task<IReadOnlyList<ISymbol>> GetAllSourceSymbolsAsync(
        LoadedWorkspaceTarget workspaceTarget,
        CancellationToken cancellationToken)
    {
        var symbols = new List<ISymbol>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var project in workspaceTarget.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null)
                continue;

            foreach (var symbol in EnumerateNamespaceSymbols(compilation.GlobalNamespace, compilation.Assembly))
            {
                var key = BuildSymbolIdentityKey(symbol);
                if (seen.Add(key))
                    symbols.Add(symbol);
            }
        }

        return symbols.AsReadOnly();
    }

    private static IEnumerable<ISymbol> EnumerateNamespaceSymbols(
        INamespaceSymbol namespaceSymbol,
        IAssemblySymbol projectAssembly)
    {
        if (!namespaceSymbol.IsGlobalNamespace)
            yield return namespaceSymbol;

        foreach (var nestedNamespace in namespaceSymbol.GetNamespaceMembers())
        {
            if (!nestedNamespace.IsGlobalNamespace
                && !SymbolEqualityComparer.Default.Equals(nestedNamespace.ContainingAssembly, projectAssembly))
            {
                continue;
            }

            foreach (var symbol in EnumerateNamespaceSymbols(nestedNamespace, projectAssembly))
                yield return symbol;
        }

        foreach (var type in namespaceSymbol.GetTypeMembers())
        {
            if (!type.Locations.Any(static location => location.IsInSource))
                continue;

            foreach (var symbol in EnumerateTypeSymbols(type))
                yield return symbol;
        }
    }

    private static IEnumerable<ISymbol> EnumerateTypeSymbols(INamedTypeSymbol typeSymbol)
    {
        yield return typeSymbol;

        foreach (var member in typeSymbol.GetMembers())
        {
            if (member.IsImplicitlyDeclared)
                continue;

            if (member is INamedTypeSymbol nestedType)
            {
                foreach (var nestedSymbol in EnumerateTypeSymbols(nestedType))
                    yield return nestedSymbol;

                continue;
            }

            if (member.Locations.Any(static location => location.IsInSource))
                yield return member;
        }
    }

    private static int ScoreSymbolMatch(ISymbol symbol, string query)
    {
        if (string.Equals(symbol.ToDisplayString(), query, StringComparison.OrdinalIgnoreCase))
            return 0;

        if (string.Equals(RoslynSymbolHelper.FormatSymbolDisplayName(symbol), query, StringComparison.OrdinalIgnoreCase))
            return 1;

        if (string.Equals(symbol.Name, query, StringComparison.OrdinalIgnoreCase))
            return 2;

        if (symbol.ToDisplayString().EndsWith($".{query}", StringComparison.OrdinalIgnoreCase))
            return 3;

        if (RoslynSymbolHelper.WildcardMatch(symbol.ToDisplayString(), query))
            return 10;

        if (RoslynSymbolHelper.WildcardMatch(RoslynSymbolHelper.FormatSymbolDisplayName(symbol), query))
            return 11;

        if (RoslynSymbolHelper.WildcardMatch(symbol.Name, query))
            return 12;

        return int.MaxValue;
    }

    private static async Task<SourceLocationView?> CreateSourceLocationViewAsync(
        LoadedWorkspaceTarget workspaceTarget,
        Location location,
        IReadOnlyList<string> excludes,
        CancellationToken cancellationToken)
    {
        var lineSpan = location.GetLineSpan();
        if (RoslynSymbolHelper.IsPathExcluded(lineSpan.Path, excludes))
            return null;

        SourceText? sourceText = null;
        if (location.SourceTree is not null)
        {
            sourceText = await location.SourceTree.GetTextAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var document = workspaceTarget.Solution.Projects
                .SelectMany(static project => project.Documents)
                .FirstOrDefault(candidate => string.Equals(candidate.FilePath, lineSpan.Path, StringComparison.OrdinalIgnoreCase));

            if (document is not null)
                sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        }

        var line = lineSpan.StartLinePosition.Line;
        var snippet = sourceText is not null && line >= 0 && line < sourceText.Lines.Count
            ? sourceText.Lines[line].ToString().Trim()
            : string.Empty;

        return new SourceLocationView(
            FormatOutputPath(lineSpan.Path, workspaceTarget),
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.StartLinePosition.Character + 1,
            snippet);
    }

    private static async Task<SourceLocationView?> CreateSourceLocationViewAsync(
        LoadedWorkspaceTarget workspaceTarget,
        Document document,
        Location location,
        IReadOnlyList<string> excludes,
        CancellationToken cancellationToken)
    {
        var lineSpan = location.GetLineSpan();
        if (RoslynSymbolHelper.IsPathExcluded(lineSpan.Path, excludes))
            return null;

        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var line = lineSpan.StartLinePosition.Line;
        var snippet = line >= 0 && line < text.Lines.Count
            ? text.Lines[line].ToString().Trim()
            : string.Empty;

        return new SourceLocationView(
            FormatOutputPath(lineSpan.Path, workspaceTarget),
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.StartLinePosition.Character + 1,
            snippet);
    }

    private static async Task<(string ContainingSymbol, string ContainingNamespace, string ContainingType)> ResolveContainingContextAsync(
        ReferenceLocation location,
        CancellationToken cancellationToken)
    {
        var semanticModel = await location.Document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
            return (string.Empty, string.Empty, string.Empty);

        var symbol = semanticModel.GetEnclosingSymbol(location.Location.SourceSpan.Start, cancellationToken);
        return (
            RoslynSymbolHelper.FormatSymbolDisplayName(symbol),
            RoslynSymbolHelper.GetContainingNamespaceName(symbol),
            symbol?.ContainingType?.Name ?? string.Empty);
    }

    private static IEnumerable<Document> EnumerateSourceDocuments(
        LoadedWorkspaceTarget workspaceTarget,
        IReadOnlyList<string> excludes)
    {
        return workspaceTarget.Projects
            .SelectMany(static project => project.Documents)
            .Where(static document => !string.IsNullOrWhiteSpace(document.FilePath))
            .Where(document => !RoslynSymbolHelper.IsPathExcluded(document.FilePath!, excludes));
    }

    private static IEnumerable<SyntaxNode> EnumerateNamespaceDependencyNodes(SyntaxNode syntaxRoot)
    {
        return syntaxRoot.DescendantNodes().Where(static node => node is
            IdentifierNameSyntax or
            GenericNameSyntax or
            MemberAccessExpressionSyntax or
            ObjectCreationExpressionSyntax or
            InvocationExpressionSyntax or
            AttributeSyntax or
            BaseTypeSyntax);
    }

    private static async Task<IReadOnlyList<Diagnostic>> CollectCompilationDiagnosticsAsync(
        LoadedWorkspaceTarget workspaceTarget,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<Diagnostic>();

        foreach (var project in workspaceTarget.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null)
                continue;

            diagnostics.AddRange(compilation.GetDiagnostics(cancellationToken));

            var analyzers = project.AnalyzerReferences
                .SelectMany(reference => reference.GetAnalyzers(project.Language))
                .ToImmutableArray();

            if (analyzers.Length == 0)
                continue;

            var compilationWithAnalyzers = compilation.WithAnalyzers(
                analyzers,
                options: null);
            diagnostics.AddRange(await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false));
        }

        return diagnostics.AsReadOnly();
    }

    private static async Task<IReadOnlyList<ISymbol>> FindMemberImplementationsAsync(
        ISymbol symbol,
        Solution solution,
        CancellationToken cancellationToken)
    {
        var containingType = symbol.ContainingType
            ?? throw new InvalidOperationException("Member symbol does not have a containing type.");

        IEnumerable<INamedTypeSymbol> candidateTypes = containingType.TypeKind switch
        {
            TypeKind.Interface => await SymbolFinder.FindImplementationsAsync(
                    containingType,
                    solution,
                    transitive: true,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false),
            _ when containingType.IsAbstract => await SymbolFinder.FindDerivedClassesAsync(
                    containingType,
                    solution,
                    transitive: true,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false),
            _ => [],
        };

        var implementations = new List<ISymbol>();
        foreach (var candidateType in candidateTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var implementation = ResolveMemberImplementation(candidateType, symbol);
            if (implementation is not null)
                implementations.Add(implementation);
        }

        return implementations.AsReadOnly();
    }

    private static ISymbol? ResolveMemberImplementation(
        INamedTypeSymbol candidateType,
        ISymbol symbol)
    {
        if (symbol.ContainingType?.TypeKind == TypeKind.Interface)
        {
            var interfaceImplementation = candidateType.FindImplementationForInterfaceMember(symbol);
            if (interfaceImplementation is not null)
                return interfaceImplementation;
        }

        return symbol switch
        {
            IMethodSymbol methodSymbol => candidateType.GetMembers(methodSymbol.Name)
                .OfType<IMethodSymbol>()
                .FirstOrDefault(candidate => SymbolEqualityComparer.Default.Equals(candidate.OverriddenMethod, methodSymbol)),
            IPropertySymbol propertySymbol => candidateType.GetMembers(propertySymbol.Name)
                .OfType<IPropertySymbol>()
                .FirstOrDefault(candidate => SymbolEqualityComparer.Default.Equals(candidate.OverriddenProperty, propertySymbol)),
            IEventSymbol eventSymbol => candidateType.GetMembers(eventSymbol.Name)
                .OfType<IEventSymbol>()
                .FirstOrDefault(candidate => SymbolEqualityComparer.Default.Equals(candidate.OverriddenEvent, eventSymbol)),
            _ => null,
        };
    }

    private static string ResolveProjectName(LoadedWorkspaceTarget workspaceTarget, string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return string.Empty;

        var fullPath = Path.GetFullPath(sourcePath);
        return workspaceTarget.Projects.FirstOrDefault(project =>
                project.Documents.Any(document => string.Equals(
                    Path.GetFullPath(document.FilePath ?? string.Empty),
                    fullPath,
                    StringComparison.OrdinalIgnoreCase)))?.Name
            ?? string.Empty;
    }

    private static string ResolveDocumentPath(LoadedWorkspaceTarget workspaceTarget, string filePath)
    {
        if (Path.IsPathRooted(filePath))
            return Path.GetFullPath(filePath);

        var candidate = Path.GetFullPath(Path.Combine(workspaceTarget.TargetDirectory, filePath));
        if (File.Exists(candidate))
            return candidate;

        var projectDirectory = workspaceTarget.PrimaryProject?.FilePath is null
            ? workspaceTarget.TargetDirectory
            : Path.GetDirectoryName(workspaceTarget.PrimaryProject.FilePath) ?? workspaceTarget.TargetDirectory;

        return Path.GetFullPath(Path.Combine(projectDirectory, filePath));
    }

    private static string FormatOutputPath(string filePath, LoadedWorkspaceTarget workspaceTarget)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return string.Empty;

        if (!Path.IsPathRooted(filePath))
            return filePath.Replace('\\', '/');

        try
        {
            var relativePath = Path.GetRelativePath(workspaceTarget.TargetDirectory, filePath);
            if (!relativePath.StartsWith("..", StringComparison.Ordinal))
                return relativePath.Replace('\\', '/');
        }
        catch
        {
            // Ignore and fall back to absolute path.
        }

        return filePath.Replace('\\', '/');
    }

    private static string BuildSymbolIdentityKey(ISymbol symbol)
        => string.Concat(symbol.ContainingAssembly?.Name, "::", RoslynSymbolHelper.GetKind(symbol), "::", symbol.ToDisplayString());

    private static bool IsSealed(ISymbol symbol)
    {
        return symbol switch
        {
            INamedTypeSymbol typeSymbol => typeSymbol.IsSealed,
            IMethodSymbol methodSymbol => methodSymbol.IsSealed,
            _ => false,
        };
    }

    private static string FormatAttributeName(AttributeData attribute)
    {
        var attributeName = attribute.AttributeClass?.Name ?? string.Empty;
        return attributeName.EndsWith("Attribute", StringComparison.Ordinal)
            ? attributeName[..^"Attribute".Length]
            : attributeName;
    }

    private static bool IsUnusedCandidate(ISymbol symbol)
    {
        return symbol switch
        {
            INamedTypeSymbol => true,
            IMethodSymbol method => method.MethodKind is MethodKind.Ordinary or MethodKind.Constructor,
            IPropertySymbol => true,
            IFieldSymbol field => !field.IsConst,
            IEventSymbol => true,
            _ => false,
        };
    }

    private static bool IsAlwaysUsed(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol method when method.MethodKind == MethodKind.Constructor && method.ContainingType.IsImplicitClass => true,
            IMethodSymbol method when method.IsOverride || method.ExplicitInterfaceImplementations.Length > 0 => true,
            IPropertySymbol property when property.IsOverride || property.ExplicitInterfaceImplementations.Length > 0 => true,
            IEventSymbol @event when @event.IsOverride || @event.ExplicitInterfaceImplementations.Length > 0 => true,
            _ => false,
        };
    }

    private static bool HasSelfLoop(ProjectId projectId, IReadOnlyDictionary<ProjectId, List<ProjectId>> graph)
        => graph.TryGetValue(projectId, out var references) && references.Contains(projectId);

    private static IReadOnlyList<IReadOnlyList<ProjectId>> TarjanStronglyConnectedComponents(
        IReadOnlyDictionary<ProjectId, List<ProjectId>> graph,
        CancellationToken cancellationToken)
    {
        var index = 0;
        var stack = new Stack<ProjectId>();
        var indices = new Dictionary<ProjectId, int>();
        var lowLinks = new Dictionary<ProjectId, int>();
        var onStack = new HashSet<ProjectId>();
        var result = new List<IReadOnlyList<ProjectId>>();

        foreach (var node in graph.Keys)
        {
            if (!indices.ContainsKey(node))
                Visit(node);
        }

        return result.AsReadOnly();

        void Visit(ProjectId node)
        {
            cancellationToken.ThrowIfCancellationRequested();

            indices[node] = index;
            lowLinks[node] = index;
            index++;
            stack.Push(node);
            onStack.Add(node);

            foreach (var neighbour in graph.TryGetValue(node, out var references) ? references : [])
            {
                if (!indices.ContainsKey(neighbour))
                {
                    Visit(neighbour);
                    lowLinks[node] = Math.Min(lowLinks[node], lowLinks[neighbour]);
                }
                else if (onStack.Contains(neighbour))
                {
                    lowLinks[node] = Math.Min(lowLinks[node], indices[neighbour]);
                }
            }

            if (lowLinks[node] != indices[node])
                return;

            var component = new List<ProjectId>();
            ProjectId current;

            do
            {
                current = stack.Pop();
                onStack.Remove(current);
                component.Add(current);
            } while (current != node);

            result.Add(component.AsReadOnly());
        }
    }

    private static IReadOnlyList<NugetPackageView> ReadPackageReferences(string projectPath)
    {
        var document = XDocument.Load(projectPath);
        var packageReferences = document.Descendants()
            .Where(static element => string.Equals(element.Name.LocalName, "PackageReference", StringComparison.OrdinalIgnoreCase))
            .Select(element =>
            {
                var packageId = element.Attribute("Include")?.Value
                    ?? element.Attribute("Update")?.Value
                    ?? string.Empty;
                var version = element.Attribute("Version")?.Value
                    ?? element.Elements().FirstOrDefault(static child => string.Equals(child.Name.LocalName, "Version", StringComparison.OrdinalIgnoreCase))?.Value
                    ?? string.Empty;

                return new NugetPackageView(packageId, version);
            })
            .Where(static package => !string.IsNullOrWhiteSpace(package.PackageId))
            .ToList();

        return packageReferences.AsReadOnly();
    }

    private static class CyclomaticComplexityCalculator
    {
        public static int Calculate(SyntaxNode declaration)
        {
            var walker = new ComplexityWalker();
            walker.Visit(declaration);
            return 1 + walker.DecisionPoints;
        }

        private sealed class ComplexityWalker : CSharpSyntaxWalker
        {
            public int DecisionPoints { get; private set; }

            public override void VisitIfStatement(IfStatementSyntax node)
            {
                DecisionPoints++;
                base.VisitIfStatement(node);
            }

            public override void VisitForStatement(ForStatementSyntax node)
            {
                DecisionPoints++;
                base.VisitForStatement(node);
            }

            public override void VisitForEachStatement(ForEachStatementSyntax node)
            {
                DecisionPoints++;
                base.VisitForEachStatement(node);
            }

            public override void VisitWhileStatement(WhileStatementSyntax node)
            {
                DecisionPoints++;
                base.VisitWhileStatement(node);
            }

            public override void VisitDoStatement(DoStatementSyntax node)
            {
                DecisionPoints++;
                base.VisitDoStatement(node);
            }

            public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
            {
                DecisionPoints++;
                base.VisitConditionalExpression(node);
            }

            public override void VisitCatchClause(CatchClauseSyntax node)
            {
                DecisionPoints++;
                base.VisitCatchClause(node);
            }

            public override void VisitCaseSwitchLabel(CaseSwitchLabelSyntax node)
            {
                DecisionPoints++;
                base.VisitCaseSwitchLabel(node);
            }

            public override void VisitSwitchExpressionArm(SwitchExpressionArmSyntax node)
            {
                DecisionPoints++;
                base.VisitSwitchExpressionArm(node);
            }

            public override void VisitBinaryExpression(BinaryExpressionSyntax node)
            {
                if (node.IsKind(SyntaxKind.LogicalAndExpression) || node.IsKind(SyntaxKind.LogicalOrExpression))
                    DecisionPoints++;

                base.VisitBinaryExpression(node);
            }
        }
    }
}
