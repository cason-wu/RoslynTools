using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using RoslynLib.Domain.Models;

namespace RoslynLib.Infrastructure;

public sealed class MSBuildProjectLoader
{
    private static readonly object _registrationLock = new();
    private static bool _msbuildRegistered;

    private static void EnsureMSBuildRegistered()
    {
        lock (_registrationLock)
        {
            if (!_msbuildRegistered)
            {
                MSBuildLocator.RegisterDefaults();
                _msbuildRegistered = true;
            }
        }
    }

    public async Task<LoadedProject> LoadProjectAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        EnsureMSBuildRegistered();

        using var workspace = MSBuildWorkspace.Create();
        var project = await workspace.OpenProjectAsync(projectPath, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        
        return await extractSymbolsAsync(
                project,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<LoadedProject> extractSymbolsAsync(
        Project project,
        CancellationToken cancellationToken)
    {
        var nodes = new List<ProjectNode>();

        var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        
        if (compilation is null)
            throw new InvalidOperationException($"Failed to get compilation for project '{project.Name}'.");

        collectNamespaceNodes( compilation.GlobalNamespace, compilation.Assembly, nodes);

        return new LoadedProject(project, nodes.AsReadOnly());
    }

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