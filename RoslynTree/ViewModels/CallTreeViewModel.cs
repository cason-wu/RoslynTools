using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.CodeAnalysis;
using System.Collections.ObjectModel;
using RoslynLib.Infrastructure;

namespace RoslynTree.ViewModels;

public partial class CallTreeViewModel : ObservableObject
{
    private readonly RoslynSymbolAnalysisService _analysisService;

    [ObservableProperty]
    private ObservableCollection<CallTreeNodeViewModel> _roots = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Select a method from the browser, then click Analyse.";

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
    }

    [RelayCommand(CanExecute = nameof(CanAnalyse))]
    private async Task AnalyseAsync(CancellationToken cancellationToken)
    {
        if (CurrentMethod is null || CurrentProject is null) return;

        IsBusy = true;
        StatusMessage = $"Building call tree for {CurrentMethod.Name}…";
        Roots.Clear();

        try
        {
            var nodes = await _analysisService.BuildCallTreeAsync(
                CurrentMethod, CurrentProject.Solution, cancellationToken)
                .ConfigureAwait(true);

            foreach (var node in nodes)
                Roots.Add(new CallTreeNodeViewModel(node, LoadChildrenAsync));

            StatusMessage = $"{Roots.Count} root(s) found.";
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

    private bool CanAnalyse() => CurrentMethod is not null && CurrentProject is not null && !IsBusy;

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
