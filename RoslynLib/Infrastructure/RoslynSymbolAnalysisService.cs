using Microsoft.CodeAnalysis;
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

        return new CallTreeNode
        {
            MethodSymbol = method,
            Name = method.Name,
            FullName = method.ToDisplayString(),
            Kind = SymbolNodeKind.Method,
            ContainingType = method.ContainingType?.ToDisplayString() ?? string.Empty,
            FilePath = location?.SourceTree?.FilePath ?? string.Empty,
            LineNumber = location?.GetLineSpan().StartLinePosition.Line + 1 ?? 0,
            IsCallee = isCallee,
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

        var root = new ReferenceTreeNode
        {
            Name = FormatSymbolDisplayName(symbol),
            FullName = symbol.ToDisplayString(),
            Kind = MapSymbolKind(symbol),
            TargetSymbol = symbol,
            IsChildrenLoaded = true,
            ContainingNamespace = GetContainingNamespaceName(symbol),
            ContainingTypeName = symbol.ContainingType?.Name ?? string.Empty,
        };

        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var referencedSymbol in references)
        {
            foreach (var location in referencedSymbol.Locations)
            {
                var path = location.Location.GetLineSpan().Path;
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                if (!seenFiles.Add(path))
                    continue;

                root.Children.Add(new ReferenceTreeNode
                {
                    Name = System.IO.Path.GetFileName(path),
                    FullName = path,
                    Kind = SymbolNodeKind.Reference,
                    TargetSymbol = symbol,
                    IsFileGroup = true,
                    FilePath = path,
                    HasUnloadedChildren = true,
                    IsChildrenLoaded = false,
                });
            }
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

                if (!seenLocations.Add(locationKey))
                    continue;

                var sourceTree = location.Location.SourceTree;
                if (sourceTree is not null)
                {
                    var text = await sourceTree.GetTextAsync(cancellationToken).ConfigureAwait(false);
                    if (line < text.Lines.Count)
                        lineText = text.Lines[line].ToString().Trim();
                }

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
                    containingNs = GetContainingNamespaceName(enclosingSymbol);
                    containingTypeName = enclosingSymbol?.ContainingType?.Name ?? string.Empty;
                    containingSymbolDisplay = FormatSymbolDisplayName(enclosingSymbol);
                }

                fileGroupNode.Children.Add(new ReferenceTreeNode
                {
                    Name = $"L{line + 1}:{col + 1}",
                    FullName = $"{span.Path}:{line + 1}:{col + 1}",
                    Kind = SymbolNodeKind.Reference,
                    FilePath = span.Path,
                    LineNumber = line + 1,
                    Column = col + 1,
                    ReferenceKind = ReferenceKind.Other,
                    LineText = lineText,
                    ContainingSymbol = containingSymbolDisplay,
                    ContainingNamespace = containingNs,
                    ContainingTypeName = containingTypeName,
                    IsChildrenLoaded = true,
                });
            }
        }

        fileGroupNode.IsChildrenLoaded = true;
        fileGroupNode.HasUnloadedChildren = false;

        return fileGroupNode.Children.OfType<ReferenceTreeNode>().ToList().AsReadOnly();
    }

    // ──────────────────────────── Display helpers ─────────────────────────────

    private static string FormatSymbolDisplayName(ISymbol? symbol)
    {
        if (symbol is null) return string.Empty;
        if (symbol is IMethodSymbol method)
        {
            var paramTypes = method.Parameters.Select(
                p => p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
            var name = method.MethodKind == MethodKind.Constructor
                ? method.ContainingType?.Name ?? method.Name
                : method.Name;
            return $"{name}({string.Join(", ", paramTypes)})";
        }
        return symbol.Name;
    }

    private static string GetContainingNamespaceName(ISymbol? symbol)
    {
        var ns = symbol?.ContainingNamespace;
        if (ns is null || ns.IsGlobalNamespace)
            return string.Empty;
        return ns.ToDisplayString();
    }

    private static SymbolNodeKind MapSymbolKind(ISymbol symbol) => symbol switch
    {
        INamespaceSymbol => SymbolNodeKind.Namespace,
        INamedTypeSymbol t => t.TypeKind switch
        {
            TypeKind.Class => SymbolNodeKind.Class,
            TypeKind.Interface => SymbolNodeKind.Interface,
            TypeKind.Struct => SymbolNodeKind.Struct,
            TypeKind.Enum => SymbolNodeKind.Enum,
            TypeKind.Delegate => SymbolNodeKind.Delegate,
            _ => SymbolNodeKind.Unknown,
        },
        IMethodSymbol { MethodKind: MethodKind.Constructor } => SymbolNodeKind.Constructor,
        IMethodSymbol => SymbolNodeKind.Method,
        IPropertySymbol => SymbolNodeKind.Property,
        IFieldSymbol => SymbolNodeKind.Field,
        IEventSymbol => SymbolNodeKind.Event,
        IParameterSymbol => SymbolNodeKind.Parameter,
        ILocalSymbol => SymbolNodeKind.LocalVariable,
        _ => SymbolNodeKind.Unknown,
    };
}
