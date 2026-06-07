這個 Solution 的目的是利用 Roslyn API 分析 .NET Solution, Project, Document 內的 Symbol。

## Solution Structure

- RoslynLib 是專案的核心庫，負責封裝 Roslyn API 的功能，提供服務和 ViewModel 給 Presentation Layer 使用。
- RoslynLib.Tests 是 RoslynLib 的單元測試專案，確保核心功能的正確性。

Presentation Layer：
- RoslynCLI  為 CLI 的應用程式
- RoslynTree 為 WPF 的應用程式 

## Key Dependencies

| Package | Purpose |
|---------|---------|
| `Microsoft.CodeAnalysis.Workspaces.MSBuild` | Open solutions/projects and query symbols via Roslyn |
| `Microsoft.CodeAnalysis.CSharp.Workspaces` | C# syntax and semantic model |
| `Microsoft.Build.Locator` | Locate the installed MSBuild at runtime (required before creating `MSBuildWorkspace`) |
| `CommunityToolkit.Mvvm` | Source-generated `[ObservableProperty]` / `[RelayCommand]` |
| `Microsoft.Extensions.DependencyInjection` | DI container wired up in `App.OnStartup` |

## Architecture Notes

- **SOLID**: each service has a single responsibility; ViewModels receive `ISymbolAnalysisService` / `ISolutionLoader` via constructor injection.
- **`MSBuildLocator.RegisterDefaults()`** is called exactly once (thread-safe static guard) before the first `MSBuildWorkspace.Create()`.