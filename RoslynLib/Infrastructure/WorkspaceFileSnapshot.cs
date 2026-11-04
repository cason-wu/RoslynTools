using RoslynLib.Domain.Models;

namespace RoslynLib.Infrastructure;

internal sealed record WorkspaceChangeSet(
    bool RequiresReload,
    string ReloadReason,
    IReadOnlyList<string> ChangedDocumentPaths)
{
    public static WorkspaceChangeSet None { get; } = new(false, string.Empty, []);

    public static WorkspaceChangeSet Reload(string reason) => new(true, reason, []);

    public bool HasChanges => RequiresReload || ChangedDocumentPaths.Count > 0;
}

// Captures file timestamps of a loaded target so the session can detect edits made after loading.
internal sealed class WorkspaceFileSnapshot
{
    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", ".vs", ".idea", "node_modules",
    };

    private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".vb",
    };

    private static readonly string[] BuildFileNames =
    [
        "Directory.Build.props",
        "Directory.Build.targets",
        "Directory.Packages.props",
        "global.json",
    ];

    private readonly Dictionary<string, FileStamp?> _documentStamps;
    private readonly Dictionary<string, FileStamp?> _buildFileStamps;
    private readonly IReadOnlyList<string> _scanDirectories;
    private readonly HashSet<string> _sourceFilesOnDisk;

    private WorkspaceFileSnapshot(
        Dictionary<string, FileStamp?> documentStamps,
        Dictionary<string, FileStamp?> buildFileStamps,
        IReadOnlyList<string> scanDirectories,
        HashSet<string> sourceFilesOnDisk)
    {
        _documentStamps = documentStamps;
        _buildFileStamps = buildFileStamps;
        _scanDirectories = scanDirectories;
        _sourceFilesOnDisk = sourceFilesOnDisk;
    }

    public static WorkspaceFileSnapshot Capture(LoadedWorkspaceTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var documentStamps = new Dictionary<string, FileStamp?>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in target.Projects.SelectMany(static project => project.Documents))
        {
            if (string.IsNullOrWhiteSpace(document.FilePath))
                continue;

            var path = Path.GetFullPath(document.FilePath);
            if (File.Exists(path))
                documentStamps[path] = FileStamp.Read(path);
        }

        if (target.TargetKind == WorkspaceTargetKind.Document)
        {
            return new WorkspaceFileSnapshot(
                documentStamps,
                new Dictionary<string, FileStamp?>(StringComparer.OrdinalIgnoreCase),
                [],
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        var scanDirectories = CollectScanDirectories(target);
        var buildFileStamps = CollectBuildFiles(target, scanDirectories)
            .ToDictionary(static path => path, FileStamp.Read, StringComparer.OrdinalIgnoreCase);

        return new WorkspaceFileSnapshot(
            documentStamps,
            buildFileStamps,
            scanDirectories,
            EnumerateSourceFiles(scanDirectories));
    }

    public WorkspaceChangeSet DetectChanges()
    {
        foreach (var (path, stamp) in _buildFileStamps)
        {
            if (FileStamp.Read(path) != stamp)
                return WorkspaceChangeSet.Reload($"build file changed: {path}");
        }

        if (_scanDirectories.Count > 0)
        {
            var currentSourceFiles = EnumerateSourceFiles(_scanDirectories);
            if (!currentSourceFiles.SetEquals(_sourceFilesOnDisk))
            {
                var added = currentSourceFiles.Except(_sourceFilesOnDisk, StringComparer.OrdinalIgnoreCase).Count();
                var removed = _sourceFilesOnDisk.Except(currentSourceFiles, StringComparer.OrdinalIgnoreCase).Count();
                return WorkspaceChangeSet.Reload($"source files added ({added}) or removed ({removed})");
            }
        }

        var changedDocuments = new List<string>();
        foreach (var (path, stamp) in _documentStamps)
        {
            var current = FileStamp.Read(path);
            if (current is null)
                return WorkspaceChangeSet.Reload($"document removed: {path}");

            if (current != stamp)
                changedDocuments.Add(path);
        }

        return changedDocuments.Count == 0
            ? WorkspaceChangeSet.None
            : new WorkspaceChangeSet(false, string.Empty, changedDocuments.AsReadOnly());
    }

    public void MarkDocumentSynced(string path, FileStamp? stamp)
        => _documentStamps[path] = stamp;

    private static IReadOnlyList<string> CollectScanDirectories(LoadedWorkspaceTarget target)
    {
        var directories = target.Projects
            .Select(static project => project.FilePath)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => Path.GetDirectoryName(Path.GetFullPath(path!)))
            .Where(static directory => !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            .Select(static directory => EnsureTrailingSeparator(directory!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static directory => directory.Length)
            .ToList();

        // Drop directories nested inside another scanned directory to avoid scanning twice.
        var roots = new List<string>();
        foreach (var directory in directories)
        {
            if (!roots.Any(root => directory.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
                roots.Add(directory);
        }

        return roots.AsReadOnly();
    }

    private static IEnumerable<string> CollectBuildFiles(
        LoadedWorkspaceTarget target,
        IReadOnlyList<string> scanDirectories)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(target.TargetPath),
        };

        foreach (var project in target.Projects)
        {
            if (!string.IsNullOrWhiteSpace(project.FilePath))
                files.Add(Path.GetFullPath(project.FilePath));
        }

        var visitedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var start in scanDirectories.Append(EnsureTrailingSeparator(target.TargetDirectory)))
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null && visitedDirectories.Add(directory.FullName))
            {
                // Track missing files too, so creating one later triggers a reload.
                foreach (var fileName in BuildFileNames)
                    files.Add(Path.Combine(directory.FullName, fileName));

                directory = directory.Parent;
            }
        }

        return files;
    }

    private static HashSet<string> EnumerateSourceFiles(IReadOnlyList<string> directories)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>(directories);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            try
            {
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    if (SourceExtensions.Contains(Path.GetExtension(file)))
                        files.Add(Path.GetFullPath(file));
                }

                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    if (!IgnoredDirectoryNames.Contains(Path.GetFileName(child)))
                        pending.Push(child);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Skip directories that disappear or cannot be read while scanning.
            }
        }

        return files;
    }

    private static string EnsureTrailingSeparator(string directory)
        => Path.EndsInDirectorySeparator(directory)
            ? directory
            : directory + Path.DirectorySeparatorChar;
}

internal sealed record FileStamp(DateTime LastWriteTimeUtc, long Length)
{
    public static FileStamp? Read(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists
                ? new FileStamp(info.LastWriteTimeUtc, info.Length)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
