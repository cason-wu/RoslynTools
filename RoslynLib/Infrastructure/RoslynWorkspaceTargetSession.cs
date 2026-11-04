using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
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
    IReadOnlyList<string> Projects,
    DateTime? LoadedAtUtc,
    DateTime? LastSyncedAtUtc,
    string LastSyncSummary,
    IReadOnlyList<string> LoadDiagnostics,
    int LoadDiagnosticCount,
    string Message);

public sealed record WorkspaceSessionReleaseView(
    bool HadLoadedTarget,
    string TargetPath,
    string TargetKind,
    int ProjectCount,
    string Message);

public sealed class RoslynWorkspaceTargetSession : IDisposable
{
    private const int MaxReportedLoadDiagnostics = 20;

    private readonly RoslynWorkspaceLoader _workspaceLoader;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LoadedWorkspaceTarget? _loadedTarget;
    private WorkspaceFileSnapshot? _snapshot;
    private DateTime? _lastSyncedAtUtc;
    private string _lastSyncSummary = string.Empty;

    public RoslynWorkspaceTargetSession(RoslynWorkspaceLoader workspaceLoader)
    {
        _workspaceLoader = workspaceLoader ?? throw new ArgumentNullException(nameof(workspaceLoader));
    }

    public Task<WorkspaceSessionStatusView> LoadTargetAsync(
        string targetPath,
        CancellationToken cancellationToken = default)
        => LoadTargetAsync(targetPath, forceReload: false, cancellationToken);

    public async Task<WorkspaceSessionStatusView> LoadTargetAsync(
        string targetPath,
        bool forceReload,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            throw new ArgumentException("Target path is required.", nameof(targetPath));

        var normalizedPath = Path.GetFullPath(targetPath);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var isSameTarget = _loadedTarget is not null
                && string.Equals(_loadedTarget.TargetPath, normalizedPath, StringComparison.OrdinalIgnoreCase);

            if (isSameTarget && !forceReload)
            {
                await SyncChangesCoreAsync(cancellationToken).ConfigureAwait(false);
                return CreateStatusView(_loadedTarget!, "Target is already loaded; file changes on disk are synchronized automatically before each query. Pass forceReload to rebuild the workspace from scratch.");
            }

            var hadPreviousTarget = _loadedTarget is not null;
            await ReplaceTargetAsync(normalizedPath, cancellationToken).ConfigureAwait(false);
            _lastSyncSummary = "Loaded.";

            var message = isSameTarget
                ? "Target reloaded from disk."
                : hadPreviousTarget
                    ? "Target reloaded and replaced the previous session."
                    : "Target loaded. Subsequent queries will reuse this session until release_target is called.";

            return CreateStatusView(_loadedTarget!, message);
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
            _snapshot = null;
            _lastSyncedAtUtc = null;
            _lastSyncSummary = string.Empty;
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

            await SyncChangesCoreAsync(cancellationToken).ConfigureAwait(false);
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
            _snapshot = null;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async Task ReplaceTargetAsync(string targetPath, CancellationToken cancellationToken)
    {
        var nextTarget = await _workspaceLoader.LoadAsync(targetPath, cancellationToken).ConfigureAwait(false);
        var previousTarget = _loadedTarget;
        _loadedTarget = nextTarget;
        _snapshot = WorkspaceFileSnapshot.Capture(nextTarget);
        _lastSyncedAtUtc = DateTime.UtcNow;
        previousTarget?.Dispose();
    }

    // Caller must hold _gate.
    private async Task SyncChangesCoreAsync(CancellationToken cancellationToken)
    {
        if (_loadedTarget is null || _snapshot is null)
            return;

        var changes = _snapshot.DetectChanges();
        if (!changes.HasChanges)
            return;

        if (changes.RequiresReload)
        {
            try
            {
                await ReplaceTargetAsync(_loadedTarget.TargetPath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException(
                    $"Detected {changes.ReloadReason}, but reloading the target failed: {ex.Message}",
                    ex);
            }

            _lastSyncSummary = $"Reloaded workspace ({changes.ReloadReason}).";
            return;
        }

        var solution = _loadedTarget.Solution;
        var updatedCount = 0;

        foreach (var path in changes.ChangedDocumentPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stamp = FileStamp.Read(path);
            SourceText text;
            try
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                text = SourceText.From(stream);
            }
            catch (IOException)
            {
                // The file may be mid-write; keep the old stamp so the next query retries.
                continue;
            }

            foreach (var documentId in solution.GetDocumentIdsWithFilePath(path))
                solution = solution.WithDocumentText(documentId, text, PreservationMode.PreserveIdentity);

            _snapshot.MarkDocumentSynced(path, stamp);
            updatedCount++;
        }

        if (updatedCount == 0)
            return;

        _loadedTarget.ReplaceSolution(solution);
        _lastSyncedAtUtc = DateTime.UtcNow;
        _lastSyncSummary = $"Updated {updatedCount} changed document(s).";
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
            [],
            null,
            null,
            string.Empty,
            [],
            0,
            message);
    }

    private WorkspaceSessionStatusView CreateStatusView(
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
            loadedTarget.Projects
                .Select(static project => project.Name)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToList()
                .AsReadOnly(),
            loadedTarget.LoadedAtUtc,
            _lastSyncedAtUtc,
            _lastSyncSummary,
            loadedTarget.LoadDiagnostics.Take(MaxReportedLoadDiagnostics).ToList().AsReadOnly(),
            loadedTarget.LoadDiagnostics.Count,
            message);
    }

    private static string NormalizePath(string path)
        => string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Replace('\\', '/');
}
