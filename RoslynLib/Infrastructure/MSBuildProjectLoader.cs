using Microsoft.CodeAnalysis;
using RoslynLib.Domain.Models;

namespace RoslynLib.Infrastructure;

public sealed class MSBuildProjectLoader
{
    private readonly RoslynWorkspaceLoader _workspaceLoader;
    private readonly ProjectNodeBuilder _projectNodeBuilder;

    public MSBuildProjectLoader(RoslynWorkspaceLoader workspaceLoader)
        : this(workspaceLoader, new ProjectNodeBuilder())
    {
    }

    internal MSBuildProjectLoader(
        RoslynWorkspaceLoader workspaceLoader,
        ProjectNodeBuilder projectNodeBuilder)
    {
        _workspaceLoader = workspaceLoader ?? throw new ArgumentNullException(nameof(workspaceLoader));
        _projectNodeBuilder = projectNodeBuilder ?? throw new ArgumentNullException(nameof(projectNodeBuilder));
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
            return await _projectNodeBuilder.BuildAsync(
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
}
