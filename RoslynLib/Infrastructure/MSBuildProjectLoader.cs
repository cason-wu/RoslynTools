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

        if (workspaceTarget.PrimaryProject is null)
        {
            workspaceTarget.Dispose();
            throw new InvalidOperationException($"Target '{projectPath}' does not resolve to a project.");
        }

        try
        {
            return await extractSymbolsWithWorkspaceTargetAsync(
                    workspaceTarget.PrimaryProject,
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

    private static async Task<LoadedProject> extractSymbolsWithWorkspaceTargetAsync(
        Project project,
        LoadedWorkspaceTarget? workspaceTarget,
        CancellationToken cancellationToken)
    {
        var nodes = new List<ProjectNode>();

        var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        
        if (compilation is null)
            throw new InvalidOperationException($"Failed to get compilation for project '{project.Name}'.");

        collectNamespaceNodes( compilation.GlobalNamespace, compilation.Assembly, nodes);

        return new LoadedProject(project, nodes.AsReadOnly(), workspaceTarget: workspaceTarget);
    }

    private static Task<LoadedProject> extractSymbolsAsync(
        Project project,
        CancellationToken cancellationToken)
        => extractSymbolsWithWorkspaceTargetAsync(project, null, cancellationToken);

    private static void collectNamespaceNodes(
        INamespaceSymbol ns,
        IAssemblySymbol projectAssembly,
        List<ProjectNode> nodes)
    {
        if (!ns.IsGlobalNamespace && !SymbolEqualityComparer.Default.Equals(ns.ContainingAssembly, projectAssembly))
            return;

        var typeSymbols = ns.GetTypeMembers().Where(t => t.Locations.Any(l => l.IsInSource)).ToList();

        foreach (var iNamedTypeSymbol in typeSymbols)
            addNode(nodes, iNamedTypeSymbol);

        foreach (var iNamespaceSymbol in ns.GetNamespaceMembers())
            collectNamespaceNodes(iNamespaceSymbol, projectAssembly, nodes);
    }

    private static void collectTypeMembers(
        INamedTypeSymbol parent,
        List<ProjectNode> nodes)
    {
        var typeSymbols = parent.GetTypeMembers().Where(t => t.Locations.Any(l => l.IsInSource)).ToList();

        foreach (var iNamedTypeSymbol in typeSymbols)
            addNode(nodes, iNamedTypeSymbol);
    }

    private static void addNode(List<ProjectNode> nodes, INamedTypeSymbol iNamedTypeSymbol)
    {
        var node = new ProjectNode(iNamedTypeSymbol);
        nodes.Add(node);

        collectTypeMembers(iNamedTypeSymbol, nodes);
    }
}