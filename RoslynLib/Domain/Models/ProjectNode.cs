using Microsoft.CodeAnalysis;
using RoslynLib.Infrastructure;

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
    private readonly Lazy<string> _displayName;
    private readonly Lazy<IReadOnlyList<ProjectMemberNode>> _members;
    private readonly Lazy<string> _tipText;

    public ProjectNode(INamedTypeSymbol symbol)
    {
        Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
        _fullName = new Lazy<string>(() => Symbol.ToDisplayString());
        _kind = new Lazy<SymbolNodeKind>(() => RoslynSymbolHelper.GetKind(Symbol.TypeKind));
        _namespace = new Lazy<string>(() => Symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty);
        _sourceFileName = new Lazy<string>(ResolveSourceFileName);
        _displayName = new Lazy<string>(() => RoslynSymbolHelper.BuildTypeDisplayName(Symbol));
        _members = new Lazy<IReadOnlyList<ProjectMemberNode>>(BuildMembers);
        _tipText = new Lazy<string>(BuildTipText);
    }

    public INamedTypeSymbol Symbol { get; }
    public string Name => Symbol.Name;
    public string FullName => _fullName.Value;
    public SymbolNodeKind Kind => _kind.Value;
    public string Namespace => _namespace.Value;
    public string SourceFileName => _sourceFileName.Value;
    public string DisplayName => _displayName.Value;
    public IReadOnlyList<ProjectMemberNode> Members => _members.Value;
    public string TipText => _tipText.Value;

    private string ResolveSourceFileName()
    {
        return Symbol.Locations
            .Where(l => l.IsInSource)
            .Select(l => l.SourceTree?.FilePath)
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(Path.GetFileName)
            .FirstOrDefault() ?? string.Empty;
    }

    private IReadOnlyList<ProjectMemberNode> BuildMembers()
    {
        return Symbol.GetMembers()
            .Where(static member => !member.IsImplicitlyDeclared)
            .Select(static member => new ProjectMemberNode(member))
            .ToList()
            .AsReadOnly();
    }

    private string BuildTipText()
    {
        if (string.IsNullOrWhiteSpace(SourceFileName))
            return FullName;

        return $"{FullName}{Environment.NewLine}Source: {SourceFileName}";
    }
}
