using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.CodeAnalysis;
using System.Collections.ObjectModel;
using RoslynLib.Infrastructure;

namespace RoslynTree.ViewModels;

public partial class ReferenceTreeViewModel : ObservableObject
{
    private readonly RoslynSymbolAnalysisService _analysisService;

    [ObservableProperty]
    private ObservableCollection<ReferenceTreeNodeViewModel> _references = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Select any symbol from the browser, then click Analyse.";

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
    }

    [RelayCommand(CanExecute = nameof(CanAnalyse))]
    private async Task AnalyseAsync(CancellationToken cancellationToken)
    {
        if (CurrentSymbol is null || CurrentProject is null) return;

        IsBusy = true;
        StatusMessage = $"Finding all references to '{CurrentSymbol.Name}'…";
        References.Clear();

        try
        {
            var nodes = await _analysisService.BuildReferenceTreeAsync(
                CurrentSymbol, CurrentProject.Solution, cancellationToken)
                .ConfigureAwait(true);

            foreach (var node in nodes)
                References.Add(new ReferenceTreeNodeViewModel(node, LoadChildrenAsync));

            var fileGroups = References.SelectMany(r => r.Children).Count();
            StatusMessage = $"{fileGroups} file group(s) found.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Cancelled.";
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
