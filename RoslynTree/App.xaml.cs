using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using RoslynLib.Infrastructure;
using RoslynTree.ViewModels;

namespace RoslynTree;

public partial class App : System.Windows.Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Services = BuildServiceProvider();

        var mainWindow = new MainWindow();
        mainWindow.DataContext = Services.GetRequiredService<MainViewModel>();
        mainWindow.Show();
    }

    private static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        // Infrastructure
        services.AddSingleton<RoslynWorkspaceLoader>();
        services.AddSingleton<MSBuildProjectLoader>();
        services.AddSingleton<RoslynSymbolAnalysisService>();

        // ViewModels
        services.AddSingleton<CallTreeViewModel>();
        services.AddSingleton<ReferenceTreeViewModel>();
        services.AddSingleton<MainViewModel>();

        return services.BuildServiceProvider();
    }
}

