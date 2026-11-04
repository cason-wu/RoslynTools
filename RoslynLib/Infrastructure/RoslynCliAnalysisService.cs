using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynLib.Domain.Models;

namespace RoslynLib.Infrastructure;

public sealed partial class RoslynCliAnalysisService
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
            Limit(ordered, options),
            ordered.Count,
            IsTruncated(ordered, options));
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
            Limit(ordered, options),
            ordered.Count,
            IsTruncated(ordered, options));
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
            Limit(results, options),
            results.Count,
            IsTruncated(results, options));
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
            Limit(results, options),
            results.Count,
            IsTruncated(results, options));
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

        return new NamespaceDependenciesResult("deps namespaces", Limit(results, options), results.Count, IsTruncated(results, options));
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
            members,
            symbol.ToDisplayString(SignatureFormat),
            GetReturnTypeName(symbol),
            ExtractDocumentationSummary(symbol, cancellationToken),
            await GetDeclarationRangesAsync(symbol, workspaceTarget, cancellationToken).ConfigureAwait(false));
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

        return new SymbolSearchResult("sym search", pattern, Limit(results, options), results.Count, IsTruncated(results, options));
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
            Limit(results, options),
            results.Count,
            IsTruncated(results, options));
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

        return new UnusedSymbolsResult("dead unused", Limit(ordered, options), ordered.Count, IsTruncated(ordered, options));
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

        return new ComplexityAnalysisResult("dead complexity", threshold, Limit(ordered, options), ordered.Count, IsTruncated(ordered, options));
    }

    public async Task<ObsoleteUsagesResult> FindObsoleteUsagesAsync(
        LoadedWorkspaceTarget workspaceTarget,
        RoslynCliQueryOptions options,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = await CollectDiagnosticsAsync(workspaceTarget, options, includeAnalyzers: false, cancellationToken).ConfigureAwait(false);
        var obsoleteDiagnostics = diagnostics
            .Select(static item => item.Diagnostic)
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

        return new ObsoleteUsagesResult("dead obsolete", Limit(obsoleteDiagnostics, options), obsoleteDiagnostics.Count, IsTruncated(obsoleteDiagnostics, options));
    }

    public Task<DiagnosticsResult> GetErrorsAsync(
        LoadedWorkspaceTarget workspaceTarget,
        RoslynCliQueryOptions options,
        CancellationToken cancellationToken = default)
        => GetDiagnosticsAsync(
            workspaceTarget,
            options,
            static severity => severity == DiagnosticSeverity.Error,
            includeAnalyzers: true,
            "diag errors",
            cancellationToken);

    public Task<DiagnosticsResult> GetWarningsAsync(
        LoadedWorkspaceTarget workspaceTarget,
        RoslynCliQueryOptions options,
        CancellationToken cancellationToken = default)
        => GetDiagnosticsAsync(
            workspaceTarget,
            options,
            static severity => severity == DiagnosticSeverity.Warning,
            includeAnalyzers: true,
            "diag warnings",
            cancellationToken);

    public Task<DiagnosticsResult> RunAnalyzersAsync(
        LoadedWorkspaceTarget workspaceTarget,
        RoslynCliQueryOptions options,
        CancellationToken cancellationToken = default)
        => GetDiagnosticsAsync(
            workspaceTarget,
            options,
            static severity => severity != DiagnosticSeverity.Hidden,
            includeAnalyzers: true,
            "diag analyze",
            cancellationToken);

    // severity: error | warning | all. Analyzers are opt-in because they are much slower than compiler diagnostics.
    public Task<DiagnosticsResult> QueryDiagnosticsAsync(
        LoadedWorkspaceTarget workspaceTarget,
        RoslynCliQueryOptions options,
        string? severity,
        bool includeAnalyzers,
        CancellationToken cancellationToken = default)
        => GetDiagnosticsAsync(
            workspaceTarget,
            options,
            ParseSeverityFilter(severity),
            includeAnalyzers,
            "diag query",
            cancellationToken);
}
