using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using RoslynLib.Domain.Models;
using RoslynLib.Infrastructure;
using RoslynLib.Tests.Helpers;

namespace RoslynLib.Tests.Infrastructure;

public sealed class RoslynCliAnalysisServiceTests
{
    private static readonly IReadOnlyList<MetadataReference> DefaultReferences = BuildDefaultReferences();
    private readonly RoslynCliAnalysisService _sut = new();

    [Fact(DisplayName = "Wildcard symbol search returns matching classes")]
    public async Task SearchSymbolsAsync_Wildcard_ReturnsMatchingClasses()
    {
        using var workspaceTarget = CreateWorkspaceTarget(
            "SearchProject",
            """
            namespace Demo;

            public class OrderService { }
            public class BillingService { }
            public class Controller { }
            """);

        var result = await _sut.SearchSymbolsAsync(
            workspaceTarget,
            new RoslynCliQueryOptions
            {
                Symbol = "*Service",
                Filter = "class",
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(2, result.TotalCount);
        Assert.Contains(result.Symbols, static item => item.Symbol == "OrderService");
        Assert.Contains(result.Symbols, static item => item.Symbol == "BillingService");
    }

    [Fact(DisplayName = "Symbol info includes interfaces and member counts")]
    public async Task GetSymbolInfoAsync_TypeSymbol_ReturnsMemberSummary()
    {
        using var workspaceTarget = CreateWorkspaceTarget(
            "InfoProject",
            """
            namespace Demo;

            public interface IJob
            {
                void Run();
            }

            public class JobService : IJob
            {
                public int Count { get; set; }
                public void Run() { }
            }
            """);

        var result = await _sut.GetSymbolInfoAsync(
            workspaceTarget,
            new RoslynCliQueryOptions
            {
                Symbol = "JobService",
            },
            TestContext.Current.CancellationToken);

        Assert.Equal("Class", result.Kind);
        Assert.Contains("Demo.IJob", result.Interfaces);
        Assert.True(result.Members.Methods >= 1);
        Assert.Equal(1, result.Members.Properties);
    }

    [Fact(DisplayName = "Unused symbol analysis returns unused method")]
    public async Task FindUnusedSymbolsAsync_UnusedMethod_IsReported()
    {
        using var workspaceTarget = CreateWorkspaceTarget(
            "UnusedProject",
            """
            namespace Demo;

            public class Worker
            {
                public void Used() { }
                public void Unused() { }
                public void Run() => Used();
            }
            """);

        var result = await _sut.FindUnusedSymbolsAsync(
            workspaceTarget,
            new RoslynCliQueryOptions
            {
                Filter = "method",
                Visibility = "public",
            },
            TestContext.Current.CancellationToken);

        Assert.Contains(result.UnusedSymbols, static item => item.Symbol.Contains("Unused", StringComparison.Ordinal));
        Assert.DoesNotContain(result.UnusedSymbols, static item => item.Symbol.Contains("Used", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "ProjectNode exposes delegate signature display name")]
    public void ProjectNode_DelegateDisplayName_UsesSignature()
    {
        var compilation = RoslynTestHelper.Compile(
            """
            namespace Demo;

            public delegate int Transformer(ref int value);
            """);
        var symbol = RoslynTestHelper.GetTypeSymbol(compilation, "Transformer");

        var node = new ProjectNode(symbol, "DemoProject", "DemoAssembly");

        Assert.Equal("Transformer(ref int) : int", node.DisplayName);
    }

    [Fact(DisplayName = "Find callers returns direct caller locations")]
    public async Task FindCallersAsync_MethodSymbol_ReturnsCallerLocations()
    {
        using var workspaceTarget = CreateWorkspaceTarget(
            "CallersProject",
            """
            namespace Demo;

            public class Service
            {
                public void Target() { }
                public void Caller() => Target();
            }
            """);

        var result = await _sut.FindCallersAsync(
            workspaceTarget,
            new RoslynCliQueryOptions
            {
                Symbol = "Demo.Service.Target()",
            },
            TestContext.Current.CancellationToken);

        Assert.Single(result.Callers);
        Assert.Contains("Caller", result.Callers[0].CallerSymbol, StringComparison.Ordinal);
        Assert.Contains("Target", result.Callers[0].Snippet, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Find usages returns containing context")]
    public async Task FindUsagesAsync_TypeSymbol_ReturnsContainingContext()
    {
        using var workspaceTarget = CreateWorkspaceTarget(
            "UsagesProject",
            """
            namespace Demo;

            public interface IRepository { }

            public class Consumer
            {
                private readonly IRepository _repository;

                public Consumer(IRepository repository)
                {
                    _repository = repository;
                }
            }
            """);

        var result = await _sut.FindUsagesAsync(
            workspaceTarget,
            new RoslynCliQueryOptions
            {
                Symbol = "Demo.IRepository",
            },
            TestContext.Current.CancellationToken);

        Assert.True(result.TotalCount >= 2);
        Assert.Contains(result.Usages, static usage => usage.ContainingSymbol == "Consumer");
    }

    [Fact(DisplayName = "Find implementations returns interface implementations")]
    public async Task FindImplementationsAsync_InterfaceType_ReturnsImplementingClass()
    {
        using var workspaceTarget = CreateWorkspaceTarget(
            "ImplementationsProject",
            """
            namespace Demo;

            public interface IWorker
            {
                void Run();
            }

            public class Worker : IWorker
            {
                public void Run() { }
            }
            """);

        var result = await _sut.FindImplementationsAsync(
            workspaceTarget,
            new RoslynCliQueryOptions
            {
                Symbol = "Demo.IWorker",
            },
            TestContext.Current.CancellationToken);

        Assert.Single(result.Implementations);
        Assert.Contains("Worker", result.Implementations[0].Symbol, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Diagnostics errors returns compilation failures")]
    public async Task GetErrorsAsync_InvalidCode_ReturnsCompilationErrors()
    {
        using var workspaceTarget = CreateWorkspaceTarget(
            "ErrorsProject",
            """
            namespace Demo;

            public class BrokenService
            {
                public int Run()
                {
                    return MissingValue;
                }
            }
            """);

        var result = await _sut.GetErrorsAsync(
            workspaceTarget,
            new RoslynCliQueryOptions(),
            TestContext.Current.CancellationToken);

        Assert.NotEmpty(result.Diagnostics);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Id == "CS0103");
    }

    [Fact(DisplayName = "Diagnostics warnings returns obsolete usage warnings")]
    public async Task GetWarningsAsync_ObsoleteUsage_ReturnsWarningDiagnostics()
    {
        using var workspaceTarget = CreateWorkspaceTarget(
            "WarningsProject",
            """
            namespace Demo;

            public class LegacyService
            {
                [System.Obsolete]
                public static void Legacy() { }
            }

            public class Consumer
            {
                public void Run()
                {
                    LegacyService.Legacy();
                }
            }
            """);

        var result = await _sut.GetWarningsAsync(
            workspaceTarget,
            new RoslynCliQueryOptions(),
            TestContext.Current.CancellationToken);

        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Id is "CS0612" or "CS0618");
    }

    private static IReadOnlyList<MetadataReference> BuildDefaultReferences()
    {
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty;
        return trustedPlatformAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
    }

    private static LoadedWorkspaceTarget CreateWorkspaceTarget(string projectName, string source)
    {
        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId(projectName);
        var documentId = DocumentId.CreateNewId(projectId);
        var projectFilePath = Path.Combine(Path.GetTempPath(), $"{projectName}.csproj");
        var documentFilePath = Path.Combine(Path.GetTempPath(), $"{projectName}.cs");

        var solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(
            projectId,
            VersionStamp.Default,
            projectName,
            projectName,
            LanguageNames.CSharp,
            filePath: projectFilePath,
            metadataReferences: DefaultReferences,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)));

        solution = solution.AddDocument(
            documentId,
            Path.GetFileName(documentFilePath),
            SourceText.From(source),
            filePath: documentFilePath);

        var project = solution.GetProject(projectId)
            ?? throw new InvalidOperationException("Failed to create test project.");
        var document = solution.GetDocument(documentId)
            ?? throw new InvalidOperationException("Failed to create test document.");

        return new LoadedWorkspaceTarget(
            workspace,
            projectFilePath,
            WorkspaceTargetKind.Project,
            solution,
            project,
            document);
    }
}
