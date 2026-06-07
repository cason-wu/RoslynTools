using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.Imaging.Interop;
using System.IO;
using System.Linq;
using RoslynTree.Infrastructure;
using RoslynLib.Domain.Models;
using RoslynLib.Infrastructure;

namespace RoslynTree.ViewModels;

/// <summary>
/// Represents a single member (method / property / field / event / ctor) inside a type.
/// </summary>
public sealed class NodeMemberViewModel
{
    public string Name { get; }
    public SymbolNodeKind Kind { get; }
    public ImageMoniker Icon { get; }
    public string TypeName { get; }
    public string Accessibility { get; }
    public bool IsStatic { get; }
    public ISymbol Symbol { get; }
    public string FileName { get; }
    public int LineNumber { get; }
    public string TipText => string.IsNullOrWhiteSpace(FileName)
        ? Symbol.ToDisplayString()
        : LineNumber > 0
            ? $"{Symbol.ToDisplayString()}{Environment.NewLine}Source: {FileName}:{LineNumber}"
            : $"{Symbol.ToDisplayString()}{Environment.NewLine}Source: {FileName}";

    public NodeMemberViewModel(ISymbol symbol)
    {
        Symbol = symbol;
        Name = BuildDisplayName(symbol);
        Kind = VisualStudioSymbolIconMap.GetKind(symbol);
        var memberAccessibility = symbol.DeclaredAccessibility;
        Icon = VisualStudioSymbolIconMap.GetIcon(Kind, memberAccessibility);
        IsStatic = symbol.IsStatic;
        Accessibility = memberAccessibility.ToString();
        var sourceLocation = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        var sourcePath = sourceLocation?.SourceTree?.FilePath;
        FileName = string.IsNullOrEmpty(sourcePath)
            ? string.Empty
            : Path.GetFileName(sourcePath);
        LineNumber = sourceLocation == null
            ? 0
            : sourceLocation.GetLineSpan().StartLinePosition.Line + 1;

        TypeName = symbol switch
        {
            IMethodSymbol { MethodKind: MethodKind.Constructor } c =>
                c.ContainingType.Name,
            IMethodSymbol m =>
                m.ReturnType.ToDisplayString(),
            INamedTypeSymbol { TypeKind: TypeKind.Delegate } d =>
                d.DelegateInvokeMethod?.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? string.Empty,
            IPropertySymbol p =>
                p.Type.ToDisplayString(),
            IFieldSymbol f =>
                f.Type.ToDisplayString(),
            IEventSymbol e =>
                e.Type.ToDisplayString(),
            _ =>
                string.Empty,
        };
    }

    private static string BuildDisplayName(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol { MethodKind: MethodKind.Constructor } ctor =>
                BuildMethodLikeDisplayName(ctor, ctor.ContainingType.Name),
            IMethodSymbol method =>
                BuildMethodLikeDisplayName(method, method.Name),
            INamedTypeSymbol { TypeKind: TypeKind.Delegate } del =>
                BuildDelegateDisplayName(del),
            _ => symbol.Name,
        };
    }

    private static string BuildMethodLikeDisplayName(IMethodSymbol method, string baseName)
    {
        var genericSuffix = method.TypeParameters.Length == 0
            ? string.Empty
            : $"<{string.Join(", ", method.TypeParameters.Select(tp => tp.Name))}>";

        var parameters = string.Join(", ", method.Parameters
            .Select(FormatParameter));

        return $"{baseName}{genericSuffix}({parameters})";
    }

    private static string BuildDelegateDisplayName(INamedTypeSymbol delegateType)
    {
        var invoke = delegateType.DelegateInvokeMethod;
        if (invoke is null)
            return delegateType.Name;

        var genericSuffix = delegateType.TypeParameters.Length == 0
            ? string.Empty
            : $"<{string.Join(", ", delegateType.TypeParameters.Select(tp => tp.Name))}>";

        var parameters = string.Join(", ", invoke.Parameters.Select(FormatParameter));
        return $"{delegateType.Name}{genericSuffix}({parameters})";
    }

    private static string FormatParameter(IParameterSymbol parameter)
    {
        var modifier = parameter.RefKind switch
        {
            RefKind.Ref => "ref ",
            RefKind.Out => "out ",
            RefKind.In => "in ",
            _ => string.Empty,
        };

        var paramsPrefix = parameter.IsParams ? "params " : string.Empty;
        var typeName = parameter.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        return $"{paramsPrefix}{modifier}{typeName}";
    }
}