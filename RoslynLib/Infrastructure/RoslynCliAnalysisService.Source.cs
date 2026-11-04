using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using RoslynLib.Domain.Models;

namespace RoslynLib.Infrastructure;

// Source-oriented queries: declaration source text, document outline, and outgoing calls.
public sealed partial class RoslynCliAnalysisService
{
    private static readonly SymbolDisplayFormat SignatureFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters
            | SymbolDisplayGenericsOptions.IncludeTypeConstraints
            | SymbolDisplayGenericsOptions.IncludeVariance,
        memberOptions: SymbolDisplayMemberOptions.IncludeAccessibility
            | SymbolDisplayMemberOptions.IncludeModifiers
            | SymbolDisplayMemberOptions.IncludeType
            | SymbolDisplayMemberOptions.IncludeContainingType
            | SymbolDisplayMemberOptions.IncludeParameters
            | SymbolDisplayMemberOptions.IncludeRef
            | SymbolDisplayMemberOptions.IncludeConstantValue,
        delegateStyle: SymbolDisplayDelegateStyle.NameAndSignature,
        extensionMethodStyle: SymbolDisplayExtensionMethodStyle.StaticMethod,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType
            | SymbolDisplayParameterOptions.IncludeName
            | SymbolDisplayParameterOptions.IncludeDefaultValue
            | SymbolDisplayParameterOptions.IncludeParamsRefOut
            | SymbolDisplayParameterOptions.IncludeExtensionThis,
        propertyStyle: SymbolDisplayPropertyStyle.ShowReadWriteDescriptor,
        kindOptions: SymbolDisplayKindOptions.IncludeTypeKeyword
            | SymbolDisplayKindOptions.IncludeMemberKeyword
            | SymbolDisplayKindOptions.IncludeNamespaceKeyword,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes
            | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
            | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public async Task<SymbolSourceResult> GetSymbolSourceAsync(
        LoadedWorkspaceTarget workspaceTarget,
        RoslynCliQueryOptions options,
        int? maxLines,
        CancellationToken cancellationToken = default)
    {
        var symbol = await ResolveSymbolAsync(workspaceTarget, options, cancellationToken).ConfigureAwait(false);
        if (symbol.DeclaringSyntaxReferences.Length == 0)
            throw new InvalidOperationException($"'{symbol.ToDisplayString()}' has no source declaration in the loaded target (it is defined in '{symbol.ContainingAssembly?.Name}').");

        var declarations = new List<SymbolSourceView>();
        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            var node = await reference.GetSyntaxAsync(cancellationToken).ConfigureAwait(false);
            var tree = node.SyntaxTree;
            if (RoslynSymbolHelper.IsPathExcluded(tree.FilePath, options.Excludes))
                continue;

            var text = await tree.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var span = GetDeclarationSpan(node);
            var startLine = text.Lines.GetLineFromPosition(span.Start).LineNumber;
            var endLine = text.Lines.GetLineFromPosition(span.End).LineNumber;

            var lastIncludedLine = maxLines is int limit && limit > 0
                ? Math.Min(endLine, startLine + limit - 1)
                : endLine;

            var source = text.ToString(TextSpan.FromBounds(
                text.Lines[startLine].Start,
                text.Lines[lastIncludedLine].End));

            declarations.Add(new SymbolSourceView(
                FormatOutputPath(tree.FilePath, workspaceTarget),
                startLine + 1,
                endLine + 1,
                source,
                lastIncludedLine < endLine));
        }

        var ordered = declarations
            .OrderBy(static item => item.File, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.StartLine)
            .ToList()
            .AsReadOnly();

        return new SymbolSourceResult(
            "sym source",
            options.Symbol ?? RoslynSymbolHelper.FormatSymbolDisplayName(symbol),
            symbol.ToDisplayString(),
            RoslynSymbolHelper.GetKind(symbol).ToString(),
            Limit(ordered, options),
            ordered.Count,
            IsTruncated(ordered, options));
    }

    public async Task<DocumentSymbolsResult> GetDocumentSymbolsAsync(
        LoadedWorkspaceTarget workspaceTarget,
        RoslynCliQueryOptions options,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.File))
            throw new InvalidOperationException("file is required (CLI: --file).");

        var document = FindDocuments(workspaceTarget, options.File).FirstOrDefault()
            ?? throw new InvalidOperationException($"Unable to find document '{options.File}' in the current target. Use an absolute path or a path relative to the target directory.");

        var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        if (syntaxRoot is null || semanticModel is null)
            throw new InvalidOperationException($"Unable to create semantic model for '{options.File}'.");

        var entries = new List<(ISymbol Symbol, TextSpan Span)>();
        var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        foreach (var node in syntaxRoot.DescendantNodes(static node => node is not BlockSyntax and not ArrowExpressionClauseSyntax))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var symbol = semanticModel.GetDeclaredSymbol(node, cancellationToken);
            if (symbol is null || !IsOutlineSymbol(symbol) || !seen.Add(symbol))
                continue;

            entries.Add((symbol, GetDeclarationSpan(node)));
        }

        // Depth comes from span containment, which matches what the reader sees in the file.
        var stack = new Stack<TextSpan>();
        var views = new List<DocumentSymbolView>();
        foreach (var (symbol, span) in entries.OrderBy(static entry => entry.Span.Start).ThenByDescending(static entry => entry.Span.Length))
        {
            while (stack.Count > 0 && !stack.Peek().Contains(span))
                stack.Pop();

            var depth = stack.Count;
            stack.Push(span);

            if (!RoslynSymbolHelper.MatchesKind(symbol, options.Filter) || !RoslynSymbolHelper.MatchesVisibility(symbol, options.Visibility))
                continue;

            views.Add(new DocumentSymbolView(
                symbol is INamespaceSymbol ? symbol.ToDisplayString() : RoslynSymbolHelper.FormatSymbolDisplayName(symbol),
                symbol.ToDisplayString(),
                RoslynSymbolHelper.GetKind(symbol).ToString(),
                symbol.DeclaredAccessibility.ToString(),
                text.Lines.GetLineFromPosition(span.Start).LineNumber + 1,
                text.Lines.GetLineFromPosition(span.End).LineNumber + 1,
                depth));
        }

        var ordered = views.AsReadOnly();
        return new DocumentSymbolsResult(
            "sym outline",
            FormatOutputPath(document.FilePath ?? options.File, workspaceTarget),
            Limit(ordered, options),
            ordered.Count,
            IsTruncated(ordered, options));
    }

    public async Task<CalleesResult> FindCalleesAsync(
        LoadedWorkspaceTarget workspaceTarget,
        RoslynCliQueryOptions options,
        bool sourceOnly,
        CancellationToken cancellationToken = default)
    {
        var symbol = await ResolveSymbolAsync(workspaceTarget, options, cancellationToken).ConfigureAwait(false);
        if (symbol is not (IMethodSymbol or IPropertySymbol or IEventSymbol))
            throw new InvalidOperationException("refs callees requires a method, constructor, property, or event symbol.");

        if (symbol.DeclaringSyntaxReferences.Length == 0)
            throw new InvalidOperationException($"'{symbol.ToDisplayString()}' has no source declaration in the loaded target.");

        var callees = new Dictionary<ISymbol, List<int>>(SymbolEqualityComparer.Default);

        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            var node = await reference.GetSyntaxAsync(cancellationToken).ConfigureAwait(false);
            var document = workspaceTarget.Solution.GetDocument(node.SyntaxTree)
                ?? throw new InvalidOperationException($"Unable to find the document declaring '{symbol.ToDisplayString()}'.");

            if (!string.Equals(document.Project.Language, LanguageNames.CSharp, StringComparison.Ordinal))
                throw new InvalidOperationException("refs callees currently supports C# only.");

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            if (semanticModel is null)
                continue;

            foreach (var callNode in node.DescendantNodesAndSelf().Where(static item => item is
                InvocationExpressionSyntax or
                BaseObjectCreationExpressionSyntax or
                ConstructorInitializerSyntax or
                PrimaryConstructorBaseTypeSyntax))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var symbolInfo = semanticModel.GetSymbolInfo(callNode, cancellationToken);
                var callee = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
                if (callee is not IMethodSymbol method)
                    continue;

                var target = (ISymbol)(method.ReducedFrom ?? method).OriginalDefinition;
                var line = text.Lines.GetLineFromPosition(callNode.SpanStart).LineNumber + 1;

                if (!callees.TryGetValue(target, out var lines))
                    callees[target] = lines = [];

                if (!lines.Contains(line))
                    lines.Add(line);
            }
        }

        var results = callees
            .Select(item =>
            {
                var location = RoslynSymbolHelper.GetSourceLocation(item.Key);
                var isSource = !string.IsNullOrWhiteSpace(location.FilePath);
                return new
                {
                    Location = location,
                    View = new CalleeView(
                        item.Key.ToDisplayString(),
                        RoslynSymbolHelper.GetKind(item.Key).ToString(),
                        item.Key.ContainingType?.ToDisplayString() ?? string.Empty,
                        item.Key.ContainingAssembly?.Name ?? string.Empty,
                        isSource,
                        isSource ? FormatOutputPath(location.FilePath, workspaceTarget) : string.Empty,
                        location.LineNumber,
                        item.Value.Order().ToList().AsReadOnly()),
                };
            })
            .Where(item => !sourceOnly || item.View.IsSource)
            .Where(item => !RoslynSymbolHelper.IsPathExcluded(item.Location.FilePath, options.Excludes))
            .Select(static item => item.View)
            .OrderBy(static item => item.CallLines[0])
            .ThenBy(static item => item.Symbol, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();

        return new CalleesResult(
            "refs callees",
            options.Symbol ?? RoslynSymbolHelper.FormatSymbolDisplayName(symbol),
            RoslynSymbolHelper.GetKind(symbol).ToString(),
            symbol.ToDisplayString(),
            Limit(results, options),
            results.Count,
            IsTruncated(results, options));
    }

    private static bool IsOutlineSymbol(ISymbol symbol)
    {
        return symbol switch
        {
            INamedTypeSymbol type => !type.IsAnonymousType && !type.IsTupleType,
            INamespaceSymbol or IPropertySymbol or IEventSymbol => true,
            // Tuple element names in signatures also surface as fields; skip them.
            IFieldSymbol field => !field.IsImplicitlyDeclared && field.ContainingType is not { IsTupleType: true },
            IMethodSymbol method => method.MethodKind is MethodKind.Ordinary
                or MethodKind.Constructor
                or MethodKind.StaticConstructor
                or MethodKind.Destructor
                or MethodKind.UserDefinedOperator
                or MethodKind.Conversion
                or MethodKind.ExplicitInterfaceImplementation,
            _ => false,
        };
    }

    // Fields and events declared through a variable declarator are expanded to the whole declaration,
    // and leading XML documentation comments are included so the snippet reads like the file.
    private static TextSpan GetDeclarationSpan(SyntaxNode node)
    {
        var declaration = node is VariableDeclaratorSyntax { Parent.Parent: BaseFieldDeclarationSyntax fieldDeclaration }
            ? fieldDeclaration
            : node;

        var start = declaration.SpanStart;
        foreach (var trivia in declaration.GetLeadingTrivia())
        {
            if (trivia.HasStructure && trivia.GetStructure() is DocumentationCommentTriviaSyntax)
            {
                start = Math.Min(start, trivia.SpanStart);
                break;
            }
        }

        return TextSpan.FromBounds(start, declaration.Span.End);
    }

    private static async Task<IReadOnlyList<SourceRangeView>> GetDeclarationRangesAsync(
        ISymbol symbol,
        LoadedWorkspaceTarget workspaceTarget,
        CancellationToken cancellationToken)
    {
        var ranges = new List<SourceRangeView>();
        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            var node = await reference.GetSyntaxAsync(cancellationToken).ConfigureAwait(false);
            var text = await node.SyntaxTree.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var span = GetDeclarationSpan(node);
            ranges.Add(new SourceRangeView(
                FormatOutputPath(node.SyntaxTree.FilePath, workspaceTarget),
                text.Lines.GetLineFromPosition(span.Start).LineNumber + 1,
                text.Lines.GetLineFromPosition(span.End).LineNumber + 1));
        }

        return ranges
            .OrderBy(static item => item.File, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.StartLine)
            .ToList()
            .AsReadOnly();
    }

    private static string GetReturnTypeName(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor or MethodKind.Destructor } => string.Empty,
            IMethodSymbol method => method.ReturnType.ToDisplayString(),
            IPropertySymbol property => property.Type.ToDisplayString(),
            IFieldSymbol field => field.Type.ToDisplayString(),
            IEventSymbol @event => @event.Type.ToDisplayString(),
            INamedTypeSymbol { TypeKind: TypeKind.Delegate } type => type.DelegateInvokeMethod?.ReturnType.ToDisplayString() ?? string.Empty,
            _ => string.Empty,
        };
    }

    private static string ExtractDocumentationSummary(ISymbol symbol, CancellationToken cancellationToken)
    {
        var xml = symbol.GetDocumentationCommentXml(expandIncludes: true, cancellationToken: cancellationToken);
        if (string.IsNullOrWhiteSpace(xml))
            return string.Empty;

        try
        {
            var summary = XElement.Parse(xml).Element("summary");
            if (summary is null)
                return string.Empty;

            var builder = new StringBuilder();
            AppendDocumentationText(builder, summary);
            return Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
        }
        catch (System.Xml.XmlException)
        {
            return string.Empty;
        }
    }

    private static void AppendDocumentationText(StringBuilder builder, XElement element)
    {
        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XText text:
                    builder.Append(text.Value);
                    break;

                case XElement { Name.LocalName: "see" or "seealso" } reference:
                    var target = reference.Attribute("cref")?.Value
                        ?? reference.Attribute("langword")?.Value
                        ?? reference.Attribute("href")?.Value
                        ?? string.Empty;
                    builder.Append(reference.IsEmpty ? StripDocumentationIdPrefix(target) : reference.Value);
                    break;

                case XElement { Name.LocalName: "paramref" or "typeparamref" } parameterReference:
                    builder.Append(parameterReference.Attribute("name")?.Value);
                    break;

                case XElement child:
                    AppendDocumentationText(builder, child);
                    break;
            }
        }
    }

    // Documentation ids look like 'T:Namespace.Type' or 'M:Namespace.Type.Method(System.Int32)'.
    private static string StripDocumentationIdPrefix(string value)
        => value.Length > 2 && value[1] == ':'
            ? value[2..]
            : value;
}
