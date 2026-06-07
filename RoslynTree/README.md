# RoslynTree

A WPF desktop tool for visualising .NET symbol relationships as interactive trees — powered by the **Roslyn** compiler API.

## Features

| Tab | Description |
|-----|-------------|
| **Call Tree** | Shows all callers of a selected method, recursively up to a configurable depth |
| **References** | Finds every reference to a selected symbol across the solution, with file name, line number, and source line preview |


## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Visual Studio 2022 (or MSBuild 17+) installed — required by `MSBuildWorkspace` to locate project toolchains at runtime

## Getting Started

1. Click **📂 Open Project** in the toolbar.
2. Browse to a `.csproj` / `.vbproj` / `.fsproj` file and open it.
3. The **Solution Browser** (left panel) will populate with namespaces and types from the selected project.
4. Click any type or method to set the active symbol.
5. Switch to the desired tab and click its **▶ Analyse** button.

## Key Dependencies

| Package | Purpose |
|---------|---------|
| `CommunityToolkit.Mvvm` | Source-generated `[ObservableProperty]` / `[RelayCommand]` |
| `Microsoft.Extensions.DependencyInjection` | DI container wired up in `App.OnStartup` |

## Architecture Notes

- **SOLID**: each service has a single responsibility; ViewModels receive `ISymbolAnalysisService` / `ISolutionLoader` via constructor injection.
- **Async UI**: all Roslyn calls are `async`/`await`; a `BusyIndicator` progress bar is shown during analysis.
- **UI virtualisation**: all `TreeView` and `ListView` controls use `VirtualizingStackPanel` with `Recycling` mode for large solutions.
