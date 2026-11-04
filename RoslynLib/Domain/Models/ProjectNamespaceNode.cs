namespace RoslynLib.Domain.Models;

public sealed class ProjectNamespaceNode
{
    private readonly Lazy<IReadOnlyList<string>> _projectNames;

    public ProjectNamespaceNode(
        string name,
        IReadOnlyList<ProjectNode> types)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Types = types ?? throw new ArgumentNullException(nameof(types));
        _projectNames = new Lazy<IReadOnlyList<string>>(BuildProjectNames);
    }

    public string Name { get; }
    public IReadOnlyList<ProjectNode> Types { get; }
    public int TypeCount => Types.Count;
    public IReadOnlyList<string> ProjectNames => _projectNames.Value;
    public int ProjectCount => ProjectNames.Count;

    private IReadOnlyList<string> BuildProjectNames()
    {
        return Types
            .Select(static type => type.ProjectName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
    }
}
