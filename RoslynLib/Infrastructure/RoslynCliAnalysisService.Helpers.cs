using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using RoslynLib.Domain.Models;

namespace RoslynLib.Infrastructure;

public sealed partial class RoslynCliAnalysisService
{
    private readonly ConditionalWeakTable<Solution, IReadOnlyList<ISymbol>> _sourceSymbolCache = new();

    private async Task<DiagnosticsResult> GetDiagnosticsAsync(
        LoadedWorkspaceTarget workspaceTarget,
        RoslynCliQueryOptions options,
        Func<DiagnosticSeverity, bool> severityFilter,
        bool includeAnalyzers,
        string command,
        CancellationToken cancellationToken)
    {
        var diagnostics = await CollectDiagnosticsAsync(workspaceTarget, options, includeAnalyzers, cancellationToken).ConfigureAwait(false);
        var filtered = diagnostics
            .Where(item => severityFilter(item.Diagnostic.Severity))
            .Where(item => item.Diagnostic.Location == Location.None || !RoslynSymbolHelper.IsPathExcluded(item.Diagnostic.Location.GetLineSpan().Path, options.Excludes))
            .Select(item =>
            {
                var diagnostic = item.Diagnostic;
                var span = diagnostic.Location == Location.None
                    ? default
                    : diagnostic.Location.GetLineSpan();
                return new DiagnosticEntryView(
                    diagnostic.Id,
                    diagnostic.Severity.ToString(),
                    diagnostic.GetMessage(),
                    item.ProjectName,
                    diagnostic.Location == Location.None
                        ? string.Empty
                        : FormatOutputPath(span.Path, workspaceTarget),
                    diagnostic.Location == Location.None ? 0 : span.StartLinePosition.Line + 1,
                    diagnostic.Location == Location.None ? 0 : span.StartLinePosition.Character + 1,
                    diagnostic.Descriptor.Category);
            })
            .Distinct()
            .OrderBy(static item => GetSeverityRank(item.Severity))
            .ThenBy(static item => item.File, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Line)
            .ThenBy(static item => item.Column)
            .ToList()
            .AsReadOnly();

        return new DiagnosticsResult(command, Limit(filtered, options), filtered.Count, IsTruncated(filtered, options));
    }

    private static int GetSeverityRank(string severity) => severity switch
    {
        nameof(DiagnosticSeverity.Error) => 0,
        nameof(DiagnosticSeverity.Warning) => 1,
        nameof(DiagnosticSeverity.Info) => 2,
        _ => 3,
    };

    private static Func<DiagnosticSeverity, bool> ParseSeverityFilter(string? severity)
    {
        return (severity ?? "error").Trim().ToLowerInvariant() switch
        {
            "error" or "errors" => static value => value == DiagnosticSeverity.Error,
            "warning" or "warnings" => static value => value == DiagnosticSeverity.Warning,
            "all" => static value => value != DiagnosticSeverity.Hidden,
            _ => throw new InvalidOperationException($"Unknown severity '{severity}'. Expected error, warning, or all."),
        };
    }

    private static IReadOnlyList<T> Limit<T>(IReadOnlyList<T> items, RoslynCliQueryOptions options)
        => IsTruncated(items, options)
            ? items.Take(options.MaxResults!.Value).ToList().AsReadOnly()
            : items;

    private static bool IsTruncated<T>(IReadOnlyList<T> items, RoslynCliQueryOptions options)
        => options.MaxResults is int maxResults && maxResults > 0 && items.Count > maxResults;

    private async Task<ISymbol> ResolveSymbolAsync(
        LoadedWorkspaceTarget workspaceTarget,
        RoslynCliQueryOptions options,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(options.File) || options.Line is not null)
        {
            if (string.IsNullOrWhiteSpace(options.File) || options.Line is null)
                throw new InvalidOperationException("file and line must be provided together (CLI: --file and --line).");

            return await ResolveSymbolByLocationAsync(
                    workspaceTarget,
                    options.File!,
                    options.Line.Value,
                    options.Column,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(options.Symbol))
            throw new InvalidOperationException("symbol is required unless file and line are provided (CLI: --symbol, or --file and --line).");

        var symbols = await GetAllSourceSymbolsAsync(workspaceTarget, cancellationToken).ConfigureAwait(false);
        var matches = symbols
            .Select(symbol => new { Symbol = symbol, Score = ScoreSymbolMatch(symbol, options.Symbol!) })
            .Where(static item => item.Score < int.MaxValue)
            .OrderBy(static item => item.Score)
            .ThenBy(item => item.Symbol.ToDisplayString(), StringComparer.Ordinal)
            .ToList();

        if (matches.Count == 0)
            throw new InvalidOperationException($"Unable to resolve symbol '{options.Symbol}'. Use a wildcard symbol search (for example '*{options.Symbol}*') to find candidates.");

        var bestMatches = matches
            .TakeWhile(item => item.Score == matches[0].Score)
            .Select(static item => item.Symbol)
            .ToList();

        // The same namespace (or a linked source file) can surface once per assembly; that is not a real ambiguity.
        var distinctMatchCount = bestMatches
            .Select(static symbol => (symbol.ToDisplayString(), RoslynSymbolHelper.GetKind(symbol)))
            .Distinct()
            .Count();

        if (distinctMatchCount > 1)
            throw new InvalidOperationException(BuildAmbiguousSymbolMessage(options.Symbol!, bestMatches, workspaceTarget));

        return bestMatches[0];
    }

    private const int MaxAmbiguousCandidates = 10;

    private static string BuildAmbiguousSymbolMessage(
        string query,
        IReadOnlyList<ISymbol> candidates,
        LoadedWorkspaceTarget workspaceTarget)
    {
        var lines = candidates
            .Take(MaxAmbiguousCandidates)
            .Select(candidate =>
            {
                var source = RoslynSymbolHelper.GetSourceLocation(candidate);
                var assembly = candidate.ContainingAssembly?.Name ?? string.Empty;
                return $"  - {candidate.ToDisplayString()} [{RoslynSymbolHelper.GetKind(candidate)}] {FormatOutputPath(source.FilePath, workspaceTarget)}:{source.LineNumber} ({assembly})";
            });

        var remaining = candidates.Count > MaxAmbiguousCandidates
            ? $"{Environment.NewLine}  ... and {candidates.Count - MaxAmbiguousCandidates} more."
            : string.Empty;

        return $"Symbol '{query}' is ambiguous ({candidates.Count} matches):{Environment.NewLine}"
            + string.Join(Environment.NewLine, lines)
            + remaining
            + $"{Environment.NewLine}Specify a fully qualified name or signature (for example 'Namespace.Type.Method(int)'), or use file + line to select the symbol.";
    }

    private async Task<ISymbol> ResolveSymbolByLocationAsync(
        LoadedWorkspaceTarget workspaceTarget,
        string filePath,
        int lineNumber,
        int? columnNumber,
        CancellationToken cancellationToken)
    {
        var document = FindDocuments(workspaceTarget, filePath).FirstOrDefault()
            ?? throw new InvalidOperationException($"Unable to find document '{filePath}' in the current target. Use an absolute path or a path relative to the target directory.");

        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (syntaxRoot is null || semanticModel is null)
            throw new InvalidOperationException($"Unable to create semantic model for '{filePath}'.");

        var zeroBasedLine = lineNumber - 1;
        if (zeroBasedLine < 0 || zeroBasedLine >= text.Lines.Count)
            throw new InvalidOperationException($"Line '{lineNumber}' is outside the bounds of '{filePath}' ({text.Lines.Count} lines).");

        var line = text.Lines[zeroBasedLine];

        if (columnNumber is int column)
        {
            if (column < 1 || column > line.Span.Length + 1)
                throw new InvalidOperationException($"Column '{column}' is outside the bounds of line {lineNumber} in '{filePath}' ({line.Span.Length} characters).");

            var token = syntaxRoot.FindToken(line.Start + column - 1);
            foreach (var node in token.Parent?.AncestorsAndSelf() ?? [])
            {
                var symbol = ResolveNodeSymbol(semanticModel, node, cancellationToken);
                if (symbol is not null)
                    return symbol;
            }
        }

        var candidateNodes = syntaxRoot.DescendantNodes(line.Span)
            .OrderBy(static node => node.Span.Length)
            .ToList();

        foreach (var node in candidateNodes)
        {
            var symbol = ResolveNodeSymbol(semanticModel, node, cancellationToken);
            if (symbol is not null)
                return symbol;
        }

        var enclosingSymbol = semanticModel.GetEnclosingSymbol(line.Start, cancellationToken);
        return enclosingSymbol ?? throw new InvalidOperationException($"No symbol found at {filePath}:{lineNumber}.");
    }

    private static ISymbol? ResolveNodeSymbol(SemanticModel semanticModel, SyntaxNode node, CancellationToken cancellationToken)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(node, cancellationToken);
        return semanticModel.GetDeclaredSymbol(node, cancellationToken)
            ?? symbolInfo.Symbol
            ?? symbolInfo.CandidateSymbols.FirstOrDefault();
    }

    private static IReadOnlyList<Document> FindDocuments(LoadedWorkspaceTarget workspaceTarget, string filePath)
    {
        var fullPath = ResolveDocumentPath(workspaceTarget, filePath);
        var documents = workspaceTarget.Solution.GetDocumentIdsWithFilePath(fullPath)
            .Select(documentId => workspaceTarget.Solution.GetDocument(documentId))
            .OfType<Document>()
            .ToList();

        if (documents.Count > 0)
            return documents.AsReadOnly();

        return workspaceTarget.Solution.Projects
            .SelectMany(static project => project.Documents)
            .Where(candidate => string.Equals(
                Path.GetFullPath(candidate.FilePath ?? string.Empty),
                fullPath,
                StringComparison.OrdinalIgnoreCase))
            .ToList()
            .AsReadOnly();
    }

    private async Task<IReadOnlyList<ISymbol>> GetAllSourceSymbolsAsync(
        LoadedWorkspaceTarget workspaceTarget,
        CancellationToken cancellationToken)
    {
        // Solution snapshots are immutable, so the symbol list stays valid until the session swaps in a new snapshot.
        if (_sourceSymbolCache.TryGetValue(workspaceTarget.Solution, out var cached))
            return cached;

        var symbols = new List<ISymbol>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var project in workspaceTarget.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null)
                continue;

            foreach (var symbol in EnumerateNamespaceSymbols(compilation.Assembly.GlobalNamespace))
            {
                var key = BuildSymbolIdentityKey(symbol);
                if (seen.Add(key))
                    symbols.Add(symbol);
            }
        }

        var result = symbols.AsReadOnly();
        _sourceSymbolCache.AddOrUpdate(workspaceTarget.Solution, result);
        return result;
    }

    // Walk the source assembly's own namespace tree; the compilation-level global namespace merges
    // referenced assemblies, and merged namespaces have no ContainingAssembly to filter on.
    private static IEnumerable<ISymbol> EnumerateNamespaceSymbols(INamespaceSymbol namespaceSymbol)
    {
        if (!namespaceSymbol.IsGlobalNamespace)
            yield return namespaceSymbol;

        foreach (var nestedNamespace in namespaceSymbol.GetNamespaceMembers())
        {
            foreach (var symbol in EnumerateNamespaceSymbols(nestedNamespace))
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

    private static readonly SymbolDisplayFormat QualifiedNameWithoutParametersFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeContainingType);

    private static readonly SymbolDisplayFormat QualifiedNameWithoutParametersOrGenericsFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.None,
        memberOptions: SymbolDisplayMemberOptions.IncludeContainingType);

    private static int ScoreSymbolMatch(ISymbol symbol, string query)
    {
        if (string.Equals(symbol.ToDisplayString(), query, StringComparison.OrdinalIgnoreCase))
            return 0;

        if (string.Equals(RoslynSymbolHelper.FormatSymbolDisplayName(symbol), query, StringComparison.OrdinalIgnoreCase))
            return 1;

        var qualifiedName = symbol.ToDisplayString(QualifiedNameWithoutParametersFormat);
        var qualifiedNameWithoutGenerics = symbol.ToDisplayString(QualifiedNameWithoutParametersOrGenericsFormat);
        if (string.Equals(qualifiedName, query, StringComparison.OrdinalIgnoreCase)
            || string.Equals(qualifiedNameWithoutGenerics, query, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (string.Equals(symbol.Name, query, StringComparison.OrdinalIgnoreCase))
            return 3;

        if (symbol.ToDisplayString().EndsWith($".{query}", StringComparison.OrdinalIgnoreCase))
            return 4;

        // Lets 'Type.Method' match methods, whose full display string includes the parameter list
        // (and generic methods, whose qualified name includes '<T>').
        if (qualifiedName.EndsWith($".{query}", StringComparison.OrdinalIgnoreCase)
            || qualifiedNameWithoutGenerics.EndsWith($".{query}", StringComparison.OrdinalIgnoreCase))
        {
            return 5;
        }

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

    private sealed record ProjectDiagnostic(Diagnostic Diagnostic, string ProjectName);

    private static async Task<IReadOnlyList<ProjectDiagnostic>> CollectDiagnosticsAsync(
        LoadedWorkspaceTarget workspaceTarget,
        RoslynCliQueryOptions options,
        bool includeAnalyzers,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<ProjectDiagnostic>();

        if (!string.IsNullOrWhiteSpace(options.File))
        {
            var documents = FindDocuments(workspaceTarget, options.File);
            if (documents.Count == 0)
                throw new InvalidOperationException($"Unable to find document '{options.File}' in the current target. Use an absolute path or a path relative to the target directory.");

            foreach (var document in documents.Where(document => MatchesProject(document.Project, options.Project)))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                if (semanticModel is null)
                    continue;

                diagnostics.AddRange(semanticModel.GetDiagnostics(cancellationToken: cancellationToken)
                    .Select(diagnostic => new ProjectDiagnostic(diagnostic, document.Project.Name)));

                var compilationWithAnalyzers = includeAnalyzers
                    ? CreateCompilationWithAnalyzers(document.Project, semanticModel.Compilation)
                    : null;
                if (compilationWithAnalyzers is null)
                    continue;

                var syntaxDiagnostics = await compilationWithAnalyzers
                    .GetAnalyzerSyntaxDiagnosticsAsync(semanticModel.SyntaxTree, cancellationToken)
                    .ConfigureAwait(false);
                var semanticDiagnostics = await compilationWithAnalyzers
                    .GetAnalyzerSemanticDiagnosticsAsync(semanticModel, filterSpan: null, cancellationToken)
                    .ConfigureAwait(false);

                diagnostics.AddRange(syntaxDiagnostics.Concat(semanticDiagnostics)
                    .Select(diagnostic => new ProjectDiagnostic(diagnostic, document.Project.Name)));
            }

            return diagnostics.AsReadOnly();
        }

        var projects = workspaceTarget.Projects
            .Where(project => MatchesProject(project, options.Project))
            .ToList();

        if (projects.Count == 0)
        {
            var available = string.Join(", ", workspaceTarget.Projects.Select(static project => project.Name).Order(StringComparer.Ordinal));
            throw new InvalidOperationException($"No project matches '{options.Project}'. Available projects: {available}.");
        }

        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null)
                continue;

            diagnostics.AddRange(compilation.GetDiagnostics(cancellationToken)
                .Select(diagnostic => new ProjectDiagnostic(diagnostic, project.Name)));

            var compilationWithAnalyzers = includeAnalyzers
                ? CreateCompilationWithAnalyzers(project, compilation)
                : null;
            if (compilationWithAnalyzers is null)
                continue;

            var analyzerDiagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            diagnostics.AddRange(analyzerDiagnostics.Select(diagnostic => new ProjectDiagnostic(diagnostic, project.Name)));
        }

        return diagnostics.AsReadOnly();
    }

    private static CompilationWithAnalyzers? CreateCompilationWithAnalyzers(Project project, Compilation compilation)
    {
        var analyzers = project.AnalyzerReferences
            .SelectMany(reference => reference.GetAnalyzers(project.Language))
            .ToImmutableArray();

        // Pass the project's analyzer options so .editorconfig / globalconfig severities are honored.
        return analyzers.Length == 0
            ? null
            : compilation.WithAnalyzers(analyzers, project.AnalyzerOptions);
    }

    private static bool MatchesProject(Project project, string? projectFilter)
        => string.IsNullOrWhiteSpace(projectFilter)
            || RoslynSymbolHelper.WildcardMatch(project.Name, projectFilter);

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
