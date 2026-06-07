using Microsoft.CodeAnalysis;
using RoslynLib.Domain.Models;

namespace RoslynLib.Infrastructure;

public sealed class MSBuildProjectLoader
{
    private readonly RoslynWorkspaceLoader _workspaceLoader;

    public MSBuildProjectLoader(RoslynWorkspaceLoader workspaceLoader)
    {
        _workspaceLoader = workspaceLoader ?? throw new ArgumentNullException(nameof(workspaceLoader));
    }

    public async Task<LoadedProject> LoadProjectAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        var workspaceTarget = await _workspaceLoader.LoadAsync(projectPath, cancellationToken).ConfigureAwait(false);

        if (workspaceTarget.Projects.Count == 0)
        {
            workspaceTarget.Dispose();
            throw new InvalidOperationException($"Target '{projectPath}' does not resolve to any project.");
        }

        try
        {
            return await extractWorkspaceSymbolsAsync(
                    workspaceTarget,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            workspaceTarget.Dispose();
            throw;
        }
    }

    private static async Task<LoadedProject> extractWorkspaceSymbolsAsync(
        LoadedWorkspaceTarget workspaceTarget,
        CancellationToken cancellationToken)
    {
        var primaryProject = workspaceTarget.PrimaryProject ?? workspaceTarget.Projects.First();
        var nodes = new List<ProjectNode>();

        foreach (var project in workspaceTarget.Projects)
        {
            var projectNodes = await extractProjectNodesAsync(project, cancellationToken).ConfigureAwait(false);
            nodes.AddRange(projectNodes);
        }

        return new LoadedProject(primaryProject, nodes.AsReadOnly(), workspaceTarget: workspaceTarget);
    }

    private static async Task<IReadOnlyList<ProjectNode>> extractProjectNodesAsync(
        Project project,
        CancellationToken cancellationToken)
    {
        var nodes = new List<ProjectNode>();

        var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        
        if (compilation is null)
            throw new InvalidOperationException($"Failed to get compilation for project '{project.Name}'.");

        var assemblyName = project.AssemblyName ?? project.Name;
        collectNamespaceNodesForProject(compilation.GlobalNamespace, compilation.Assembly, project.Name, assemblyName, nodes);

        return nodes.AsReadOnly();
    }

    private static Task<LoadedProject> extractSymbolsAsync(
        Project project,
        CancellationToken cancellationToken)
        => extractSymbolsAsyncCore(project, cancellationToken);

    private static async Task<LoadedProject> extractSymbolsAsyncCore(
        Project project,
        CancellationToken cancellationToken)
    {
        var nodes = await extractProjectNodesAsync(project, cancellationToken).ConfigureAwait(false);
        return new LoadedProject(project, nodes);
    }

    private static void collectNamespaceNodes(
        INamespaceSymbol ns,
        IAssemblySymbol projectAssembly,
        List<ProjectNode> nodes)
    {
        collectNamespaceNodesForProject(ns, projectAssembly, string.Empty, projectAssembly.Name, nodes);
    }

    private static void collectNamespaceNodesForProject(
        INamespaceSymbol ns,
        IAssemblySymbol projectAssembly,
        string projectName,
        string assemblyName,
        List<ProjectNode> nodes)
    {
        if (!ns.IsGlobalNamespace && !SymbolEqualityComparer.Default.Equals(ns.ContainingAssembly, projectAssembly))
            return;

        var typeSymbols = ns.GetTypeMembers().Where(t => t.Locations.Any(l => l.IsInSource)).ToList();

        foreach (var iNamedTypeSymbol in typeSymbols)
            addNode(nodes, iNamedTypeSymbol, projectName, assemblyName);

        foreach (var iNamespaceSymbol in ns.GetNamespaceMembers())
            collectNamespaceNodesForProject(iNamespaceSymbol, projectAssembly, projectName, assemblyName, nodes);
    }

    private static void collectTypeMembers(
        INamedTypeSymbol parent,
        string projectName,
        string assemblyName,
        List<ProjectNode> nodes)
    {
        var typeSymbols = parent.GetTypeMembers().Where(t => t.Locations.Any(l => l.IsInSource)).ToList();

        foreach (var iNamedTypeSymbol in typeSymbols)
            addNode(nodes, iNamedTypeSymbol, projectName, assemblyName);
    }

    private static void addNode(
        List<ProjectNode> nodes,
        INamedTypeSymbol iNamedTypeSymbol,
        string projectName,
        string assemblyName)
    {
        var node = new ProjectNode(iNamedTypeSymbol, projectName, assemblyName);
        nodes.Add(node);

        collectTypeMembers(iNamedTypeSymbol, projectName, assemblyName, nodes);
    }
}