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
    private readonly Lazy<(string FilePath, string FileName, int LineNumber)> _source;
    private readonly Lazy<string> _displayName;
    private readonly Lazy<IReadOnlyList<ProjectMemberNode>> _members;
    private readonly Lazy<string> _sourceDisplay;

    public ProjectNode(
        INamedTypeSymbol symbol,
        string projectName,
        string assemblyName)
    {
        Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
        ProjectName = projectName ?? throw new ArgumentNullException(nameof(projectName));
        AssemblyName = assemblyName ?? throw new ArgumentNullException(nameof(assemblyName));
        _fullName = new Lazy<string>(() => Symbol.ToDisplayString());
        _kind = new Lazy<SymbolNodeKind>(() => RoslynSymbolHelper.GetKind(Symbol.TypeKind));
        _namespace = new Lazy<string>(() => Symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty);
        _source = new Lazy<(string FilePath, string FileName, int LineNumber)>(() => RoslynSymbolHelper.GetSourceLocation(Symbol));
        _displayName = new Lazy<string>(() => RoslynSymbolHelper.BuildTypeDisplayName(Symbol));
        _members = new Lazy<IReadOnlyList<ProjectMemberNode>>(BuildMembers);
        _sourceDisplay = new Lazy<string>(() => RoslynSymbolHelper.BuildSourceText(FilePath, LineNumber));
    }

    public INamedTypeSymbol Symbol { get; }
    public string ProjectName { get; }
    public string AssemblyName { get; }
    public string Name => Symbol.Name;
    public string FullName => _fullName.Value;
    public SymbolNodeKind Kind => _kind.Value;
    public string Namespace => _namespace.Value;
    public string FilePath => _source.Value.FilePath;
    public string SourceFileName => _source.Value.FileName;
    public int LineNumber => _source.Value.LineNumber;
    public string DisplayName => _displayName.Value;
    public IReadOnlyList<ProjectMemberNode> Members => _members.Value;
    public string SourceDisplay => _sourceDisplay.Value;

    private IReadOnlyList<ProjectMemberNode> BuildMembers()
    {
        return Symbol.GetMembers()
            .Where(static member => !member.IsImplicitlyDeclared)
            .Select(member => new ProjectMemberNode(member, ProjectName, AssemblyName))
            .ToList()
            .AsReadOnly();
    }
}
