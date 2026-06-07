using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using RoslynLib.Domain.Models;

namespace RoslynLib.Infrastructure;

public static class RoslynSymbolHelper
{
    public static SymbolNodeKind GetKind(ISymbol symbol) => symbol switch
    {
        INamespaceSymbol => SymbolNodeKind.Namespace,
        INamedTypeSymbol type => GetKind(type.TypeKind),
        IMethodSymbol { MethodKind: MethodKind.Constructor } => SymbolNodeKind.Constructor,
        IMethodSymbol => SymbolNodeKind.Method,
        IPropertySymbol => SymbolNodeKind.Property,
        IFieldSymbol => SymbolNodeKind.Field,
        IEventSymbol => SymbolNodeKind.Event,
        IParameterSymbol => SymbolNodeKind.Parameter,
        ILocalSymbol => SymbolNodeKind.LocalVariable,
        _ => SymbolNodeKind.Unknown,
    };

    public static SymbolNodeKind GetKind(TypeKind typeKind) => typeKind switch
    {
        TypeKind.Class => SymbolNodeKind.Class,
        TypeKind.Interface => SymbolNodeKind.Interface,
        TypeKind.Struct => SymbolNodeKind.Struct,
        TypeKind.Enum => SymbolNodeKind.Enum,
        TypeKind.Delegate => SymbolNodeKind.Delegate,
        _ => SymbolNodeKind.Unknown,
    };

    public static string FormatSymbolDisplayName(ISymbol? symbol)
    {
        if (symbol is null)
            return string.Empty;

        return symbol switch
        {
            INamedTypeSymbol type => BuildTypeDisplayName(type),
            _ => BuildMemberDisplayName(symbol),
        };
    }

    public static string BuildTypeDisplayName(INamedTypeSymbol type)
    {
        if (type.TypeKind != TypeKind.Delegate)
            return type.Name;

        var invoke = type.DelegateInvokeMethod;
        if (invoke is null)
            return type.Name;

        var genericSuffix = type.TypeParameters.Length == 0
            ? string.Empty
            : $"<{string.Join(", ", type.TypeParameters.Select(tp => tp.Name))}>";

        var parameters = string.Join(", ", invoke.Parameters.Select(FormatParameter));
        var returnType = invoke.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        return $"{type.Name}{genericSuffix}({parameters}) : {returnType}";
    }

    public static string BuildMemberDisplayName(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol { MethodKind: MethodKind.Constructor } ctor =>
                BuildMethodLikeDisplayName(ctor, ctor.ContainingType.Name),
            IMethodSymbol method =>
                BuildMethodLikeDisplayName(method, method.Name),
            INamedTypeSymbol type when type.TypeKind == TypeKind.Delegate =>
                BuildTypeDisplayName(type),
            _ => symbol.Name,
        };
    }

    public static string BuildMethodLikeDisplayName(IMethodSymbol method, string? baseName = null)
    {
        ArgumentNullException.ThrowIfNull(method);

        var resolvedBaseName = string.IsNullOrWhiteSpace(baseName)
            ? method.MethodKind == MethodKind.Constructor
                ? method.ContainingType.Name
                : method.Name
            : baseName;

        var genericSuffix = method.TypeParameters.Length == 0
            ? string.Empty
            : $"<{string.Join(", ", method.TypeParameters.Select(tp => tp.Name))}>";

        var parameters = string.Join(", ", method.Parameters.Select(FormatParameter));
        return $"{resolvedBaseName}{genericSuffix}({parameters})";
    }

    public static string BuildMemberTypeName(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol { MethodKind: MethodKind.Constructor } ctor => ctor.ContainingType.Name,
            IMethodSymbol method => method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            INamedTypeSymbol type when type.TypeKind == TypeKind.Delegate =>
                type.DelegateInvokeMethod?.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? string.Empty,
            IPropertySymbol property => property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            IFieldSymbol field => field.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            IEventSymbol @event => @event.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            _ => string.Empty,
        };
    }

    public static string FormatParameter(IParameterSymbol parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);

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

    public static string GetContainingNamespaceName(ISymbol? symbol)
    {
        var containingNamespace = symbol?.ContainingNamespace;
        if (containingNamespace is null || containingNamespace.IsGlobalNamespace)
            return string.Empty;

        return containingNamespace.ToDisplayString();
    }

    public static string BuildContainingContext(
        string? containingNamespace,
        string? containingTypeName,
        string? containingSymbol)
    {
        var parts = new[]
        {
            containingNamespace,
            containingTypeName,
            containingSymbol,
        }.Where(static value => !string.IsNullOrWhiteSpace(value));

        return string.Join('.', parts);
    }

    public static (string FilePath, string FileName, int LineNumber) GetSourceLocation(ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(static location => location.IsInSource);
        var filePath = location?.SourceTree?.FilePath ?? string.Empty;
        var fileName = string.IsNullOrWhiteSpace(filePath)
            ? string.Empty
            : Path.GetFileName(filePath);
        var lineNumber = location is null
            ? 0
            : location.GetLineSpan().StartLinePosition.Line + 1;

        return (filePath, fileName, lineNumber);
    }

    public static string BuildSourceText(string filePath, int lineNumber)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return string.Empty;

        var fileName = Path.GetFileName(filePath);
        if (string.IsNullOrWhiteSpace(fileName))
            return string.Empty;

        return lineNumber > 0
            ? $"{fileName}:{lineNumber}"
            : fileName;
    }

    public static bool MatchesKind(ISymbol symbol, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        return filter.Trim().ToLowerInvariant() switch
        {
            "class" => symbol is INamedTypeSymbol { TypeKind: TypeKind.Class },
            "method" => symbol is IMethodSymbol and not IMethodSymbol { MethodKind: MethodKind.Constructor },
            "property" => symbol is IPropertySymbol,
            "event" => symbol is IEventSymbol,
            "field" => symbol is IFieldSymbol,
            "interface" => symbol is INamedTypeSymbol { TypeKind: TypeKind.Interface },
            "struct" => symbol is INamedTypeSymbol { TypeKind: TypeKind.Struct },
            "enum" => symbol is INamedTypeSymbol { TypeKind: TypeKind.Enum },
            "delegate" => symbol is INamedTypeSymbol { TypeKind: TypeKind.Delegate },
            "namespace" => symbol is INamespaceSymbol,
            _ => true,
        };
    }

    public static bool MatchesVisibility(ISymbol symbol, string? visibility)
    {
        if (string.IsNullOrWhiteSpace(visibility) || string.Equals(visibility, "all", StringComparison.OrdinalIgnoreCase))
            return true;

        return visibility.Trim().ToLowerInvariant() switch
        {
            "public" => symbol.DeclaredAccessibility == Accessibility.Public,
            "internal" => symbol.DeclaredAccessibility is Accessibility.Internal or Accessibility.ProtectedAndInternal or Accessibility.ProtectedOrInternal,
            "private" => symbol.DeclaredAccessibility is Accessibility.Private or Accessibility.Protected,
            _ => true,
        };
    }

    public static bool WildcardMatch(string? value, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return true;

        var input = value ?? string.Empty;
        var regexPattern = "^" + Regex.Escape(pattern.Trim())
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return Regex.IsMatch(input, regexPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static bool IsPathExcluded(string path, IEnumerable<string>? patterns)
    {
        if (string.IsNullOrWhiteSpace(path) || patterns is null)
            return false;

        var normalizedPath = path.Replace('\\', '/');

        foreach (var pattern in patterns.Where(static pattern => !string.IsNullOrWhiteSpace(pattern)))
        {
            var normalizedPattern = pattern.Replace('\\', '/');
            if (WildcardMatch(normalizedPath, normalizedPattern) || WildcardMatch(Path.GetFileName(normalizedPath), normalizedPattern))
                return true;
        }

        return false;
    }
}