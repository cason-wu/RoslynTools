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

    // Current loaded project context
    private Project? _currentProject;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Ready. Open a project file to begin.";

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
            Title = "Open Project",
            Filter = "Project files (*.csproj;*.vbproj;*.fsproj)|*.csproj;*.vbproj;*.fsproj",
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

        try
        {
            var loaded = await _projectLoader.LoadProjectAsync(path, cancellationToken).ConfigureAwait(true);

            _currentProject = loaded.Project;
            CurrentProjectRoot = loaded.ProjectRoot;

            PopulateBrowser(loaded.Nodes);
            WindowTitle = $"RoslynTree — {System.IO.Path.GetFileName(path)}";
            StatusMessage = $"Loaded {loaded.Nodes.Count} type(s).";
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

    // ──────────────────────────────────────── Browser ─────────────────────────

    private void PopulateBrowser(IReadOnlyList<ProjectNode> nodes)
    {
        var byNamespace = nodes
            .GroupBy(s => string.IsNullOrEmpty(s.Namespace) ? "<global>" : s.Namespace)
            .OrderBy(g => g.Key);

        foreach (var nsGroup in byNamespace)
        {
            var nsNode = new NodeNamespaceViewModel(nsGroup.Key);
            
            foreach (var entry in nsGroup.OrderBy(e => e.Name).ThenBy(e => e.FullName))
                nsNode.Types.Add(new NodeTypeViewModel(entry));
            
            NamespaceNodes.Add(nsNode);
        }
    }

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

        // For call tree, set first public method as default if available
        var firstMethod = typeSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m => !m.IsImplicitlyDeclared);

        if (firstMethod is not null)
            CallTreeVM.SetContext(firstMethod, _currentProject);

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

        StatusMessage = $"Selected: {member.Symbol.ToDisplayString()}";
    }
}
