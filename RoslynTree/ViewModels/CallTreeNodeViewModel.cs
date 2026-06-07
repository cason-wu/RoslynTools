using RoslynLib.Domain.Models;

namespace RoslynTree.ViewModels;

/// <summary>
/// ViewModel for a single node in the Call Tree.
/// Wraps a <see cref="CallTreeNode"/> and exposes call-specific properties.
/// </summary>
public sealed partial class CallTreeNodeViewModel : SymbolTreeNodeViewModel
{
    private readonly CallTreeNode _callModel;
    private readonly Lazy<string> _metadataText;

    public CallTreeNode CallTreeModel => _callModel;
    public string ContainingType => _callModel.ContainingType;
    public string SourceDisplay => _callModel.SourceDisplay;
    public string ProjectName => _callModel.ProjectName;
    public string Accessibility => _callModel.Accessibility;
    public string MetadataText => _metadataText.Value;

    public override string Name => _callModel.DisplayName;

    public CallTreeNodeViewModel(
        CallTreeNode model,
        Func<SymbolTreeNodeViewModel, CancellationToken, Task<IReadOnlyList<RoslynTreeNode>>>? childrenLoader,
        SymbolTreeNodeViewModel? parent = null)
        : base(model, childrenLoader, parent)
    {
        _callModel = model;
        _metadataText = new Lazy<string>(BuildMetadataText);
    }

    protected override SymbolTreeNodeViewModel CreateChildViewModel(RoslynTreeNode child, SymbolTreeNodeViewModel parent)
        => new CallTreeNodeViewModel((CallTreeNode)child, _childrenLoader, parent);

    private string BuildMetadataText()
    {
        var parts = new[]
        {
            string.IsNullOrWhiteSpace(_callModel.ContainingType) ? null : _callModel.ContainingType,
            string.IsNullOrWhiteSpace(_callModel.ProjectName) ? null : _callModel.ProjectName,
            string.IsNullOrWhiteSpace(_callModel.Accessibility) ? null : _callModel.Accessibility,
        };

        return string.Join(" • ", parts.Where(static value => !string.IsNullOrWhiteSpace(value))!);
    }
}
