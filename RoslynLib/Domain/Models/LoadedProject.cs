using Microsoft.CodeAnalysis;

namespace RoslynLib.Domain.Models;

public sealed class LoadedProject : IDisposable
{
    public LoadedProject(
        Project project,
        IReadOnlyList<ProjectNode> nodes,
        IReadOnlyList<ProjectNamespaceNode>? namespaceNodes = null,
        LoadedWorkspaceTarget? workspaceTarget = null)
    {
        Project = project;
        Nodes = nodes;
        NamespaceNodes = namespaceNodes ?? BuildNamespaceNodes(nodes);
        WorkspaceTarget = workspaceTarget;
    }

    public Project Project { get; init; }
    public IReadOnlyList<ProjectNode> Nodes { get; init; }
    public IReadOnlyList<ProjectNamespaceNode> NamespaceNodes { get; init; }
    public LoadedWorkspaceTarget? WorkspaceTarget { get; }

    public string ProjectRoot
        => Path.GetDirectoryName(Project.FilePath)
            ?? WorkspaceTarget?.TargetDirectory
            ?? string.Empty;

    public void Dispose()
        => WorkspaceTarget?.Dispose();

    private static IReadOnlyList<ProjectNamespaceNode> BuildNamespaceNodes(IReadOnlyList<ProjectNode> nodes)
    {
        return nodes
            .GroupBy(static node => string.IsNullOrEmpty(node.Namespace) ? "<global>" : node.Namespace)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(static group => new ProjectNamespaceNode(
                group.Key,
                group.OrderBy(static node => node.Name, StringComparer.Ordinal)
                    .ThenBy(static node => node.FullName, StringComparer.Ordinal)
                    .ToList()
                    .AsReadOnly()))
            .ToList()
            .AsReadOnly();
    }
}
