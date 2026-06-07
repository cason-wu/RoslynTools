using RoslynLib.Infrastructure;

namespace RoslynLib.Tests.Infrastructure;

public sealed class RoslynWorkspaceTargetSessionTests
{
    [Fact(DisplayName = "Load target keeps the workspace available until released")]
    public async Task LoadTargetAsync_StatusAndRelease_ReturnExpectedState()
    {
        using var sourceFile = new TemporarySourceFile(
            "StatefulSample.cs",
            """
            namespace Demo;

            public class StatefulSample
            {
                public void Run() { }
            }
            """);
        var loader = new RoslynWorkspaceLoader();
        using var session = new RoslynWorkspaceTargetSession(loader);

        var loaded = await session.LoadTargetAsync(sourceFile.FilePath, TestContext.Current.CancellationToken);
        var status = await session.GetStatusAsync(TestContext.Current.CancellationToken);
        var released = await session.ReleaseTargetAsync(TestContext.Current.CancellationToken);
        var emptyStatus = await session.GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.True(loaded.IsLoaded);
        Assert.Equal("Document", loaded.TargetKind);
        Assert.Equal("StatefulSample.cs", loaded.PrimaryDocument);
        Assert.Contains("Subsequent queries will reuse this session", loaded.Message, StringComparison.Ordinal);

        Assert.True(status.IsLoaded);
        Assert.Equal(loaded.TargetPath, status.TargetPath);

        Assert.True(released.HadLoadedTarget);
        Assert.Equal(loaded.TargetPath, released.TargetPath);
        Assert.Equal("Document", released.TargetKind);

        Assert.False(emptyStatus.IsLoaded);
        Assert.Contains("No target is currently loaded", emptyStatus.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Loading the same target twice keeps the existing session")]
    public async Task LoadTargetAsync_SamePath_ReturnsAlreadyLoadedMessage()
    {
        using var sourceFile = new TemporarySourceFile(
            "CachedSample.cs",
            """
            namespace Demo;

            public class CachedSample { }
            """);
        var loader = new RoslynWorkspaceLoader();
        using var session = new RoslynWorkspaceTargetSession(loader);

        var firstLoad = await session.LoadTargetAsync(sourceFile.FilePath, TestContext.Current.CancellationToken);
        var secondLoad = await session.LoadTargetAsync(sourceFile.FilePath, TestContext.Current.CancellationToken);
        var documentName = await session.RunWithLoadedTargetAsync(
            static (loadedTarget, _) => Task.FromResult(loadedTarget.PrimaryDocument?.Name ?? string.Empty),
            TestContext.Current.CancellationToken);

        Assert.Equal(firstLoad.TargetPath, secondLoad.TargetPath);
        Assert.Contains("already loaded", secondLoad.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("CachedSample.cs", documentName);
    }

    [Fact(DisplayName = "Loading a new target replaces the previous session")]
    public async Task LoadTargetAsync_NewPath_ReplacesExistingSession()
    {
        using var firstFile = new TemporarySourceFile(
            "FirstSample.cs",
            """
            namespace Demo;

            public class FirstSample { }
            """);
        using var secondFile = new TemporarySourceFile(
            "SecondSample.cs",
            """
            namespace Demo;

            public class SecondSample { }
            """);
        var loader = new RoslynWorkspaceLoader();
        using var session = new RoslynWorkspaceTargetSession(loader);

        await session.LoadTargetAsync(firstFile.FilePath, TestContext.Current.CancellationToken);
        var secondLoad = await session.LoadTargetAsync(secondFile.FilePath, TestContext.Current.CancellationToken);

        Assert.Contains("replaced the previous session", secondLoad.Message, StringComparison.Ordinal);
        Assert.EndsWith("/SecondSample.cs", secondLoad.TargetPath, StringComparison.Ordinal);
        Assert.Equal("SecondSample.cs", secondLoad.PrimaryDocument);
    }

    [Fact(DisplayName = "Running analysis without a loaded target fails fast")]
    public async Task RunWithLoadedTargetAsync_NoTarget_Throws()
    {
        var loader = new RoslynWorkspaceLoader();
        using var session = new RoslynWorkspaceTargetSession(loader);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await session.RunWithLoadedTargetAsync(
                static (loadedTarget, _) => Task.FromResult(loadedTarget.TargetPath),
                TestContext.Current.CancellationToken));

        Assert.Contains("load_target", exception.Message, StringComparison.Ordinal);
    }

    private sealed class TemporarySourceFile : IDisposable
    {
        private readonly string _directoryPath;

        public TemporarySourceFile(string fileName, string source)
        {
            _directoryPath = Path.Combine(Path.GetTempPath(), "RoslynWorkspaceTargetSessionTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directoryPath);

            FilePath = Path.Combine(_directoryPath, fileName);
            File.WriteAllText(FilePath, source);
        }

        public string FilePath { get; }

        public void Dispose()
        {
            if (Directory.Exists(_directoryPath))
                Directory.Delete(_directoryPath, recursive: true);
        }
    }
}
