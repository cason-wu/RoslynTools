# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 專案目的

利用 Roslyn API 分析 .NET Solution / Project / Document 內的 Symbol。支援的 target：`.sln`、`.slnx`、`.csproj`、`.vbproj`、`.fsproj`、`.cs`。

## 常用指令

```bash
dotnet build RoslynTools.slnx
dotnet test RoslynTools.slnx

# 單一測試類別 / 方法（xUnit v3）
dotnet test RoslynLib.Tests --filter "FullyQualifiedName~RoslynCliAnalysisServiceTests"
dotnet test RoslynLib.Tests --filter "FullyQualifiedName~RoslynCliAnalysisServiceTests.<MethodName>"

# 執行 CLI（stateless 單次查詢）
dotnet run --project RoslynCLI -- sym search RoslynTools.slnx --symbol "*Service" --filter class --output text

# 以原始碼啟動 MCP stdio server
dotnet run --project RoslynMcp

# MCP 端到端（stdio）驗證：啟動 build 出來的 roslyn-mcp.exe，依 JSON 情境呼叫 tool / 寫檔
dotnet build RoslynMcp
uv run --no-project python -I agents/scripts/mcp_e2e.py agents/scripts/mcp_e2e.sample.json
```

- 全部專案 target `net10.0`（`RoslynTree` 為 `net10.0-windows` WPF，只能在 Windows 建置）。
- 沒有 CI workflow、沒有 `.editorconfig`。

## 架構

```
RoslynLib  ←  RoslynCLI   (stateless CLI, exe)
           ←  RoslynMcp   (stateful MCP stdio server, assembly name: roslyn-mcp)
           ←  RoslynTree  (WPF 桌面瀏覽器)
           ←  RoslynLib.Tests
```

`RoslynLib` 是唯一的分析核心；presentation 專案只處理 transport / UI / lifecycle，分析邏輯一律放在 `RoslynLib`。

### RoslynLib 關鍵元件（`RoslynLib/Infrastructure/`）

- `RoslynWorkspaceLoader`：依副檔名載入 target，回傳 `LoadedWorkspaceTarget`（`IDisposable`，持有 workspace）。
  - `.sln/.slnx/*proj` → `MSBuildWorkspace`；首次使用前以 static lock 呼叫一次 `MSBuildLocator.RegisterDefaults()`。
  - `.cs` → `AdhocWorkspace` in-memory project，metadata references 取自 `TRUSTED_PLATFORM_ASSEMBLIES`，沒有跨專案語意。
- `RoslynCliAnalysisService`（partial：`.cs` + `.Helpers.cs` + `.Source.cs`；`.Source.cs` 放 symbol source / document outline / callees 與 signature、XML doc summary 解析）：CLI 與 MCP **共用**的查詢服務（refs / deps / sym / dead / diag）。輸入為 `LoadedWorkspaceTarget` + `RoslynCliQueryOptions`，輸出 record 定義在 `RoslynCliAnalysisResults.cs`。
- `RoslynWorkspaceTargetSession`：MCP 專用的 loaded-target lifecycle（load / status / release / `RunWithLoadedTargetAsync`），讓多次 tool 呼叫重用同一 workspace；同一路徑重複 load 會直接重用（`forceReload` 可強制重載）。
  - 每次查詢前以 `WorkspaceFileSnapshot` 比對檔案 timestamp：`.cs` 內容變更走 `Solution.WithDocumentText` 增量更新（`LoadedWorkspaceTarget.ReplaceSolution`），新增/刪除檔案或 project / `Directory.Build.*` 變更則完整 reload。
- Symbol 名稱解析（`ResolveSymbolAsync`）：同分候選多於一個時丟出例外並列出候選，不可改回靜默取第一個。列舉 source symbols 必須走 `compilation.Assembly.GlobalNamespace`（`compilation.GlobalNamespace` 是 merged namespace，`ContainingAssembly` 為 null，會漏掉與被參考專案共用 namespace 的 symbol）；結果以 `ConditionalWeakTable<Solution, ...>` 快取。
- List 型 result record 都有 `TotalCount`（完整數量）與 `Truncated`；以 `RoslynCliQueryOptions.MaxResults` 截斷（CLI 預設不限，MCP 預設 100）。
- `RoslynSymbolAnalysisService` + `MSBuildProjectLoader` / `ProjectNodeBuilder`：RoslynTree 專用，建立 call tree、reference tree 與 namespace/type/member browser read models（`Domain/Models/`）。
- `AssemblyInfo.cs` 設定 `InternalsVisibleTo("RoslynLib.Tests")`，可用 `internal` constructor 注入測試替身。

### Presentation 專案

- **RoslynCLI**：自製輕量 parser（不依賴 CLI framework）。`Program.ExecuteAsync` 以 `(group, command)` switch 對應到 `RoslynCliAnalysisService` 方法；接受 `<group> <command> <target>` 與 `<target> <group> <command>` 兩種順序。`--output json|text|mcp`，`mcp` 只是輸出包裝（`content` + `structuredContent`），不會啟動 server。輸出格式化在 `CliOutputWriter`，help 在 `CliHelpText`。
- **RoslynMcp**：`Program.cs` 只註冊 DI（loader / analysis service / session 皆為 singleton）與 stdio transport；tools 定義在 `Tools/RoslynAnalysisTools.cs`。**必須保持 stdout 乾淨**（已 `ClearProviders()`），不可寫 console log，否則會破壞 MCP 協定。Publish 設定為 self-contained single-file。
  - Tool 一律經過 `InvokeAsync` 把例外轉成 `McpException`；MCP SDK 只會轉送 `McpException` 的訊息，其他例外 Agent 只會看到「An error occurred invoking ...」。
  - `filter` / `visibility` / `severity` 參數使用 `Tools/ToolParameters.cs` 的 enum（`JsonStringEnumMemberName` 小寫），讓 input schema 出現 `enum`；共用的參數描述也在該檔。
  - Workflow 說明放在 `Program.cs` 的 `ServerInstructions`。
  - 有 `RuntimeIdentifiers` + self-contained，Debug build 輸出在 `RoslynMcp/bin/Debug/net10.0/win-x64/`，不是 `net10.0/`。
- **RoslynTree**：WPF + `CommunityToolkit.Mvvm` + DI（`App.OnStartup`）。`MainViewModel` 載入 target 後把 context 交給 `CallTreeViewModel` / `ReferenceTreeViewModel`，`SetContext(...)` 會自動開始分析並取消前一次分析；節點 lazy-load 展開。Symbol 圖示來自 `Microsoft.VisualStudio.ImageCatalog`。

新增一個查詢功能時，通常要同步修改：`RoslynCliAnalysisService`（+ result record）→ `RoslynCLI/Program.cs` switch 與 `CliHelpText` → `RoslynMcp/Tools/RoslynAnalysisTools.cs` → 各專案 README / `RoslynCLI/instructions.md`。

### 相依注意事項

`RoslynLib.csproj` 中 `Microsoft.Build*` 套件刻意設定 `ExcludeAssets="runtime"`，讓 `MSBuildLocator` 在 runtime 載入本機 MSBuild；不可移除，否則會產生 MSBuild assembly 衝突。

## 測試

- xUnit v3 + NSubstitute，`Xunit` 為 global using。
- `RoslynLib.Tests/Helpers/RoslynTestHelper` 以 in-memory `CSharpCompilation` / `Solution` 建立測試資料，多數測試不需要真實的專案檔。

## Coding conventions（來自 `.github/instructions/`）

- PascalCase：型別、public 成員；camelCase：private 欄位、區域變數。
- `csharp_prefer_braces = false`：單行 `if` 等不加大括號。
- 不要新增 XML Documentation Comments。
- 文件（README、instructions）以繁體中文撰寫。

## 其他文件

- `RoslynCLI/README.md`、`RoslynCLI/instructions.md`：CLI 指令樹與選項規格。
- `RoslynMcp/README.md`：MCP tool 一覽與本機設定。
- `Msagl.md`：未來圖形化（MSAGL）探索備忘，尚未整合進產品。
