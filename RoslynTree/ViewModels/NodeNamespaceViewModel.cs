using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.VisualStudio.Imaging.Interop;
using System.Collections.ObjectModel;
using RoslynTree.Infrastructure;
using RoslynLib.Domain.Models;

namespace RoslynTree.ViewModels;

/// <summary>
/// Represents a namespace grouping node in the solution browser tree.
/// </summary>
public partial class NodeNamespaceViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isExpanded;

    public ProjectNamespaceNode Node { get; }
    public string Name => Node.Name;
    public string SummaryText => Node.SummaryText;
    public ImageMoniker Icon => VisualStudioSymbolIconMap.GetIcon(SymbolNodeKind.Namespace);
    public ObservableCollection<NodeTypeViewModel> Types { get; } = [];

    public NodeNamespaceViewModel(ProjectNamespaceNode node)
    {
        Node = node;

        foreach (var type in node.Types)
            Types.Add(new NodeTypeViewModel(type));
    }
}