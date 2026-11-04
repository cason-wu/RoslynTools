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
        Document? primaryDocument,
        IReadOnlyList<string>? loadDiagnostics = null)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        TargetPath = targetPath ?? throw new ArgumentNullException(nameof(targetPath));
        TargetKind = targetKind;
        Solution = solution ?? throw new ArgumentNullException(nameof(solution));
        PrimaryProject = primaryProject;
        PrimaryDocument = primaryDocument;
        Projects = solution.Projects.ToList().AsReadOnly();
        LoadDiagnostics = loadDiagnostics ?? [];
        LoadedAtUtc = DateTime.UtcNow;
    }

    public string TargetPath { get; }
    public WorkspaceTargetKind TargetKind { get; }
    public Solution Solution { get; private set; }
    public Project? PrimaryProject { get; private set; }
    public Document? PrimaryDocument { get; private set; }
    public IReadOnlyList<Project> Projects { get; private set; }
    public IReadOnlyList<string> LoadDiagnostics { get; }
    public DateTime LoadedAtUtc { get; }

    public string TargetDirectory
        => Directory.Exists(TargetPath)
            ? TargetPath
            : Path.GetDirectoryName(TargetPath) ?? string.Empty;

    internal void ReplaceSolution(Solution solution)
    {
        ArgumentNullException.ThrowIfNull(solution);

        Solution = solution;
        Projects = solution.Projects.ToList().AsReadOnly();
        PrimaryProject = PrimaryProject is null ? null : solution.GetProject(PrimaryProject.Id);
        PrimaryDocument = PrimaryDocument is null ? null : solution.GetDocument(PrimaryDocument.Id);
    }

    public void Dispose()
        => _workspace.Dispose();
}
