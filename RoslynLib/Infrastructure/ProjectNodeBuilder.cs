using Microsoft.CodeAnalysis;
using RoslynLib.Domain.Models;

namespace RoslynLib.Infrastructure;

internal sealed class ProjectNodeBuilder
{
    public async Task<LoadedProject> BuildAsync(
        LoadedWorkspaceTarget workspaceTarget,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspaceTarget);

        var primaryProject = workspaceTarget.PrimaryProject ?? workspaceTarget.Projects.First();
        var nodes = new List<ProjectNode>();

        foreach (var project in workspaceTarget.Projects)
        {
            var projectNodes = await BuildProjectNodesAsync(project, cancellationToken).ConfigureAwait(false);
            nodes.AddRange(projectNodes);
        }

        return new LoadedProject(primaryProject, nodes.AsReadOnly(), workspaceTarget: workspaceTarget);
    }

    public async Task<IReadOnlyList<ProjectNode>> BuildProjectNodesAsync(
        Project project,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        if (compilation is null)
            throw new InvalidOperationException($"Failed to get compilation for project '{project.Name}'.");

        var assemblyName = project.AssemblyName ?? project.Name;
        var nodes = new List<ProjectNode>();
        CollectNamespaceNodes(compilation.GlobalNamespace, compilation.Assembly, project.Name, assemblyName, nodes);
        return nodes.AsReadOnly();
    }

    public void CollectNamespaceNodes(
        INamespaceSymbol namespaceSymbol,
        IAssemblySymbol projectAssembly,
        string projectName,
        string assemblyName,
        List<ProjectNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(namespaceSymbol);
        ArgumentNullException.ThrowIfNull(projectAssembly);
        ArgumentNullException.ThrowIfNull(projectName);
        ArgumentNullException.ThrowIfNull(assemblyName);
        ArgumentNullException.ThrowIfNull(nodes);

        CollectNamespaceNodesCore(namespaceSymbol, projectAssembly, projectName, assemblyName, nodes);
    }

    private static void CollectNamespaceNodesCore(
        INamespaceSymbol namespaceSymbol,
        IAssemblySymbol projectAssembly,
        string projectName,
        string assemblyName,
        List<ProjectNode> nodes)
    {
        if (!namespaceSymbol.IsGlobalNamespace
            && !SymbolEqualityComparer.Default.Equals(namespaceSymbol.ContainingAssembly, projectAssembly))
        {
            return;
        }

        var typeSymbols = namespaceSymbol.GetTypeMembers()
            .Where(static type => type.Locations.Any(static location => location.IsInSource))
            .ToList();

        foreach (var typeSymbol in typeSymbols)
            AddNode(nodes, typeSymbol, projectName, assemblyName);

        foreach (var nestedNamespace in namespaceSymbol.GetNamespaceMembers())
            CollectNamespaceNodesCore(nestedNamespace, projectAssembly, projectName, assemblyName, nodes);
    }

    private static void CollectTypeMembers(
        INamedTypeSymbol parent,
        string projectName,
        string assemblyName,
        List<ProjectNode> nodes)
    {
        var typeSymbols = parent.GetTypeMembers()
            .Where(static type => type.Locations.Any(static location => location.IsInSource))
            .ToList();

        foreach (var typeSymbol in typeSymbols)
            AddNode(nodes, typeSymbol, projectName, assemblyName);
    }

    private static void AddNode(
        List<ProjectNode> nodes,
        INamedTypeSymbol typeSymbol,
        string projectName,
        string assemblyName)
    {
        var node = new ProjectNode(typeSymbol, projectName, assemblyName);
        nodes.Add(node);
        CollectTypeMembers(typeSymbol, projectName, assemblyName, nodes);
    }
}
