using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynLib.Domain.Models;

namespace RoslynLib.Infrastructure;

public sealed class RoslynSymbolAnalysisService
{
    // ──────────────────────────────────────────── Call Tree ───────────────────

    public async Task<IReadOnlyList<CallTreeNode>> BuildCallTreeAsync(
        IMethodSymbol method,
        Solution solution,
        CancellationToken cancellationToken = default)
    {
        var root = CreateCallTreeNode(method, isCallee: false);

        await LoadCallTreeChildrenAsync(
            root,
            solution,
            [GetMethodKey(method)],
            cancellationToken)
            .ConfigureAwait(false);

        return [root];
    }

    public async Task<IReadOnlyList<CallTreeNode>> LoadCallTreeChildrenAsync(
        CallTreeNode node,
        Solution solution,
        IEnumerable<string>? ancestorKeys = null,
        CancellationToken cancellationToken = default)
    {
        if (node.IsChildrenLoaded)
        {
            return node.Children.OfType<CallTreeNode>().ToList().AsReadOnly();
        }

        if (node.MethodSymbol is null)
        {
            node.IsChildrenLoaded = true;
            node.HasUnloadedChildren = false;
            return [];
        }

        var visited = ancestorKeys is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(ancestorKeys, StringComparer.Ordinal);

        visited.Add(GetMethodKey(node.MethodSymbol));

        var callers = await SymbolFinder.FindCallersAsync(node.MethodSymbol, solution, cancellationToken)
            .ConfigureAwait(false);

        // Deduplicate CallingSymbol to avoid duplicates from partial classes / multi-project refs
        var seenCallers = new HashSet<string>(StringComparer.Ordinal);

        foreach (var caller in callers)
        {
            if (caller.CallingSymbol is not IMethodSymbol callerMethod) 
                continue;

            var callerKey = GetMethodKey(callerMethod);
            if (!seenCallers.Add(callerKey)) 
                continue;

            if (visited.Contains(callerKey))
            {
                var recursionStubNode = CreateCallTreeNode(callerMethod, isCallee: true);
                recursionStubNode.IsChildrenLoaded = true;
                recursionStubNode.HasUnloadedChildren = false;
                node.Children.Add(recursionStubNode);
                continue;
            }

            var childVisited = new HashSet<string>(visited, StringComparer.Ordinal)
            {
                callerKey,
            };

            var childNode = CreateCallTreeNode(callerMethod, isCallee: true);
            childNode.HasUnloadedChildren = await HasAnyCallersAsync(
                    callerMethod,
                    solution,
                    childVisited,
                    cancellationToken)
                .ConfigureAwait(false);
            childNode.IsChildrenLoaded = false;

            node.Children.Add(childNode);
        }

        node.IsChildrenLoaded = true;
        node.HasUnloadedChildren = node.Children.Count > 0;

        return node.Children.OfType<CallTreeNode>().ToList().AsReadOnly();
    }

    private static CallTreeNode CreateCallTreeNode(IMethodSymbol method, bool isCallee)
    {
        var location = method.Locations.FirstOrDefault();
        var filePath = location?.SourceTree?.FilePath ?? string.Empty;
        var lineNumber = location?.GetLineSpan().StartLinePosition.Line + 1 ?? 0;
        var sourceDisplay = RoslynSymbolHelper.BuildSourceText(filePath, lineNumber);
        var methodDisplayName = RoslynSymbolHelper.BuildMemberDisplayName(method);
        var assemblyName = method.ContainingAssembly?.Name ?? string.Empty;

        return new CallTreeNode
        {
            MethodSymbol = method,
            Name = method.Name,
            FullName = method.ToDisplayString(),
            Kind = RoslynSymbolHelper.GetKind(method),
            ContainingType = method.ContainingType?.ToDisplayString() ?? string.Empty,
            FilePath = filePath,
            LineNumber = lineNumber,
            IsCallee = isCallee,
            SourceDisplay = sourceDisplay,
            DisplayName = methodDisplayName,
            ProjectName = assemblyName,
            AssemblyName = assemblyName,
            Accessibility = method.DeclaredAccessibility.ToString(),
        };
    }

    private static async Task<bool> HasAnyCallersAsync(
        IMethodSymbol method,
        Solution solution,
        ISet<string> excludedKeys,
        CancellationToken cancellationToken)
    {
        var callers = await SymbolFinder.FindCallersAsync(method, solution, cancellationToken)
            .ConfigureAwait(false);

        foreach (var caller in callers)
        {
            if (caller.CallingSymbol is not IMethodSymbol callerMethod) 
                continue;

            var callerKey = GetMethodKey(callerMethod);
            if (!excludedKeys.Contains(callerKey))
                return true;
        }

        return false;
    }

    private static string GetMethodKey(IMethodSymbol method)
        => method.ToDisplayString();

    // ──────────────────────────────────────────── References ──────────────────

    public async Task<IReadOnlyList<ReferenceTreeNode>> BuildReferenceTreeAsync(
        ISymbol symbol,
        Solution solution,
        CancellationToken cancellationToken = default)
    {
        var references = await SymbolFinder.FindReferencesAsync(symbol, solution, cancellationToken)
            .ConfigureAwait(false);
        var sourceLocation = RoslynSymbolHelper.GetSourceLocation(symbol);
        var fileGroups = references
            .SelectMany(static referencedSymbol => referencedSymbol.Locations)
            .Where(static location => !string.IsNullOrWhiteSpace(location.Location.GetLineSpan().Path))
            .GroupBy(static location => location.Location.GetLineSpan().Path, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var root = new ReferenceTreeNode
        {
            Name = RoslynSymbolHelper.FormatSymbolDisplayName(symbol),
            FullName = symbol.ToDisplayString(),
            Kind = RoslynSymbolHelper.GetKind(symbol),
            TargetSymbol = symbol,
            IsChildrenLoaded = true,
            ContainingNamespace = RoslynSymbolHelper.GetContainingNamespaceName(symbol),
            ContainingTypeName = symbol.ContainingType?.Name ?? string.Empty,
            ProjectName = symbol.ContainingAssembly?.Name ?? string.Empty,
            AssemblyName = symbol.ContainingAssembly?.Name ?? string.Empty,
            Accessibility = symbol.DeclaredAccessibility.ToString(),
            OccurrenceCount = fileGroups.Sum(static group => group.Count()),
            LocationDisplay = RoslynSymbolHelper.BuildSourceText(sourceLocation.FilePath, sourceLocation.LineNumber),
        };

        foreach (var fileGroup in fileGroups)
        {
            var firstLocation = fileGroup.First();
            var path = fileGroup.Key;

            root.Children.Add(new ReferenceTreeNode
            {
                Name = $"{System.IO.Path.GetFileName(path)} ({fileGroup.Count()})",
                FullName = path,
                Kind = SymbolNodeKind.Reference,
                TargetSymbol = symbol,
                IsFileGroup = true,
                FilePath = path,
                HasUnloadedChildren = true,
                IsChildrenLoaded = false,
                ProjectName = firstLocation.Document.Project.Name,
                AssemblyName = firstLocation.Document.Project.AssemblyName ?? firstLocation.Document.Project.Name,
                Accessibility = symbol.DeclaredAccessibility.ToString(),
                OccurrenceCount = fileGroup.Count(),
                LocationDisplay = path,
            });
        }

        root.HasUnloadedChildren = root.Children.Count > 0;
        return [root];
    }

    public async Task<IReadOnlyList<ReferenceTreeNode>> LoadReferenceTreeChildrenAsync(
        ReferenceTreeNode fileGroupNode,
        Solution solution,
        CancellationToken cancellationToken = default)
    {
        if (fileGroupNode.IsChildrenLoaded)
            return fileGroupNode.Children.OfType<ReferenceTreeNode>().ToList().AsReadOnly();

        if (!fileGroupNode.IsFileGroup || fileGroupNode.TargetSymbol is null)
        {
            fileGroupNode.IsChildrenLoaded = true;
            fileGroupNode.HasUnloadedChildren = false;
            return [];
        }

        var references = await SymbolFinder.FindReferencesAsync(fileGroupNode.TargetSymbol, solution, cancellationToken)
            .ConfigureAwait(false);

        var seenLocations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var referencedSymbol in references)
        {
            foreach (var location in referencedSymbol.Locations)
            {
                var span = location.Location.GetLineSpan();
                if (!string.Equals(span.Path, fileGroupNode.FilePath, StringComparison.OrdinalIgnoreCase))
                    continue;

                var line = span.StartLinePosition.Line;
                var col = span.StartLinePosition.Character;
                var lineText = string.Empty;
                var locationKey = string.Concat(span.Path, ":", line, ":", col);
                var referenceKind = ReferenceKind.Other;
                var locationDisplay = $"{System.IO.Path.GetFileName(span.Path)}:{line + 1}:{col + 1}";

                if (!seenLocations.Add(locationKey))
                    continue;

                var sourceTree = location.Location.SourceTree;
                if (sourceTree is not null)
                {
                    var text = await sourceTree.GetTextAsync(cancellationToken).ConfigureAwait(false);
                    if (line < text.Lines.Count)
                        lineText = text.Lines[line].ToString().Trim();
                }

                var syntaxRoot = await location.Document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                var syntaxNode = syntaxRoot?.FindNode(location.Location.SourceSpan, getInnermostNodeForTie: true);
                referenceKind = DetermineReferenceKind(syntaxNode);

                // Resolve containing symbol (namespace / class / method-with-params) via semantic model
                string containingNs = string.Empty;
                string containingTypeName = string.Empty;
                string containingSymbolDisplay = string.Empty;

                var semanticModel = await location.Document
                    .GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                if (semanticModel is not null)
                {
                    var enclosingSymbol = semanticModel.GetEnclosingSymbol(
                        location.Location.SourceSpan.Start, cancellationToken);
                    containingNs = RoslynSymbolHelper.GetContainingNamespaceName(enclosingSymbol);
                    containingTypeName = enclosingSymbol?.ContainingType?.Name ?? string.Empty;
                    containingSymbolDisplay = RoslynSymbolHelper.FormatSymbolDisplayName(enclosingSymbol);
                }

                fileGroupNode.Children.Add(new ReferenceTreeNode
                {
                    Name = $"{referenceKind} • L{line + 1}:{col + 1}",
                    FullName = $"{span.Path}:{line + 1}:{col + 1}",
                    Kind = SymbolNodeKind.Reference,
                    FilePath = span.Path,
                    LineNumber = line + 1,
                    Column = col + 1,
                    ReferenceKind = referenceKind,
                    LineText = lineText,
                    ContainingSymbol = containingSymbolDisplay,
                    ContainingNamespace = containingNs,
                    ContainingTypeName = containingTypeName,
                    ContainingContext = RoslynSymbolHelper.BuildContainingContext(containingNs, containingTypeName, containingSymbolDisplay),
                    ProjectName = location.Document.Project.Name,
                    AssemblyName = location.Document.Project.AssemblyName ?? location.Document.Project.Name,
                    Accessibility = fileGroupNode.TargetSymbol.DeclaredAccessibility.ToString(),
                    OccurrenceCount = 1,
                    LocationDisplay = locationDisplay,
                    IsChildrenLoaded = true,
                });
            }
        }

        fileGroupNode.IsChildrenLoaded = true;
        fileGroupNode.HasUnloadedChildren = false;

        return fileGroupNode.Children.OfType<ReferenceTreeNode>().ToList().AsReadOnly();
    }

    private static ReferenceKind DetermineReferenceKind(SyntaxNode? syntaxNode)
    {
        if (syntaxNode is null)
            return ReferenceKind.Other;

        var invocation = syntaxNode.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
        if (invocation?.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" })
            return ReferenceKind.NameOf;

        if (syntaxNode.AncestorsAndSelf().OfType<TypeOfExpressionSyntax>().Any())
            return ReferenceKind.TypeOf;

        if (syntaxNode.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().Any())
            return ReferenceKind.Invocation;

        if (syntaxNode.Parent is AssignmentExpressionSyntax assignment)
            return assignment.Left.Span.IntersectsWith(syntaxNode.Span)
                ? ReferenceKind.Write
                : ReferenceKind.Read;

        if (syntaxNode.Parent is PrefixUnaryExpressionSyntax prefixExpression
            && (prefixExpression.IsKind(SyntaxKind.PreIncrementExpression) || prefixExpression.IsKind(SyntaxKind.PreDecrementExpression)))
        {
            return ReferenceKind.Write;
        }

        if (syntaxNode.Parent is PostfixUnaryExpressionSyntax postfixExpression
            && (postfixExpression.IsKind(SyntaxKind.PostIncrementExpression) || postfixExpression.IsKind(SyntaxKind.PostDecrementExpression)))
        {
            return ReferenceKind.Write;
        }

        if (syntaxNode.Parent is ArgumentSyntax argument && argument.RefOrOutKeyword.RawKind != 0)
            return ReferenceKind.Write;

        return ReferenceKind.Read;
    }
}
