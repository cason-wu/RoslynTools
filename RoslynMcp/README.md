# roslyn-mcp

> 以 [Model Context Protocol](https://modelcontextprotocol.io/) 為 transport、以 `RoslynLib` 為分析核心的 **stateful MCP stdio server**。  
> `roslyn-mcp` 會在記憶體中保留目前載入的 target，讓多個 tool 呼叫可以重用同一個 workspace，不需要每次重新載入。

---

## 核心流程

1. 呼叫 `load_target` 載入 `.sln` / `.slnx` / project / `.cs`
2. 重複呼叫分析工具，例如 `find_callers`、`search_symbols`、`get_diagnostics`
3. 如需確認目前狀態，可呼叫 `get_loaded_target`
4. 完成後呼叫 `release_target` 釋放 workspace

> 如果同一路徑已經載入，`load_target` 會直接重用目前 session；若要強制重新載入，傳入 `forceReload: true`。

### 檔案變更自動同步

每次查詢前，server 會比對已載入檔案的 timestamp / 大小（`WorkspaceFileSnapshot`）：

- 既有 `.cs` / `.vb` 檔內容變更 → 以 `Solution.WithDocumentText` 增量更新。
- 專案目錄下新增 / 刪除原始碼檔，或 `.sln(x)`、project 檔、`Directory.Build.*`、`Directory.Packages.props`、`global.json` 變更 → 自動完整 reload。
- 同步結果可由 `get_loaded_target` 的 `lastSyncSummary` / `lastSyncedAtUtc` 查看。

Agent 修改檔案後不需要手動重新載入。

### 錯誤回報

Tool 內的例外一律轉成 `McpException`，讓錯誤訊息（例如未載入 target、symbol 有歧義時的候選清單）能傳回 Agent。
MCP SDK 只會轉送 `McpException` 的訊息，其他例外只會變成通用的「An error occurred invoking ...」。

---

## 支援的 target 類型

- `.sln`
- `.slnx`
- `.csproj`
- `.vbproj`
- `.fsproj`
- `.cs`

---

## Tool 一覽

### Session tools

| Tool | 用途 |
|---|---|
| `load_target` | 載入 target 並建立可重用的 Roslyn workspace session |
| `get_loaded_target` | 回傳目前 session 狀態 |
| `release_target` | 釋放目前已載入的 target |

### Analysis tools

| Tool | 用途 |
|---|---|
| `find_callers` | 找出 method 的 callers |
| `find_usages` | 找出 symbol 的 usages |
| `find_implementations` | 找出 interface / abstract type / member 的實作 |
| `find_overrides` | 找出 overrides / derived types |
| `get_symbol_info` | 取得 symbol 詳細資訊：signature、回傳型別、XML doc summary、各宣告的起訖行 |
| `get_symbol_source` | 回傳 symbol 宣告的原始碼（含 XML doc comment）與起訖行；`maxLines` 預設 200 |
| `get_document_symbols` | 檔案大綱：namespace / type / member、起訖行與巢狀深度 |
| `find_callees` | 列出 method / property 內呼叫的 method 與 constructor（outgoing calls，僅 C#）；`sourceOnly` 可略過 framework API |
| `search_symbols` | Wildcard 搜尋 symbol |
| `list_members` | 列出 type / namespace 成員 |
| `get_hierarchy` | 查詢 type hierarchy |
| `analyze_dependencies` | project dependency graph |
| `analyze_namespace_dependencies` | namespace dependency graph |
| `find_cycles` | 尋找 project dependency cycles |
| `get_nuget_deps` | 列出 NuGet 依賴 |
| `find_unused` | 找出可能未使用的 symbol |
| `analyze_complexity` | 進行 cyclomatic complexity 分析 |
| `find_obsolete` | 找出 obsolete API usages |
| `get_diagnostics` | 依 `severity`（`error` / `warning` / `all`，預設 `error`）回傳 compiler diagnostics，可用 `file` 只檢查單一檔案、`project` 篩選專案，`includeAnalyzers` 預設關閉 |
| `run_analyzers` | 執行 compiler diagnostics 與 analyzers，可用 `file` / `project` 縮小範圍 |

大多數查詢工具都支援下列常見參數組合：

- `symbol`：簡單名稱、`Type.Member`、含參數 signature（`Type.Method(int, string)`）或 fully qualified name；有歧義時回傳錯誤並列出候選
- `file` + `line`（+ `column`）：以位置選取 symbol
- `filter`：symbol kind enum（`class` / `interface` / `struct` / `enum` / `delegate` / `method` / `property` / `field` / `event` / `namespace`）
- `visibility`：`all` / `public` / `internal` / `private`
- `exclude`：wildcard，比對完整路徑或檔名（例如 `*Tests*`、`*/obj/*`）
- `maxResults`：清單上限，預設 `100`，`0` 表示不限；結果中的 `totalCount` 為完整數量、`truncated` 表示是否被截斷

其他設計：

- 所有 tool 皆標記 `ReadOnly` / `Idempotent` annotations，並支援 MCP cancellation。
- `Program.cs` 透過 `ServerInstructions` 向 client 說明 workflow、symbol 寫法與結果上限。
- 輸出路徑相對於 `get_loaded_target` 回報的 `targetDirectory`，行列皆為 1-based。

---

## 本機開發設定

若要直接以原始碼啟動 MCP server，可在 MCP 設定檔中使用 `dotnet run`：

```json
{
  "servers": {
    "RoslynMcp": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "<PATH TO RoslynMcp PROJECT DIRECTORY>"
      ]
    }
  }
}
```

## 建置與驗證

- `dotnet build RoslynTools.slnx`
- `dotnet test RoslynTools.slnx`
- 端到端（stdio）驗證：先 `dotnet build RoslynMcp`，再執行
  `uv run --no-project python -I scripts/mcp_e2e.py scripts/mcp_e2e.sample.json`。
  腳本會啟動 `RoslynMcp/bin/Debug/net10.0/win-x64/roslyn-mcp.exe`、完成 initialize，依 JSON 情境逐步呼叫 tool 或寫入檔案（測試變更同步），
  暫存檔放在 `TestResults/mcp-e2e/`（已被 `.gitignore` 排除）。情境格式見腳本開頭說明。

`roslyn-mcp` 預設不註冊 console logger，避免干擾 stdio MCP client。

`RoslynMcp` 採用 self-contained、single-file 設定，預設支援以下 runtime：

- `win-x64`
- `win-arm64`
- `osx-arm64`
- `linux-x64`
- `linux-arm64`
- `linux-musl-x64`

---

## 架構說明

- `RoslynMcp` host 本身只負責註冊 stdio transport 與 tools。
- target lifecycle 由 `RoslynLib.Infrastructure.RoslynWorkspaceTargetSession` 管理。
- 分析邏輯由 `RoslynLib.Infrastructure.RoslynCliAnalysisService` 提供。
- workspace 載入由 `RoslynLib.Infrastructure.RoslynWorkspaceLoader` 負責。

---

## 相關專案

| 專案 | 角色 |
|---|---|
| `RoslynCLI` | 一次性、stateless CLI 查詢 |
| `RoslynMcp` | stateful MCP stdio server |
| `RoslynTree` | WPF 桌面瀏覽器 |
| `RoslynLib` | 共用分析核心與 read models |
