using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using RoslynLib.Domain.Models;

namespace RoslynLib.Infrastructure;

public sealed class RoslynWorkspaceLoader
{
    private static readonly object RegistrationLock = new();
    private static bool _msbuildRegistered;
    private static readonly Lazy<IReadOnlyList<MetadataReference>> DefaultMetadataReferences = new(BuildDefaultMetadataReferences);

    public async Task<LoadedWorkspaceTarget> LoadAsync(
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            throw new ArgumentException("Target path is required.", nameof(targetPath));

        var fullPath = Path.GetFullPath(targetPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Target '{fullPath}' does not exist.", fullPath);

        var extension = Path.GetExtension(fullPath);
        return extension.ToLowerInvariant() switch
        {
            ".sln" or ".slnx" => await LoadSolutionAsync(fullPath, cancellationToken).ConfigureAwait(false),
            ".csproj" or ".vbproj" or ".fsproj" => await LoadProjectAsync(fullPath, cancellationToken).ConfigureAwait(false),
            ".cs" => await LoadDocumentAsync(fullPath, cancellationToken).ConfigureAwait(false),
            _ => throw new NotSupportedException($"Unsupported target type '{extension}'. Expected .sln, .slnx, .csproj, .vbproj, .fsproj, or .cs."),
        };
    }

    private static async Task<LoadedWorkspaceTarget> LoadSolutionAsync(
        string solutionPath,
        CancellationToken cancellationToken)
    {
        EnsureMSBuildRegistered();

        var workspace = MSBuildWorkspace.Create();
        try
        {
            var solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return new LoadedWorkspaceTarget(
                workspace,
                solutionPath,
                WorkspaceTargetKind.Solution,
                solution,
                solution.Projects.FirstOrDefault(),
                null);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    private static async Task<LoadedWorkspaceTarget> LoadProjectAsync(
        string projectPath,
        CancellationToken cancellationToken)
    {
        EnsureMSBuildRegistered();

        var workspace = MSBuildWorkspace.Create();
        try
        {
            var project = await workspace.OpenProjectAsync(projectPath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return new LoadedWorkspaceTarget(
                workspace,
                projectPath,
                WorkspaceTargetKind.Project,
                project.Solution,
                project,
                null);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    private static async Task<LoadedWorkspaceTarget> LoadDocumentAsync(
        string documentPath,
        CancellationToken cancellationToken)
    {
        var workspace = new AdhocWorkspace();

        try
        {
            var projectId = ProjectId.CreateNewId();
            var documentId = DocumentId.CreateNewId(projectId);
            var sourceText = await File.ReadAllTextAsync(documentPath, cancellationToken).ConfigureAwait(false);
            var projectName = Path.GetFileNameWithoutExtension(documentPath);
            var projectInfo = ProjectInfo.Create(
                    projectId,
                    VersionStamp.Default,
                    projectName,
                    projectName,
                    LanguageNames.CSharp,
                    filePath: Path.ChangeExtension(documentPath, ".csproj"),
                    metadataReferences: DefaultMetadataReferences.Value,
                    parseOptions: new CSharpParseOptions(LanguageVersion.Preview),
                    compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                .WithDocuments([
                    DocumentInfo.Create(
                        documentId,
                        Path.GetFileName(documentPath),
                        filePath: documentPath,
                        loader: TextLoader.From(TextAndVersion.Create(SourceText.From(sourceText), VersionStamp.Default)))
                ]);

            var solution = workspace.AddProject(projectInfo).Solution;
            var project = solution.GetProject(projectId)
                ?? throw new InvalidOperationException("Failed to create in-memory project for source file.");
            var document = project.GetDocument(documentId)
                ?? throw new InvalidOperationException("Failed to create in-memory document for source file.");

            return new LoadedWorkspaceTarget(
                workspace,
                documentPath,
                WorkspaceTargetKind.Document,
                project.Solution,
                project,
                document);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    private static void EnsureMSBuildRegistered()
    {
        lock (RegistrationLock)
        {
            if (_msbuildRegistered)
                return;

            MSBuildLocator.RegisterDefaults();
            _msbuildRegistered = true;
        }
    }

    private static IReadOnlyList<MetadataReference> BuildDefaultMetadataReferences()
    {
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty;
        return trustedPlatformAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
    }
}