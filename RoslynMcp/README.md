# roslyn-mcp

> 以 [Model Context Protocol](https://modelcontextprotocol.io/) 為 transport、以 `RoslynLib` 為分析核心的 **stateful MCP stdio server**。  
> 與 `RoslynCLI` 不同，`roslyn-mcp` 會在記憶體中保留目前載入的 target，讓多個 tool 呼叫可以重用同一個 workspace，不需要每次重新載入。

---

## 核心流程

1. 呼叫 `load_target` 載入 `.sln` / `.slnx` / project / `.cs`
2. 重複呼叫分析工具，例如 `find_callers`、`search_symbols`、`get_diagnostics`
3. 如需確認目前狀態，可呼叫 `get_loaded_target`
4. 完成後呼叫 `release_target` 釋放 workspace

> 如果同一路徑已經載入，`load_target` 會直接重用目前 session；若你想強制重新載入，先呼叫 `release_target` 再重新 `load_target`。

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
| `get_symbol_info` | 取得 symbol 詳細資訊 |
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
| `get_diagnostics` | 一次回傳 errors + warnings |
| `run_analyzers` | 執行 compiler diagnostics 與 analyzers |

大多數查詢工具都支援下列常見參數組合：

- `symbol`
- `file` + `line`
- `filter`
- `visibility`
- `exclude`

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

---

## 以 NuGet MCP package 使用

`RoslynMcp` 的 package id 與 tool command name 都是 `roslyn-mcp`。  
發布到 NuGet 後，可用 `dnx` 方式在 VS Code 或 Visual Studio 中註冊：

```json
{
  "servers": {
    "RoslynMcp": {
      "type": "stdio",
      "command": "dnx",
      "args": [
        "roslyn-mcp",
        "--version",
        "0.1.0-beta",
        "--yes"
      ]
    }
  }
}
```

---

## 建置與驗證

- `dotnet build SymbolTree.slnx`
- `dotnet test SymbolTree.slnx`

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

