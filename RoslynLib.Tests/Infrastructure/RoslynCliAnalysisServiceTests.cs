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

    [Fact(DisplayName = "Ambiguous symbol name reports candidates instead of picking one")]
    public async Task GetSymbolInfoAsync_AmbiguousOverloads_ThrowsWithCandidates()
    {
        using var workspaceTarget = CreateWorkspaceTarget(
            "AmbiguousProject",
            """
            namespace Demo;

            public class Calculator
            {
                public int Add(int left, int right) => left + right;
                public double Add(double left, double right) => left + right;
            }
            """);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.GetSymbolInfoAsync(
            workspaceTarget,
            new RoslynCliQueryOptions { Symbol = "Calculator.Add" },
            TestContext.Current.CancellationToken));

        Assert.Contains("ambiguous", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Demo.Calculator.Add(int, int)", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Demo.Calculator.Add(double, double)", exception.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Overload signature resolves a single symbol")]
    public async Task GetSymbolInfoAsync_OverloadSignature_ResolvesSymbol()
    {
        using var workspaceTarget = CreateWorkspaceTarget(
            "SignatureProject",
            """
            namespace Demo;

            public class Calculator
            {
                public int Add(int left, int right) => left + right;
                public double Add(double left, double right) => left + right;
            }
            """);

        var result = await _sut.GetSymbolInfoAsync(
            workspaceTarget,
            new RoslynCliQueryOptions { Symbol = "Calculator.Add(double, double)" },
            TestContext.Current.CancellationToken);

        Assert.Equal("Demo.Calculator.Add(double, double)", result.FullyQualifiedName);
    }

    [Fact(DisplayName = "Type.Method resolves generic methods without type parameters")]
    public async Task GetSymbolInfoAsync_GenericMethodWithoutTypeParameters_ResolvesSymbol()
    {
        using var workspaceTarget = CreateWorkspaceTarget(
            "GenericProject",
            """
            namespace Demo;

            public class Runner
            {
                public T Run<T>(T value) => value;
            }
            """);

        var result = await _sut.GetSymbolInfoAsync(
            workspaceTarget,
            new RoslynCliQueryOptions { Symbol = "Runner.Run" },
            TestContext.Current.CancellationToken);

        Assert.Equal("Demo.Runner.Run<T>(T)", result.FullyQualifiedName);
    }

    [Fact(DisplayName = "Symbols are found when their namespace also exists in a referenced project")]
    public async Task SearchSymbolsAsync_NamespaceSharedWithReferencedProject_FindsSymbols()
    {
        using var workspaceTarget = CreateReferencingWorkspaceTarget(
            """
            namespace Demo.Core;

            public class OrderService { }
            """,
            """
            namespace Demo.Core.Tests;

            public class OrderServiceTests { }
            """);

        var result = await _sut.SearchSymbolsAsync(
            workspaceTarget,
            new RoslynCliQueryOptions { Symbol = "*Tests", Filter = "class" },
            TestContext.Current.CancellationToken);

        Assert.Contains(result.Symbols, static item => item.FullyQualifiedName == "Demo.Core.Tests.OrderServiceTests");
    }

    [Fact(DisplayName = "MaxResults truncates items but keeps the full total count")]
    public async Task SearchSymbolsAsync_MaxResults_TruncatesItems()
    {
        using var workspaceTarget = CreateWorkspaceTarget(
            "LimitProject",
            """
            namespace Demo;

            public class AlphaService { }
            public class BetaService { }
            public class GammaService { }
            """);

        var result = await _sut.SearchSymbolsAsync(
            workspaceTarget,
            new RoslynCliQueryOptions { Symbol = "*Service", MaxResults = 2 },
            TestContext.Current.CancellationToken);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(2, result.Symbols.Count);
        Assert.True(result.Truncated);
    }

    [Fact(DisplayName = "Column selects the symbol at that position on a line")]
    public async Task GetSymbolInfoAsync_FileLineColumn_ResolvesSymbolAtColumn()
    {
        using var workspaceTarget = CreateWorkspaceTarget(
            "ColumnProject",
            """
            namespace Demo;

            public class Left { } public class Right { }
            """);
        var file = workspaceTarget.PrimaryDocument!.FilePath!;

        var result = await _sut.GetSymbolInfoAsync(
            workspaceTarget,
            new RoslynCliQueryOptions { File = file, Line = 3, Column = 36 },
            TestContext.Current.CancellationToken);

        Assert.Equal("Demo.Right", result.FullyQualifiedName);
    }

    [Fact(DisplayName = "Diagnostics query filters by severity and file")]
    public async Task QueryDiagnosticsAsync_SeverityAndFile_ReturnsMatchingDiagnostics()
    {
        using var workspaceTarget = CreateWorkspaceTarget(
            "DiagnosticsProject",
            """
            namespace Demo;

            public class Broken
            {
                public int Run()
                {
                    int unused;
                    return MissingMethod();
                }
            }
            """);
        var file = workspaceTarget.PrimaryDocument!.FilePath!;

        var errors = await _sut.QueryDiagnosticsAsync(
            workspaceTarget,
            new RoslynCliQueryOptions { File = file },
            "error",
            includeAnalyzers: false,
            TestContext.Current.CancellationToken);
        var all = await _sut.QueryDiagnosticsAsync(
            workspaceTarget,
            new RoslynCliQueryOptions(),
            "all",
            includeAnalyzers: false,
            TestContext.Current.CancellationToken);

        Assert.Contains(errors.Diagnostics, static item => item.Id == "CS0103");
        Assert.All(errors.Diagnostics, static item => Assert.Equal("Error", item.Severity));
        Assert.Contains(all.Diagnostics, static item => item.Id == "CS0168");
        Assert.Equal("Error", all.Diagnostics[0].Severity);
    }

    private const string ReadingSource = """
        namespace Demo;

        public class Pricing
        {
            private readonly int _rate = 2;

            /// <summary>Returns the price for <paramref name="count"/> items using <see cref="Helper"/>.</summary>
            public int Price(int count)
            {
                var helper = new Helper();
                return helper.Scale(count) * _rate + System.Math.Abs(count);
            }
        }

        public class Helper
        {
            public int Scale(int value) => value * 10;
        }
        """;

    [Fact(DisplayName = "Symbol info includes signature, return type, summary, and declaration range")]
    public async Task GetSymbolInfoAsync_Method_ReturnsSignatureAndSummary()
    {
        using var workspaceTarget = CreateWorkspaceTarget("InfoDetailsProject", ReadingSource);

        var result = await _sut.GetSymbolInfoAsync(
            workspaceTarget,
            new RoslynCliQueryOptions { Symbol = "Pricing.Price" },
            TestContext.Current.CancellationToken);

        Assert.Equal("public int Pricing.Price(int count)", result.Signature);
        Assert.Equal("int", result.ReturnType);
        Assert.Equal("Returns the price for count items using Demo.Helper.", result.Summary);
        var declaration = Assert.Single(result.Declarations);
        Assert.Equal(7, declaration.StartLine);
        Assert.Equal(12, declaration.EndLine);
    }

    [Fact(DisplayName = "Symbol source returns the declaration text and honors max lines")]
    public async Task GetSymbolSourceAsync_Method_ReturnsDeclarationSource()
    {
        using var workspaceTarget = CreateWorkspaceTarget("SourceProject", ReadingSource);

        var full = await _sut.GetSymbolSourceAsync(
            workspaceTarget,
            new RoslynCliQueryOptions { Symbol = "Pricing.Price" },
            maxLines: null,
            TestContext.Current.CancellationToken);
        var limited = await _sut.GetSymbolSourceAsync(
            workspaceTarget,
            new RoslynCliQueryOptions { Symbol = "Pricing.Price" },
            maxLines: 2,
            TestContext.Current.CancellationToken);

        var declaration = Assert.Single(full.Declarations);
        Assert.StartsWith("    /// <summary>", declaration.Source, StringComparison.Ordinal);
        Assert.EndsWith("    }", declaration.Source, StringComparison.Ordinal);
        Assert.False(declaration.Truncated);

        var limitedDeclaration = Assert.Single(limited.Declarations);
        Assert.True(limitedDeclaration.Truncated);
        Assert.Equal(2, limitedDeclaration.Source.Split('\n').Length);
        Assert.Equal(12, limitedDeclaration.EndLine);
    }

    [Fact(DisplayName = "Document symbols list types and members with nesting depth")]
    public async Task GetDocumentSymbolsAsync_File_ReturnsOutline()
    {
        using var workspaceTarget = CreateWorkspaceTarget("OutlineProject", ReadingSource);

        var result = await _sut.GetDocumentSymbolsAsync(
            workspaceTarget,
            new RoslynCliQueryOptions { File = workspaceTarget.PrimaryDocument!.FilePath },
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ["Demo", "Pricing", "_rate", "Price(int)", "Helper", "Scale(int)"],
            result.Symbols.Select(static item => item.Symbol));
        Assert.Equal([0, 1, 2, 2, 1, 2], result.Symbols.Select(static item => item.Depth));
        Assert.DoesNotContain(result.Symbols, static item => item.Symbol == "helper");
    }

    [Fact(DisplayName = "Callees list methods and constructors called from a method body")]
    public async Task FindCalleesAsync_Method_ReturnsOutgoingCalls()
    {
        using var workspaceTarget = CreateWorkspaceTarget("CalleesProject", ReadingSource);

        var all = await _sut.FindCalleesAsync(
            workspaceTarget,
            new RoslynCliQueryOptions { Symbol = "Pricing.Price" },
            sourceOnly: false,
            TestContext.Current.CancellationToken);
        var sourceOnly = await _sut.FindCalleesAsync(
            workspaceTarget,
            new RoslynCliQueryOptions { Symbol = "Pricing.Price" },
            sourceOnly: true,
            TestContext.Current.CancellationToken);

        Assert.Contains(all.Callees, static item => item.Symbol == "Demo.Helper.Scale(int)" && item.IsSource && item.CallLines.SequenceEqual([11]));
        Assert.Contains(all.Callees, static item => item.Kind == "Constructor" && item.ContainingType == "Demo.Helper");
        Assert.Contains(all.Callees, static item => item.Symbol.StartsWith("System.Math.Abs", StringComparison.Ordinal) && !item.IsSource);
        Assert.DoesNotContain(sourceOnly.Callees, static item => !item.IsSource);
    }

    private static LoadedWorkspaceTarget CreateReferencingWorkspaceTarget(string librarySource, string testSource)
    {
        var workspace = new AdhocWorkspace();
        var libraryId = ProjectId.CreateNewId("Library");
        var testId = ProjectId.CreateNewId("Library.Tests");
        var directory = Path.GetTempPath();

        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(
                libraryId,
                VersionStamp.Default,
                "Library",
                "Library",
                LanguageNames.CSharp,
                filePath: Path.Combine(directory, "Library.csproj"),
                metadataReferences: DefaultReferences,
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)))
            .AddProject(ProjectInfo.Create(
                testId,
                VersionStamp.Default,
                "Library.Tests",
                "Library.Tests",
                LanguageNames.CSharp,
                filePath: Path.Combine(directory, "Library.Tests.csproj"),
                metadataReferences: DefaultReferences,
                projectReferences: [new ProjectReference(libraryId)],
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)));

        solution = solution
            .AddDocument(DocumentId.CreateNewId(libraryId), "Library.cs", SourceText.From(librarySource), filePath: Path.Combine(directory, "Library.cs"))
            .AddDocument(DocumentId.CreateNewId(testId), "LibraryTests.cs", SourceText.From(testSource), filePath: Path.Combine(directory, "LibraryTests.cs"));

        return new LoadedWorkspaceTarget(
            workspace,
            Path.Combine(directory, "Library.slnx"),
            WorkspaceTargetKind.Solution,
            solution,
            solution.GetProject(libraryId),
            null);
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
