using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace RoslynLib.Tests.Helpers;

/// <summary>
/// Creates lightweight in-memory Roslyn compilations for unit testing.
/// </summary>
public static class RoslynTestHelper
{
    private static readonly IReadOnlyList<MetadataReference> DefaultReferences = BuildDefaultReferences();

    private static IReadOnlyList<MetadataReference> BuildDefaultReferences()
    {
        var trustedPlatformAssembly = System.AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty;
        return trustedPlatformAssembly
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
    }

    /// <summary>
    /// Compiles C# source and returns the resulting <see cref="Compilation"/>.
    /// </summary>
    public static CSharpCompilation Compile(string source, string assemblyName = "TestAssembly")
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        return CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            DefaultReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    /// <summary>
    /// Returns the first named type with the given name from a compilation.
    /// </summary>
    public static INamedTypeSymbol GetTypeSymbol(Compilation compilation, string typeName)
    {
        var symbol = compilation.GetSymbolsWithName(typeName, SymbolFilter.Type).FirstOrDefault();
        return symbol as INamedTypeSymbol
            ?? throw new InvalidOperationException($"Type '{typeName}' not found in compilation.");
    }

    /// <summary>
    /// Creates an in-memory <see cref="Solution"/> with a single C# document.
    /// Useful for tests that require a <see cref="Solution"/> (call tree, references, hierarchy).
    /// </summary>
    public static Solution CreateSolution(string source, string projectName = "TestProject")
    {
        var workspace = new AdhocWorkspace();

        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);

        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Default,
            projectName,
            projectName,
            LanguageNames.CSharp,
            metadataReferences: DefaultReferences,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithDocuments([
                DocumentInfo.Create(documentId, "TestDocument.cs",
                    loader: TextLoader.From(
                        TextAndVersion.Create(
                            Microsoft.CodeAnalysis.Text.SourceText.From(source),
                            VersionStamp.Default)))
            ]);

        return workspace.AddProject(projectInfo).Solution;
    }
}
