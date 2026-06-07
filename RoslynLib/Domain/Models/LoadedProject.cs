using Microsoft.CodeAnalysis;

namespace RoslynLib.Domain.Models;

public sealed class LoadedProject
{
    public LoadedProject(
        Project project,
        IReadOnlyList<ProjectNode> nodes)
    {
        Project = project;
        Nodes = nodes;
    }
    public Project Project { get; init; }
    public IReadOnlyList<ProjectNode> Nodes { get; init; }

    public string ProjectRoot => Path.GetDirectoryName(Project.FilePath) ?? string.Empty;
}
