using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.CodeAnalysis;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using RoslynLib.Domain.Models;
using RoslynLib.Infrastructure;

namespace RoslynTree.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly MSBuildProjectLoader _projectLoader;
    private LoadedProject? _loadedProject;

    // Current loaded project context
    private Project? _currentProject;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Ready. Open a solution, project, or C# file to begin.";

    [ObservableProperty]
    private string _windowTitle = "RoslynTree";

    [ObservableProperty]
    private string _currentProjectRoot = string.Empty;

    // Browser panel
    [ObservableProperty]
    private ObservableCollection<NodeNamespaceViewModel> _namespaceNodes = [];

    [ObservableProperty]
    private NodeTypeViewModel? _selectedEntry;

    // Tab ViewModels
    public CallTreeViewModel CallTreeVM { get; }
    public ReferenceTreeViewModel ReferenceTreeVM { get; }

    public MainViewModel(
        MSBuildProjectLoader projectLoader,
        CallTreeViewModel callTreeVM,
        ReferenceTreeViewModel referenceTreeVM)
    {
        _projectLoader = projectLoader;
        CallTreeVM = callTreeVM;
        ReferenceTreeVM = referenceTreeVM;
    }

    // ──────────────────────────────────────── Open Project ────────────────────

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private async Task OpenProjectAsync(CancellationToken cancellationToken)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open Solution / Project",
            Filter = "Solution / Project / C# files (*.sln;*.slnx;*.csproj;*.vbproj;*.fsproj;*.cs)|*.sln;*.slnx;*.csproj;*.vbproj;*.fsproj;*.cs",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true) 
            return;

        await LoadFileAsync(dialog.FileName, cancellationToken).ConfigureAwait(true);
    }

    public async Task LoadFileAsync(string path, CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        StatusMessage = $"Loading {System.IO.Path.GetFileName(path)}…";
        NamespaceNodes.Clear();
        ReferenceTreeVM.Clear();
        CallTreeVM.Clear();

        try
        {
            var loaded = await _projectLoader.LoadProjectAsync(path, cancellationToken).ConfigureAwait(true);

            _loadedProject?.Dispose();
            _loadedProject = loaded;

            _currentProject = loaded.Project;
            CurrentProjectRoot = loaded.ProjectRoot;

            NamespaceNodes = new ObservableCollection<NodeNamespaceViewModel>(
                loaded.NamespaceNodes.Select(static node => new NodeNamespaceViewModel(node)));
            WindowTitle = loaded.ProjectCount > 1
                ? $"RoslynTree — {loaded.TargetName} ({loaded.ProjectCount} projects)"
                : $"RoslynTree — {loaded.TargetName}";
            StatusMessage = loaded.ProjectCount > 1
                ? $"Loaded {loaded.Nodes.Count} type(s) across {loaded.ProjectCount} project(s)."
                : $"Loaded {loaded.Nodes.Count} type(s).";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Load cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanOpen() => !IsBusy;

    // Invoked from View when user clicks a symbol entry
    [RelayCommand]
    private void SelectEntry(NodeTypeViewModel? entry)
    {
        if (entry is null || _currentProject is null) 
            return;

        SelectedEntry = entry;

        var typeSymbol = entry.Node.Symbol;

        // Push context to active tab VMs
        ReferenceTreeVM.SetContext(typeSymbol, _currentProject);

        var firstMethod = typeSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(static method => !method.IsImplicitlyDeclared && method.MethodKind == MethodKind.Ordinary)
            .OrderByDescending(static method => method.DeclaredAccessibility == Microsoft.CodeAnalysis.Accessibility.Public)
            .ThenBy(static method => method.Name, StringComparer.Ordinal)
            .FirstOrDefault();

        if (firstMethod is not null)
            CallTreeVM.SetContext(firstMethod, _currentProject);
        else
            CallTreeVM.Clear();

        StatusMessage = $"Selected: {entry.FullName}";
    }

    // Invoked from View when user clicks a member node
    [RelayCommand]
    private void SelectMember(NodeMemberViewModel? member)
    {
        if (member is null || _currentProject is null) 
            return;

        ReferenceTreeVM.SetContext(member.Symbol, _currentProject);

        if (member.Symbol is IMethodSymbol method)
            CallTreeVM.SetContext(method, _currentProject);
        else
            CallTreeVM.Clear();

        StatusMessage = $"Selected: {member.Symbol.ToDisplayString()}";
    }
}
