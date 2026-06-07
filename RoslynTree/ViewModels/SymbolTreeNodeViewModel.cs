using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.VisualStudio.Imaging.Interop;
using System.Collections.ObjectModel;
using RoslynTree.Infrastructure;
using RoslynLib.Infrastructure;
using RoslynLib.Domain.Models;

namespace RoslynTree.ViewModels;

/// <summary>
/// Abstract base ViewModel for nodes in any symbol tree (Call Tree, Reference Tree, etc.).
/// Concrete subclasses supply type-specific properties and child creation.
/// </summary>
public abstract partial class SymbolTreeNodeViewModel : ObservableObject
{
    private readonly RoslynTreeNode _model;
    protected readonly Func<SymbolTreeNodeViewModel, CancellationToken, Task<IReadOnlyList<RoslynTreeNode>>>? _childrenLoader;
    private readonly bool _isPlaceholder;
    private bool _isLoadingChildren;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    public virtual string Name => _model.Name;
    public string FullName => _model.FullName;
    public SymbolNodeKind Kind => _model.Kind;
    internal RoslynTreeNode Model => _model;
    public ImageMoniker Icon => VisualStudioSymbolIconMap.GetIcon(Kind);
    public SymbolTreeNodeViewModel? Parent { get; }
    public ObservableCollection<SymbolTreeNodeViewModel> Children { get; }

    protected SymbolTreeNodeViewModel(
        RoslynTreeNode model,
        Func<SymbolTreeNodeViewModel, CancellationToken, Task<IReadOnlyList<RoslynTreeNode>>>? childrenLoader,
        SymbolTreeNodeViewModel? parent,
        bool isPlaceholder = false)
    {
        _model = model;
        _childrenLoader = childrenLoader;
        Parent = parent;
        _isPlaceholder = isPlaceholder;

        Children = new ObservableCollection<SymbolTreeNodeViewModel>(
            model.Children.Select(c => CreateChildViewModel(c, this)));

        EnsurePlaceholderChild();
    }

    /// <summary>
    /// Factory method: creates the correct concrete child ViewModel for a given model node.
    /// </summary>
    protected abstract SymbolTreeNodeViewModel CreateChildViewModel(RoslynTreeNode child, SymbolTreeNodeViewModel parent);

    public IEnumerable<string> GetPathKeysFromRoot()
    {
        var stack = new Stack<string>();
        SymbolTreeNodeViewModel? cursor = this;

        while (cursor is not null)
        {
            stack.Push(cursor.FullName);
            cursor = cursor.Parent;
        }

        while (stack.Count > 0)
            yield return stack.Pop();
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (!value) return;

        _ = EnsureChildrenLoadedAsync(CancellationToken.None);
    }

    public async Task EnsureChildrenLoadedAsync(CancellationToken cancellationToken)
    {
        if (_isPlaceholder || _childrenLoader is null || _isLoadingChildren)
            return;

        if (_model.IsChildrenLoaded || !_model.HasUnloadedChildren)
            return;

        _isLoadingChildren = true;
        RemovePlaceholderChild();

        try
        {
            var loadedChildren = await _childrenLoader(this, cancellationToken).ConfigureAwait(true);
            var existing = new HashSet<string>(
                Children.Where(c => c is not PlaceholderNodeViewModel).Select(c => c.FullName),
                StringComparer.Ordinal);

            foreach (var childModel in loadedChildren)
            {
                if (!existing.Add(childModel.FullName))
                    continue;

                Children.Add(CreateChildViewModel(childModel, this));
            }

            _model.IsChildrenLoaded = true;
        }
        finally
        {
            _isLoadingChildren = false;
            EnsurePlaceholderChild();
        }
    }

    private void EnsurePlaceholderChild()
    {
        if (_isPlaceholder || _childrenLoader is null)
            return;

        if (!_model.HasUnloadedChildren || _model.IsChildrenLoaded)
            return;

        if (Children.Count > 0)
            return;

        Children.Add(new PlaceholderNodeViewModel(this));
    }

    private void RemovePlaceholderChild()
    {
        for (var i = Children.Count - 1; i >= 0; i--)
        {
            if (Children[i] is PlaceholderNodeViewModel)
                Children.RemoveAt(i);
        }
    }

    private sealed class PlaceholderNodeViewModel : SymbolTreeNodeViewModel
    {
        public PlaceholderNodeViewModel(SymbolTreeNodeViewModel parent)
            : base(
                new PlaceholderNode
                {
                    Name = "(Expand to load...)",
                    FullName = Guid.NewGuid().ToString("N"),
                    Kind = SymbolNodeKind.Unknown,
                    IsChildrenLoaded = true,
                },
                childrenLoader: null,
                parent,
                isPlaceholder: true)
        {
        }

        protected override SymbolTreeNodeViewModel CreateChildViewModel(RoslynTreeNode child, SymbolTreeNodeViewModel parent)
            => throw new NotSupportedException("Placeholder nodes cannot have children.");
    }

    private sealed class PlaceholderNode : RoslynTreeNode
    {
    }
}
