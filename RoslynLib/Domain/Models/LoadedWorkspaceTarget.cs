using Microsoft.CodeAnalysis;

namespace RoslynLib.Domain.Models;

public enum WorkspaceTargetKind
{
    Solution,
    Project,
    Document,
}

public sealed class LoadedWorkspaceTarget : IDisposable
{
    private readonly Workspace _workspace;

    public LoadedWorkspaceTarget(
        Workspace workspace,
        string targetPath,
        WorkspaceTargetKind targetKind,
        Solution solution,
        Project? primaryProject,
        Document? primaryDocument)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        TargetPath = targetPath ?? throw new ArgumentNullException(nameof(targetPath));
        TargetKind = targetKind;
        Solution = solution ?? throw new ArgumentNullException(nameof(solution));
        PrimaryProject = primaryProject;
        PrimaryDocument = primaryDocument;
        Projects = solution.Projects.ToList().AsReadOnly();
    }

    public string TargetPath { get; }
    public WorkspaceTargetKind TargetKind { get; }
    public Solution Solution { get; }
    public Project? PrimaryProject { get; }
    public Document? PrimaryDocument { get; }
    public IReadOnlyList<Project> Projects { get; }

    public string TargetDirectory
        => Directory.Exists(TargetPath)
            ? TargetPath
            : Path.GetDirectoryName(TargetPath) ?? string.Empty;

    public void Dispose()
        => _workspace.Dispose();
}