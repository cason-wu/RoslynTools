using RoslynLib.Domain.Models;

namespace RoslynTree.ViewModels;

/// <summary>
/// ViewModel for a single node in the Call Tree.
/// Wraps a <see cref="CallTreeNode"/> and exposes call-specific properties.
/// </summary>
public sealed partial class CallTreeNodeViewModel : SymbolTreeNodeViewModel
{
    private readonly CallTreeNode _callModel;

    public CallTreeNode CallTreeModel => _callModel;
    public string ContainingType => _callModel.ContainingType;

    public override string Name => _callModel.DisplayName;

    public CallTreeNodeViewModel(
        CallTreeNode model,
        Func<SymbolTreeNodeViewModel, CancellationToken, Task<IReadOnlyList<RoslynTreeNode>>>? childrenLoader,
        SymbolTreeNodeViewModel? parent = null)
        : base(model, childrenLoader, parent)
    {
        _callModel = model;
    }

    protected override SymbolTreeNodeViewModel CreateChildViewModel(RoslynTreeNode child, SymbolTreeNodeViewModel parent)
        => new CallTreeNodeViewModel((CallTreeNode)child, _childrenLoader, parent);
}
