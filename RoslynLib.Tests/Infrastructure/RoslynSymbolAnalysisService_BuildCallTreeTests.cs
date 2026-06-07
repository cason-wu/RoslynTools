using Microsoft.CodeAnalysis;
using RoslynLib.Domain.Models;
using RoslynLib.Infrastructure;
using RoslynLib.Tests.Helpers;

namespace RoslynLib.Tests.Infrastructure;

public class RoslynSymbolAnalysisService_BuildCallTreeAsync
{
    private readonly RoslynSymbolAnalysisService _sut = new();

    /// <summary>
    /// Returns the IMethodSymbol named <paramref name="methodName"/> from the first project compilation.
    /// </summary>
    private static async Task<(IMethodSymbol Method, Solution Solution)> ResolveMethodAsync(
        string source, string typeName, string methodName)
    {
        var solution = RoslynTestHelper.CreateSolution(source);
        var compilation = (await solution.Projects.First().GetCompilationAsync())!;
        var type = RoslynTestHelper.GetTypeSymbol(compilation, typeName);
        var method = type.GetMembers(methodName).OfType<IMethodSymbol>().First();
        return (method, solution);
    }

    [Fact(DisplayName = "Root node corresponds to the analysed method")]
    public async Task BuildCallTreeAsync_RootNameMatchesMethod()
    {
        // Setup
        const string source = """
            public class MyClass
            {
                public void Target() { }
            }
            """;

        var (method, solution) = await ResolveMethodAsync(source, "MyClass", "Target");

        // Execute
        var result = await _sut.BuildCallTreeAsync(method, solution);
        var root = result[0];

        // Verify
        Assert.Equal("Target", root.Name);
    }

    [Fact(DisplayName = "A method with no callers has no children")]
    public async Task BuildCallTreeAsync_NoCallers_EmptyChildren()
    {
        // Setup — Target is never called anywhere in this tiny solution
        const string source = """
            public class MyClass
            {
                public void Target() { }
            }
            """;

        var (method, solution) = await ResolveMethodAsync(source, "MyClass", "Target");

        // Execute
        var result = await _sut.BuildCallTreeAsync(method, solution);
        var root = result[0];

        // Verify
        Assert.Empty(root.Children);
    }

    [Fact(DisplayName = "Initial load only includes direct callers")]
    public async Task BuildCallTreeAsync_InitialLoad_OnlyDirectCallers()
    {
        // Setup
        const string source = """
            public class MyClass
            {
                public void Target() { }
                public void Caller1() => Target();
                public void Caller2() => Caller1();
            }
            """;

        var (method, solution) = await ResolveMethodAsync(source, "MyClass", "Target");

        // Execute
        var result = await _sut.BuildCallTreeAsync(method, solution);
        var root = result[0];

        // Verify — only direct caller in initial tree
        var caller1 = Assert.Single(root.Children);
        Assert.Equal("Caller1", caller1.Name);

        // Verify — deeper caller not loaded until expand
        Assert.Empty(caller1.Children);
        Assert.True(caller1.HasUnloadedChildren);

        // Expand Caller1
        var caller1Node = Assert.IsType<CallTreeNode>(caller1);
        await _sut.LoadCallTreeChildrenAsync(caller1Node, solution, [root.FullName, caller1Node.FullName]);

        Assert.Contains(caller1Node.Children, c => c.Name == "Caller2");
    }

    [Fact(DisplayName = "Recursive method does not cause infinite recursion")]
    public async Task BuildCallTreeAsync_RecursiveMethod_DoesNotInfiniteLoop()
    {
        // Setup — Fibonacci calls itself
        const string source = """
            public class Math
            {
                public int Fib(int n) => n <= 1 ? n : Fib(n - 1) + Fib(n - 2);
            }
            """;

        var (method, solution) = await ResolveMethodAsync(source, "Math", "Fib");

        // Execute — should complete without stack-overflow
        var result = await _sut.BuildCallTreeAsync(method, solution);
        var root = result[0];

        // Verify — root is Fib; the visited guard prevents infinite recursion.
        // The recursive self-reference may appear as a stub child (with no deeper children).
        Assert.Equal("Fib", root.Name);
        var selfRef = root.Children.FirstOrDefault(c => c.Name == "Fib");
        if (selfRef is not null)
            Assert.Empty(selfRef.Children); // stub only — no deeper recursion
    }

    [Fact(DisplayName = "Caller is listed as a direct child node")]
    public async Task BuildCallTreeAsync_OneCaller_AppearsAsChild()
    {
        // Setup
        const string source = """
            public class Service
            {
                public void RunAll() => Run();
                public void Run() { }
            }
            """;

        var (method, solution) = await ResolveMethodAsync(source, "Service", "Run");

        // Execute
        var result = await _sut.BuildCallTreeAsync(method, solution);
        var root = result[0];

        // Verify
        Assert.Contains(root.Children, c => c.Name == "RunAll");
    }

    [Fact(DisplayName = "No duplicate children for the same caller")]
    public async Task BuildCallTreeAsync_SameCallerMultipleCallSites_DeduplicatesChild()
    {
        // Setup — SingleCaller calls Target twice from the same method
        const string source = """
            public class Service
            {
                public void SingleCaller()
                {
                    Target();
                    Target();
                }
                public void Target() { }
            }
            """;

        var (method, solution) = await ResolveMethodAsync(source, "Service", "Target");

        // Execute
        var result = await _sut.BuildCallTreeAsync(method, solution);
        var root = result[0];

        // Verify — should appear exactly once even though there are 2 call sites
        var callerNodes = root.Children.Where(c => c.Name == "SingleCaller").ToList();
        Assert.Single(callerNodes);
    }
}
