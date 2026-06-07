using RoslynLib.Domain.Models;

namespace RoslynTree.ViewModels;

/// <summary>
/// ViewModel for a single node in the Reference Tree.
/// Wraps a <see cref="ReferenceTreeNode"/> and exposes reference-specific properties.
/// </summary>
public sealed partial class ReferenceTreeNodeViewModel : SymbolTreeNodeViewModel
{
    private readonly ReferenceTreeNode _refModel;

    public ReferenceTreeNode RefTreeModel => _refModel;
    public string FilePath => _refModel.FilePath;
    public int LineNumber => _refModel.LineNumber;
    public string LineText => _refModel.LineText;
    public bool IsFileGroup => _refModel.IsFileGroup;

    /// <summary>
    /// Formatted context where this reference lives: "Namespace.ClassName.Method(params)".
    /// Empty for root and file-group nodes.
    /// </summary>
    public string ContainingContext
    {
        get
        {
            if (_refModel.IsFileGroup) return string.Empty;
            var parts = new[] { _refModel.ContainingNamespace, _refModel.ContainingTypeName, _refModel.ContainingSymbol }
                .Where(s => !string.IsNullOrEmpty(s));
            return string.Join(".", parts);
        }
    }

    public ReferenceTreeNodeViewModel(
        ReferenceTreeNode model,
        Func<SymbolTreeNodeViewModel, CancellationToken, Task<IReadOnlyList<RoslynTreeNode>>>? childrenLoader,
        SymbolTreeNodeViewModel? parent = null)
        : base(model, childrenLoader, parent)
    {
        _refModel = model;
    }

    protected override SymbolTreeNodeViewModel CreateChildViewModel(RoslynTreeNode child, SymbolTreeNodeViewModel parent)
        => new ReferenceTreeNodeViewModel((ReferenceTreeNode)child, _childrenLoader, parent);
}
