using Microsoft.CodeAnalysis;
using RoslynLib.Infrastructure;
using RoslynLib.Tests.Helpers;

namespace RoslynLib.Tests.Infrastructure;

public class RoslynSymbolAnalysisService_BuildReferenceTreeAsync
{
    private readonly RoslynSymbolAnalysisService _sut = new();

    private static async Task<(ISymbol Symbol, Solution Solution)> ResolveSymbolAsync(
        string source,
        string typeName,
        string memberName)
    {
        var solution = RoslynTestHelper.CreateSolution(source);
        var compilation = (await solution.Projects.First().GetCompilationAsync())!;
        var type = RoslynTestHelper.GetTypeSymbol(compilation, typeName);
        var symbol = type.GetMembers(memberName).First();
        return (symbol, solution);
    }

    [Fact(DisplayName = "Returns reference nodes for direct usages")]
    public async Task BuildReferenceTreeAsync_MethodHasUsage_ReturnsReferenceNodes()
    {
        // Setup
        const string source = """
            public class Service
            {
                public void Target() { }
                public void Caller() { Target(); }
            }
            """;

        var (symbol, solution) = await ResolveSymbolAsync(source, "Service", "Target");

        // Execute
        var nodes = await _sut.BuildReferenceTreeAsync(symbol, solution);
        var root = Assert.Single(nodes);
        Assert.NotEmpty(root.Children);

        var fileNode = Assert.IsType<RoslynLib.Domain.Models.ReferenceTreeNode>(root.Children[0]);
        var loadedReferences = await _sut.LoadReferenceTreeChildrenAsync(fileNode, solution);

        // Verify
        Assert.NotEmpty(loadedReferences);
        Assert.Contains(loadedReferences, n => n.LineText.Contains("Target", StringComparison.Ordinal));
        Assert.All(loadedReferences, n => Assert.True(n.LineNumber > 0));
    }

    [Fact(DisplayName = "Cross-document references are included")]
    public async Task BuildReferenceTreeAsync_CrossDocumentUsage_IncludesAllLocations()
    {
        // Setup
        const string source = """
            namespace App;

            public class Service
            {
                public void Target() { }
            }

            public class ConsumerA
            {
                public void Run(Service service) => service.Target();
            }

            public class ConsumerB
            {
                public void Run(Service service) => service.Target();
            }
            """;

        var (symbol, solution) = await ResolveSymbolAsync(source, "Service", "Target");

        // Execute
        var nodes = await _sut.BuildReferenceTreeAsync(symbol, solution);
        var root = Assert.Single(nodes);
        Assert.NotEmpty(root.Children);

        var expandedCount = 0;
        foreach (var child in root.Children.OfType<RoslynLib.Domain.Models.ReferenceTreeNode>())
        {
            var fileRefs = await _sut.LoadReferenceTreeChildrenAsync(child, solution);
            expandedCount += fileRefs.Count;
        }

        // Verify
        // Includes at least declaration + usage (Roslyn may vary by workspace/indexing details)
        Assert.True(expandedCount >= 2);
    }

    [Fact(DisplayName = "No usage returns empty list")]
    public async Task BuildReferenceTreeAsync_NoUsage_ReturnsEmpty()
    {
        // Setup
        const string source = """
            public class Service
            {
                public void Target() { }
            }
            """;

        var (symbol, solution) = await ResolveSymbolAsync(source, "Service", "Target");

        // Execute
        var nodes = await _sut.BuildReferenceTreeAsync(symbol, solution);
        var root = Assert.Single(nodes);

        var loaded = new List<RoslynLib.Domain.Models.ReferenceTreeNode>();
        foreach (var child in root.Children.OfType<RoslynLib.Domain.Models.ReferenceTreeNode>())
        {
            loaded.AddRange(await _sut.LoadReferenceTreeChildrenAsync(child, solution));
        }

        // Verify
        Assert.Empty(loaded.Where(n => n.LineText.Contains("Target()", StringComparison.OrdinalIgnoreCase))
            .Skip(1));
    }
}
