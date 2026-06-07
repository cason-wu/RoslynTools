using Microsoft.CodeAnalysis;

namespace RoslynLib.Domain.Models;

/// <summary>
/// Represents an entry in the project browser — either a type or a namespace grouping.
/// </summary>
public sealed class ProjectNode
{
    private readonly Lazy<string> _fullName;
    private readonly Lazy<SymbolNodeKind> _kind;
    private readonly Lazy<string> _namespace;
    private readonly Lazy<string> _sourceFileName;

    public ProjectNode(INamedTypeSymbol symbol)
    {
        Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
        _fullName = new Lazy<string>(() => Symbol.ToDisplayString());
        _kind = new Lazy<SymbolNodeKind>(() => MapTypeKind(Symbol.TypeKind));
        _namespace = new Lazy<string>(() => Symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty);
        _sourceFileName = new Lazy<string>(ResolveSourceFileName);
    }

    public INamedTypeSymbol Symbol { get; }
    public string Name => Symbol.Name;
    public string FullName => _fullName.Value;
    public SymbolNodeKind Kind => _kind.Value;
    public string Namespace => _namespace.Value;
    public string SourceFileName => _sourceFileName.Value;

    private string ResolveSourceFileName()
    {
        return Symbol.Locations
            .Where(l => l.IsInSource)
            .Select(l => l.SourceTree?.FilePath)
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(Path.GetFileName)
            .FirstOrDefault() ?? string.Empty;
    }

    private static SymbolNodeKind MapTypeKind(TypeKind typeKind) => typeKind switch
    {
        TypeKind.Class => SymbolNodeKind.Class,
        TypeKind.Interface => SymbolNodeKind.Interface,
        TypeKind.Struct => SymbolNodeKind.Struct,
        TypeKind.Enum => SymbolNodeKind.Enum,
        TypeKind.Delegate => SymbolNodeKind.Delegate,
        _ => SymbolNodeKind.Unknown,
    };
}
