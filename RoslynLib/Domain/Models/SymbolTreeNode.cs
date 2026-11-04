namespace RoslynLib.Domain.Models;

public abstract class RoslynTreeNode
{
    public string Name { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public SymbolNodeKind Kind { get; init; }
    public bool HasUnloadedChildren { get; set; }
    public bool IsChildrenLoaded { get; set; }
    public List<RoslynTreeNode> Children { get; } = [];
}
