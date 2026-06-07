using RoslynLib.Domain.Models;

namespace RoslynLib.Infrastructure;

public sealed record WorkspaceSessionStatusView(
    bool IsLoaded,
    string TargetPath,
    string TargetKind,
    string TargetDirectory,
    string PrimaryProject,
    string PrimaryDocument,
    int ProjectCount,
    string Message);

public sealed record WorkspaceSessionReleaseView(
    bool HadLoadedTarget,
    string TargetPath,
    string TargetKind,
    int ProjectCount,
    string Message);

public sealed class RoslynWorkspaceTargetSession : IDisposable
{
    private readonly RoslynWorkspaceLoader _workspaceLoader;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LoadedWorkspaceTarget? _loadedTarget;

    public RoslynWorkspaceTargetSession(RoslynWorkspaceLoader workspaceLoader)
    {
        _workspaceLoader = workspaceLoader ?? throw new ArgumentNullException(nameof(workspaceLoader));
    }

    public async Task<WorkspaceSessionStatusView> LoadTargetAsync(
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            throw new ArgumentException("Target path is required.", nameof(targetPath));

        var normalizedPath = Path.GetFullPath(targetPath);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_loadedTarget is not null
                && string.Equals(_loadedTarget.TargetPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                return CreateStatusView(_loadedTarget, "Target is already loaded. Use release_target first if you need a fresh reload.");
            }

            var nextTarget = await _workspaceLoader.LoadAsync(normalizedPath, cancellationToken).ConfigureAwait(false);
            var previousTarget = _loadedTarget;
            _loadedTarget = nextTarget;
            previousTarget?.Dispose();

            return CreateStatusView(
                nextTarget,
                previousTarget is null
                    ? "Target loaded. Subsequent queries will reuse this session until release_target is called."
                    : "Target reloaded and replaced the previous session.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WorkspaceSessionReleaseView> ReleaseTargetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_loadedTarget is null)
            {
                return new WorkspaceSessionReleaseView(
                    false,
                    string.Empty,
                    string.Empty,
                    0,
                    "No target is currently loaded.");
            }

            var releasedTarget = _loadedTarget;
            _loadedTarget = null;
            releasedTarget.Dispose();

            return new WorkspaceSessionReleaseView(
                true,
                NormalizePath(releasedTarget.TargetPath),
                releasedTarget.TargetKind.ToString(),
                releasedTarget.Projects.Count,
                "Target released.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WorkspaceSessionStatusView> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _loadedTarget is null
                ? CreateEmptyStatusView("No target is currently loaded.")
                : CreateStatusView(_loadedTarget, "Target is loaded and ready for queries.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TResult> RunWithLoadedTargetAsync<TResult>(
        Func<LoadedWorkspaceTarget, CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_loadedTarget is null)
                throw new InvalidOperationException("No target is loaded. Call load_target before running analysis tools.");

            return await action(_loadedTarget, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Wait();
        try
        {
            _loadedTarget?.Dispose();
            _loadedTarget = null;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private static WorkspaceSessionStatusView CreateEmptyStatusView(string message)
    {
        return new WorkspaceSessionStatusView(
            false,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            0,
            message);
    }

    private static WorkspaceSessionStatusView CreateStatusView(
        LoadedWorkspaceTarget loadedTarget,
        string message)
    {
        return new WorkspaceSessionStatusView(
            true,
            NormalizePath(loadedTarget.TargetPath),
            loadedTarget.TargetKind.ToString(),
            NormalizePath(loadedTarget.TargetDirectory),
            loadedTarget.PrimaryProject?.Name ?? string.Empty,
            loadedTarget.PrimaryDocument?.Name ?? string.Empty,
            loadedTarget.Projects.Count,
            message);
    }

    private static string NormalizePath(string path)
        => string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Replace('\\', '/');
}
