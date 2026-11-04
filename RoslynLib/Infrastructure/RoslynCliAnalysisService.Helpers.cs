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

public sealed partial class RoslynCliAnalysisService
{
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
