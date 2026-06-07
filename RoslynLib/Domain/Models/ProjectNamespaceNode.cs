namespace RoslynLib.Domain.Models;

public sealed class ProjectNamespaceNode
{
    public ProjectNamespaceNode(
        string name,
        IReadOnlyList<ProjectNode> types)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Types = types ?? throw new ArgumentNullException(nameof(types));
    }

    public string Name { get; }
    public IReadOnlyList<ProjectNode> Types { get; }
}