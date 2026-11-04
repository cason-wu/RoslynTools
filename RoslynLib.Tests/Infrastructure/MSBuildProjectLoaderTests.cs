using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynLib.Domain.Models;
using RoslynLib.Infrastructure;

namespace RoslynLib.Tests.Infrastructure;

public class ProjectNodeBuilderTests
{
    private readonly ProjectNodeBuilder _sut = new();
    private static readonly IReadOnlyList<MetadataReference> _references = BuildDefaultReferences();

    private static IReadOnlyList<MetadataReference> BuildDefaultReferences()
    {
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty;
        return tpa
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
    }

    private static Project CreateProject(string projectName, string source, string documentName)
    {
        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId(projectName);
        var projectFilePath = Path.Combine(Path.GetTempPath(), $"{projectName}.csproj");
        var documentFilePath = Path.Combine(Path.GetTempPath(), documentName);
        var solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(
            projectId,
            VersionStamp.Default,
            projectName,
            projectName,
            LanguageNames.CSharp,
            filePath: projectFilePath,
            metadataReferences: _references,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)));

        var documentId = DocumentId.CreateNewId(projectId, documentName);
        solution = solution.AddDocument(
            documentId,
            documentName,
            Microsoft.CodeAnalysis.Text.SourceText.From(source),
            filePath: documentFilePath);

        return solution.GetProject(projectId)!;
    }

    private static CSharpCompilation CreateCompilation(string assemblyName, string source)
    {
        return CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            _references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static LoadedWorkspaceTarget CreateWorkspaceTarget(Project project)
    {
        return new LoadedWorkspaceTarget(
            project.Solution.Workspace,
            project.FilePath ?? project.Name,
            WorkspaceTargetKind.Project,
            project.Solution,
            project,
            project.Documents.FirstOrDefault());
    }

    [Fact(DisplayName = "Collects top-level and nested source types")]
    public async Task ExtractSymbolsAsync_CollectsTopLevelAndNestedTypes()
    {
        // Setup
        var project = CreateProject(
            "P1",
            """
            namespace Demo;
            public class Outer
            {
                public class Inner { }
            }
            """,
            "P1.cs");

        // Execute
        using var workspaceTarget = CreateWorkspaceTarget(project);
        var result = await _sut.BuildAsync(workspaceTarget);

        // Verify
        Assert.Contains(result.Nodes, s => s.Name == "Outer" && s.Kind == SymbolNodeKind.Class);
        Assert.Contains(result.Nodes, s => s.Name == "Inner" && s.Kind == SymbolNodeKind.Class);
        Assert.Contains(result.Nodes, s => s.FullName.Contains("Demo.Outer", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Project root is returned from loaded project context")]
    public async Task ExtractSymbolsAsync_ProjectRoot_IsReturned()
    {
        // Setup
        var project = CreateProject(
            "P1",
            "namespace Demo; public class RootType { }",
            "P1.cs");

        // Execute
        using var workspaceTarget = CreateWorkspaceTarget(project);
        var result = await _sut.BuildAsync(workspaceTarget);

        // Verify
        var expectedRoot = Path.GetDirectoryName(project.FilePath) ?? string.Empty;
        Assert.Equal(expectedRoot, result.ProjectRoot);
        Assert.Equal(project.Id, result.Project.Id);
    }

    [Fact(DisplayName = "Namespace collection excludes namespaces from other assemblies")]
    public void CollectTypes_NamespaceFromDifferentAssembly_Excluded()
    {
        // Setup
        var projectCompilation = CreateCompilation(
            "ProjectAssembly",
            "namespace ProjectNs; public class Keep { }");
        var foreignCompilation = CreateCompilation(
            "ForeignAssembly",
            "namespace ForeignNs; public class ShouldBeExcluded { }");

        var nodes = new List<ProjectNode>();
        var foreignNamespace = foreignCompilation.GlobalNamespace.GetNamespaceMembers()
            .Single(n => n.Name == "ForeignNs");

        // Execute
        _sut.CollectNamespaceNodes(
            foreignNamespace,
            projectCompilation.Assembly,
            "ProjectAssembly",
            projectCompilation.Assembly.Name,
            nodes);

        // Verify
        Assert.Empty(nodes);
    }

    [Fact(DisplayName = "Global namespace source types are kept")]
    public async Task ExtractSymbolsAsync_GlobalNamespaceSourceTypes_Collected()
    {
        // Setup
        var project = CreateProject(
            "P1",
            "public class TopLevelType { }",
            "P1.cs");

        // Execute
        var result = await _sut.BuildProjectNodesAsync(project);

        // Verify
        Assert.Contains(result, s =>
            s.Name == "TopLevelType" &&
            (s.Namespace == string.Empty || s.Namespace == "<global namespace>") &&
            s.FullName == "TopLevelType");
    }

    [Fact(DisplayName = "Entry computed properties match embedded symbol")]
    public async Task ExtractSymbolsAsync_EntryComputedProperties_MatchEmbeddedSymbol()
    {
        // Setup
        var project = CreateProject(
            "P1",
            "namespace Demo; public interface IFoo { }",
            "IFoo.cs");

        // Execute
        var result = await _sut.BuildProjectNodesAsync(project);
        var entry = Assert.Single(result, s => s.Name == "IFoo");

        // Verify
        Assert.Equal(entry.Symbol.Name, entry.Name);
        Assert.Equal(entry.Symbol.ToDisplayString(), entry.FullName);
        Assert.Equal(SymbolNodeKind.Interface, entry.Kind);
        Assert.Equal(entry.Symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty, entry.Namespace);
    }

    [Fact(DisplayName = "Entry source file name resolves from source location")]
    public async Task ExtractSymbolsAsync_EntrySourceFileName_IsResolved()
    {
        // Setup
        var project = CreateProject(
            "P1",
            "namespace Demo; public class SourceBackedType { }",
            "SourceBackedType.cs");

        // Execute
        var result = await _sut.BuildProjectNodesAsync(project);
        var entry = Assert.Single(result, s => s.Name == "SourceBackedType");

        // Verify
        Assert.Equal("SourceBackedType.cs", entry.SourceFileName);
    }
}
