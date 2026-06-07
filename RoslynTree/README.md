# RoslynTree

以 **Roslyn** compiler API 為核心的 WPF 桌面工具，用來瀏覽 .NET solution / project / document 內的 symbol 結構、呼叫關係與參考樹。

## Features

| 區塊 | 說明 |
|-----|------|
| **Solution Browser** | 讀取 namespace / type / member 結構，支援多 project target |
| **Call Tree** | 選取 method 後自動載入 caller tree，並可逐層展開更多節點 |
| **References** | 選取 symbol 後自動載入 reference tree，顯示檔案與來源片段 |
| **Shared loader** | 與 `RoslynCLI` / `RoslynMcp` 共用 `RoslynLib` 的 workspace loading 與分析核心 |

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Visual Studio 2022（或 MSBuild 17+）— `MSBuildWorkspace` 需要用來定位專案工具鏈

## Supported targets

- `.sln`
- `.slnx`
- `.csproj`
- `.vbproj`
- `.fsproj`
- `.cs`

## Getting Started

1. Click **📂 Open Project** in the toolbar.
2. Browse to a supported target file and open it.
3. The left-side browser will populate with namespaces, types, and members from the loaded target.
4. Click a type node to update the current symbol context.
5. Click a member node to focus that symbol directly.
6. The **Call Tree** and **References** tabs will automatically start analysis when a valid context is selected.
7. Expand tree nodes to lazy-load deeper caller / reference branches.

## Key Dependencies

| Package | Purpose |
|---------|---------|
| `CommunityToolkit.Mvvm` | Source-generated `[ObservableProperty]` / `[RelayCommand]` |
| `Microsoft.Extensions.DependencyInjection` | DI container wired up in `App.OnStartup` |
| `Microsoft.CodeAnalysis.Workspaces.MSBuild` | Load solutions / projects and query symbols |

## Architecture Notes

- `RoslynTree` 只保留 WPF-specific UI 與 ViewModel 協調邏輯。
- workspace 載入與 symbol/browser read models 由 `RoslynLib` 提供。
- `MainViewModel` 透過 `MSBuildProjectLoader` 讀取 target，並把 context 傳給 `CallTreeViewModel` / `ReferenceTreeViewModel`。
- `CallTreeViewModel` 與 `ReferenceTreeViewModel` 在 `SetContext(...)` 後會自動啟動分析。
- 所有 Roslyn 呼叫皆為 `async` / `await`，並在切換 context 時取消前一次分析。
