using RoslynLib.Domain.Models;

namespace RoslynTree.ViewModels;

/// <summary>
/// ViewModel for a single node in the Reference Tree.
/// Wraps a <see cref="ReferenceTreeNode"/> and exposes reference-specific properties.
/// </summary>
public sealed partial class ReferenceTreeNodeViewModel : SymbolTreeNodeViewModel
{
    private readonly ReferenceTreeNode _refModel;
    private readonly Lazy<string> _metadataText;

    public ReferenceTreeNode RefTreeModel => _refModel;
    public string FilePath => _refModel.FilePath;
    public int LineNumber => _refModel.LineNumber;
    public string LineText => _refModel.LineText;
    public bool IsFileGroup => _refModel.IsFileGroup;
    public string ProjectName => _refModel.ProjectName;
    public string LocationDisplay => _refModel.LocationDisplay;
    public string ReferenceKindText => _refModel.ReferenceKind.ToString();
    public string MetadataText => _metadataText.Value;

    /// <summary>
    /// Formatted context where this reference lives: "Namespace.ClassName.Method(params)".
    /// Empty for root and file-group nodes.
    /// </summary>
    public string ContainingContext => _refModel.ContainingContext;

    public ReferenceTreeNodeViewModel(
        ReferenceTreeNode model,
        Func<SymbolTreeNodeViewModel, CancellationToken, Task<IReadOnlyList<RoslynTreeNode>>>? childrenLoader,
        SymbolTreeNodeViewModel? parent = null)
        : base(model, childrenLoader, parent)
    {
        _refModel = model;
        _metadataText = new Lazy<string>(BuildMetadataText);
    }

    protected override SymbolTreeNodeViewModel CreateChildViewModel(RoslynTreeNode child, SymbolTreeNodeViewModel parent)
        => new ReferenceTreeNodeViewModel((ReferenceTreeNode)child, _childrenLoader, parent);

    private string BuildMetadataText()
    {
        if (_refModel.IsFileGroup)
        {
            var fileGroupParts = new[]
            {
                string.IsNullOrWhiteSpace(_refModel.ProjectName) ? null : _refModel.ProjectName,
                _refModel.OccurrenceCount > 0 ? $"{_refModel.OccurrenceCount} occurrence(s)" : null,
                string.IsNullOrWhiteSpace(_refModel.LocationDisplay) ? null : _refModel.LocationDisplay,
            };

            return string.Join(" • ", fileGroupParts.Where(static value => !string.IsNullOrWhiteSpace(value))!);
        }

        if (_refModel.LineNumber > 0)
        {
            var referenceParts = new[]
            {
                string.IsNullOrWhiteSpace(_refModel.ProjectName) ? null : _refModel.ProjectName,
                string.IsNullOrWhiteSpace(ReferenceKindText) ? null : ReferenceKindText,
                string.IsNullOrWhiteSpace(_refModel.LocationDisplay) ? null : _refModel.LocationDisplay,
            };

            return string.Join(" • ", referenceParts.Where(static value => !string.IsNullOrWhiteSpace(value))!);
        }

        var rootParts = new[]
        {
            string.IsNullOrWhiteSpace(_refModel.Accessibility) ? null : _refModel.Accessibility,
            string.IsNullOrWhiteSpace(_refModel.AssemblyName) ? null : _refModel.AssemblyName,
            string.IsNullOrWhiteSpace(_refModel.LocationDisplay) ? null : _refModel.LocationDisplay,
        };

        return string.Join(" • ", rootParts.Where(static value => !string.IsNullOrWhiteSpace(value))!);
    }
}
