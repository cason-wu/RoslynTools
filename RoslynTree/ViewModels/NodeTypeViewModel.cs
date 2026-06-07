using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.Imaging.Interop;
using System.Collections.ObjectModel;
using RoslynTree.Infrastructure;
using RoslynLib.Domain.Models;

namespace RoslynTree.ViewModels;

/// <summary>
/// Represents a single type entry in the project browser.
/// </summary>
public partial class NodeTypeViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    public ProjectNode Node { get; }
    public string Name => Node.DisplayName;
    public string FullName => Node.FullName;
    public SymbolNodeKind Kind => Node.Kind;
    public string SourceFileName => Node.SourceFileName;
    public string ProjectName => Node.ProjectName;
    public string MetadataText => Node.MetadataText;
    public string TipText => Node.TipText;

    public ImageMoniker Icon { get; }

    public ObservableCollection<NodeMemberViewModel> Members { get; } = [];
    private readonly INamedTypeSymbol _typeSymbol;

    public NodeTypeViewModel(ProjectNode node)
    {
        this.Node = node;
        _typeSymbol = node.Symbol;

        foreach (var member in node.Members)
            Members.Add(new NodeMemberViewModel(member));

        Icon = VisualStudioSymbolIconMap.GetIcon(Kind, _typeSymbol.DeclaredAccessibility);
    }
}