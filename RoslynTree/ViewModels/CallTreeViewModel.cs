using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.CodeAnalysis;
using System.Collections.ObjectModel;
using RoslynLib.Infrastructure;

namespace RoslynTree.ViewModels;

public partial class CallTreeViewModel : ObservableObject
{
    private readonly RoslynSymbolAnalysisService _analysisService;
    private CancellationTokenSource? _analysisCancellationTokenSource;

    [ObservableProperty]
    private ObservableCollection<CallTreeNodeViewModel> _roots = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Select a method node to load callers automatically.";

    public IMethodSymbol? CurrentMethod { get; private set; }
    public Project? CurrentProject { get; private set; }

    public CallTreeViewModel(RoslynSymbolAnalysisService analysisService)
    {
        _analysisService = analysisService;
    }

    public void SetContext(IMethodSymbol method, Project project)
    {
        CurrentMethod = method;
        CurrentProject = project;
        AnalyseCommand.NotifyCanExecuteChanged();
        StartAutoAnalysis();
    }

    public void Clear()
    {
        CancelPendingAnalysis();
        CurrentMethod = null;
        CurrentProject = null;
        Roots.Clear();
        StatusMessage = "Select a method node to load callers automatically.";
        AnalyseCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanAnalyse))]
    private async Task AnalyseAsync(CancellationToken cancellationToken)
    {
        var currentMethod = CurrentMethod;
        var currentProject = CurrentProject;
        if (currentMethod is null || currentProject is null)
            return;

        IsBusy = true;
        StatusMessage = $"Building call tree for {currentMethod.Name}…";
        Roots.Clear();

        try
        {
            var nodes = await _analysisService.BuildCallTreeAsync(
                currentMethod, currentProject.Solution, cancellationToken)
                .ConfigureAwait(true);

            foreach (var node in nodes)
            {
                var rootViewModel = new CallTreeNodeViewModel(node, LoadChildrenAsync);
                Roots.Add(rootViewModel);
                rootViewModel.IsExpanded = true;
            }

            StatusMessage = $"{Roots.Count} root(s) found.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = CurrentMethod is null
                ? "Select a method node to load callers automatically."
                : $"Caller load cancelled for {CurrentMethod.Name}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanAnalyse() => CurrentMethod is not null && CurrentProject is not null && !IsBusy;

    private void StartAutoAnalysis()
    {
        CancelPendingAnalysis();
        _analysisCancellationTokenSource = new CancellationTokenSource();
        _ = AnalyseAsync(_analysisCancellationTokenSource.Token);
    }

    private void CancelPendingAnalysis()
    {
        _analysisCancellationTokenSource?.Cancel();
        _analysisCancellationTokenSource?.Dispose();
        _analysisCancellationTokenSource = null;
    }

    private async Task<IReadOnlyList<RoslynLib.Domain.Models.RoslynTreeNode>> LoadChildrenAsync(
        SymbolTreeNodeViewModel node,
        CancellationToken cancellationToken)
    {
        if (CurrentProject is null)
            return [];

        if (node is not CallTreeNodeViewModel callVm)
            return [];

        var pathKeys = callVm.GetPathKeysFromRoot();
        var loaded = await _analysisService.LoadCallTreeChildrenAsync(
                callVm.CallTreeModel,
                CurrentProject.Solution,
                pathKeys,
                cancellationToken)
            .ConfigureAwait(true);

        return loaded.Cast<RoslynLib.Domain.Models.RoslynTreeNode>().ToList().AsReadOnly();
    }
}
