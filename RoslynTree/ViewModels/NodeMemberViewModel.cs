using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.Imaging.Interop;
using RoslynTree.Infrastructure;
using RoslynLib.Domain.Models;

namespace RoslynTree.ViewModels;

/// <summary>
/// Represents a single member (method / property / field / event / ctor) inside a type.
/// </summary>
public sealed class NodeMemberViewModel
{
    public ProjectMemberNode Node { get; }
    public string Name => Node.Name;
    public SymbolNodeKind Kind => Node.Kind;
    public ImageMoniker Icon { get; }
    public string TypeName => Node.TypeName;
    public string Accessibility => Node.Accessibility;
    public bool IsStatic => Node.IsStatic;
    public ISymbol Symbol => Node.Symbol;
    public string FileName => Node.FileName;
    public int LineNumber => Node.LineNumber;
    public string TipText => Node.TipText;

    public NodeMemberViewModel(ProjectMemberNode node)
    {
        Node = node;

        var memberAccessibility = node.Symbol.DeclaredAccessibility;
        Icon = VisualStudioSymbolIconMap.GetIcon(Kind, memberAccessibility);
    }
}