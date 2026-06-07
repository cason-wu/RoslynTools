using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.CodeAnalysis;
using System.Collections.ObjectModel;
using RoslynLib.Infrastructure;

namespace RoslynTree.ViewModels;

public partial class ReferenceTreeViewModel : ObservableObject
{
    private readonly RoslynSymbolAnalysisService _analysisService;
    private CancellationTokenSource? _analysisCancellationTokenSource;

    [ObservableProperty]
    private ObservableCollection<ReferenceTreeNodeViewModel> _references = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Select a symbol node to load references automatically.";

    public ISymbol? CurrentSymbol { get; private set; }
    public Project? CurrentProject { get; private set; }

    public ReferenceTreeViewModel(RoslynSymbolAnalysisService analysisService)
    {
        _analysisService = analysisService;
    }

    public void SetContext(ISymbol symbol, Project project)
    {
        CurrentSymbol = symbol;
        CurrentProject = project;
        AnalyseCommand.NotifyCanExecuteChanged();
        StartAutoAnalysis();
    }

    public void Clear()
    {
        CancelPendingAnalysis();
        CurrentSymbol = null;
        CurrentProject = null;
        References.Clear();
        StatusMessage = "Select a symbol node to load references automatically.";
        AnalyseCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanAnalyse))]
    private async Task AnalyseAsync(CancellationToken cancellationToken)
    {
        var currentSymbol = CurrentSymbol;
        var currentProject = CurrentProject;
        if (currentSymbol is null || currentProject is null)
            return;

        IsBusy = true;
        StatusMessage = $"Finding all references to '{currentSymbol.Name}'…";
        References.Clear();

        try
        {
            var nodes = await _analysisService.BuildReferenceTreeAsync(
                currentSymbol, currentProject.Solution, cancellationToken)
                .ConfigureAwait(true);

            foreach (var node in nodes)
            {
                var rootViewModel = new ReferenceTreeNodeViewModel(node, LoadChildrenAsync);
                References.Add(rootViewModel);
                rootViewModel.IsExpanded = true;
            }

            var fileGroups = References.SelectMany(r => r.Children).Count();
            StatusMessage = $"{fileGroups} file group(s) found.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = CurrentSymbol is null
                ? "Select a symbol node to load references automatically."
                : $"Reference load cancelled for '{CurrentSymbol.Name}'.";
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

    private bool CanAnalyse() => CurrentSymbol is not null && CurrentProject is not null && !IsBusy;

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

        if (node is not ReferenceTreeNodeViewModel refVm)
            return [];

        var loaded = await _analysisService.LoadReferenceTreeChildrenAsync(
                refVm.RefTreeModel,
                CurrentProject.Solution,
                cancellationToken)
            .ConfigureAwait(true);

        return loaded.Cast<RoslynLib.Domain.Models.RoslynTreeNode>().ToList().AsReadOnly();
    }
}
