namespace RoslynLib.Infrastructure;

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
