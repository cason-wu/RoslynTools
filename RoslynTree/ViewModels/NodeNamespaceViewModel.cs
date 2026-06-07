using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.VisualStudio.Imaging.Interop;
using System.Collections.ObjectModel;
using RoslynTree.Infrastructure;
using RoslynLib.Domain.Models;
using RoslynLib.Infrastructure;

namespace RoslynTree.ViewModels;

/// <summary>
/// Represents a namespace grouping node in the solution browser tree.
/// </summary>
public partial class NodeNamespaceViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isExpanded;

    public string Name { get; }
    public ImageMoniker Icon => VisualStudioSymbolIconMap.GetIcon(SymbolNodeKind.Namespace);
    public ObservableCollection<NodeTypeViewModel> Types { get; } = [];

    public NodeNamespaceViewModel(string name) => Name = name;
}