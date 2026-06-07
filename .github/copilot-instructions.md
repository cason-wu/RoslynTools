這個 Solution 的目的是利用 Roslyn API 分析 .NET Solution, Project, Document 內的 Symbol。

## Solution Structure

- RoslynLib 是專案的核心庫，負責封裝 Roslyn API 的功能，提供服務和 Read Model 給 Presentation Layer 叫用。
- RoslynLib.Tests 是 RoslynLib 的單元測試專案，確保核心功能的正確性。

Presentation Layer：
- RoslynCLI 為 stateless CLI 的應用程式，提供一次性查詢與 `--output mcp` 輸出格式。
- RoslynMcp 為 stateful MCP stdio server，透過 tool 載入 / 查詢 / 釋放 target。
- RoslynTree 為 WPF 的應用程式。

## Key Dependencies

| Package | Purpose |
|---------|---------|
| `Microsoft.CodeAnalysis.Workspaces.MSBuild` | Open solutions/projects and query symbols via Roslyn |
| `Microsoft.CodeAnalysis.CSharp.Workspaces` | C# syntax and semantic model |
| `Microsoft.Build.Locator` | Locate the installed MSBuild at runtime (required before creating `MSBuildWorkspace`) |
| `ModelContextProtocol` | Host the `RoslynMcp` stdio MCP server |
| `CommunityToolkit.Mvvm` | Source-generated `[ObservableProperty]` / `[RelayCommand]` |
| `Microsoft.Extensions.DependencyInjection` | DI container wired up in `App.OnStartup` |

## Architecture Notes

- **SOLID**: each service has a single responsibility; presentation projects keep transport / UI concerns thin and delegate analysis to `RoslynLib`.
- **`MSBuildLocator.RegisterDefaults()`** is called exactly once (thread-safe static guard) before the first `MSBuildWorkspace.Create()`.
- `RoslynWorkspaceTargetSession` owns the loaded-target lifecycle for `RoslynMcp`, so repeated tool calls reuse the same workspace instead of reloading every time.
- `RoslynCLI` and `RoslynMcp` both share `RoslynCliAnalysisService`; only the transport and lifecycle orchestration differ.