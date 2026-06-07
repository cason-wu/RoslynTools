# roslyn-cli

> 以 [Roslyn](https://github.com/dotnet/roslyn) 為核心的 .NET 程式碼分析 CLI 工具，
> 支援 Symbol 查詢、參考追蹤、相依分析，並可作為 **MCP Server** 供 AI 直接呼叫。

---

## 功能特色

- 🔍 **Find References** — 找出 method / class / interface 的所有呼叫者與使用位置
- 🗺️ **Dependency Graph** — 分析 project 間與 namespace 間的相依關係，偵測循環相依
- 🏷️ **Symbol Info** — 查詢任意 Symbol 的完整 metadata（存取修飾詞、繼承、成員等）
- 🧹 **Dead Code 偵測** — 找出未使用的成員、高複雜度方法、過時 API 使用
- 📋 **Diagnostics** — 列出編譯錯誤、警告與 Roslyn Analyzer 結果
- 🤖 **MCP Server 模式** — 以 `stdio` transport 啟動，讓 AI Agent 直接呼叫各項功能

---

## 安裝

```bash
dotnet tool install -g roslyn-cli
```

**系統需求**

- .NET SDK 7 / 8 / 9
- 分析目標的 SDK 版本需已安裝於本機

---

## 快速開始

```bash
# 找出誰呼叫了某個 method
roslyn-cli refs callers MyApp.sln --symbol "OrderService.CalculateTotal"

# 找出 interface 的所有實作
roslyn-cli refs impls MyApp.sln --symbol "IUserRepository"

# 輸出 project 相依圖（JSON）
roslyn-cli deps graph MyApp.sln --output json

# 找出所有未使用的 public method
roslyn-cli dead unused MyApp.sln --filter method --visibility public

# 以 MCP Server 模式啟動
roslyn-cli --mcp-stdio MyApp.sln
```

---

## 指令架構

```
roslyn-cli <target> <command> <subcommand> [options]

roslyn-cli
├── refs      # 參考關係查詢
│   ├── callers    找出所有呼叫指定 Symbol 的地方
│   ├── usages     找出 Symbol 的所有使用位置
│   ├── impls      找出 interface / abstract class 的所有實作
│   └── overrides  找出所有 override 關係
├── deps      # Dependency 分析
│   ├── graph      輸出 project 間的相依圖
│   ├── cycles     偵測循環相依
│   ├── nuget      列出 NuGet 套件相依
│   └── namespaces namespace 間的使用關係
├── sym       # Symbol 資訊查詢
│   ├── info       取得 Symbol 的完整詳情
│   ├── search     Wildcard 搜尋 Symbol
│   ├── list       列出某 class / namespace 的所有成員
│   └── hierarchy  顯示繼承與介面層級
├── dead      # Dead Code 偵測
│   ├── unused     找出未被任何地方參考的 Symbol
│   ├── complexity 分析 cyclomatic complexity
│   └── obsolete   找出所有使用 [Obsolete] API 的地方
└── diag      # 診斷資訊
    ├── errors     列出所有編譯錯誤
    ├── warnings   列出所有警告
    └── analyze    執行 Roslyn Analyzer 靜態分析
```

---

## 引數與選項

### Positional Argument（必要）

| 引數 | 說明 |
|------|------|
| `<target>` | `.sln` / `.csproj` / `.cs` 路徑（第一個位置引數，不帶旗標） |

### Options（選用）

| 選項 | 說明 |
|------|------|
| `--output <format>` | `json` / `text` / `mcp`（預設 `json`） |
| `--symbol <name>` | 指定 Symbol 名稱或 Fully Qualified Name |
| `--filter <kind>` | `class` / `method` / `property` / `event` / `field` |
| `--visibility <level>` | `public` / `internal` / `private` / `all` |
| `--exclude <pattern>` | 排除 glob 路徑（可多次使用） |
| `--file <path>` | 指定 `.cs` 檔案路徑 |
| `--line <number>` | 指定行號（搭配 `--file` 定位 Symbol） |
| `--mcp-stdio` | 以 MCP stdio 模式啟動 |

---

## 指令說明

### `refs` — 參考關係查詢

```bash
# 找出誰呼叫了這個 method
roslyn-cli refs callers MyApp.sln --symbol "OrderService.CalculateTotal"

# 找出某 symbol 的所有使用位置
roslyn-cli refs usages MyApp.sln --symbol "IRepository" --output json

# 找出某 interface 的所有實作
roslyn-cli refs impls MyApp.sln --symbol "IUserRepository"

# 找出 override 關係
roslyn-cli refs overrides MyApp.sln --symbol "BaseController.OnActionExecuted"

# 以行號定位（類似 IDE Code Lens 點擊）
roslyn-cli refs callers MyLib.csproj --file "Services/OrderService.cs" --line 42
```

<details>
<summary>JSON 輸出範例</summary>

```json
{
  "command": "refs callers",
  "symbol": "OrderService.CalculateTotal",
  "symbolKind": "Method",
  "fullyQualifiedName": "MyApp.Services.OrderService.CalculateTotal",
  "callers": [
    {
      "callerSymbol": "OrderController.PlaceOrder",
      "callerKind": "Method",
      "file": "Controllers/OrderController.cs",
      "line": 42,
      "column": 18,
      "snippet": "var total = _orderService.CalculateTotal(cart);"
    }
  ],
  "totalCount": 1
}
```

</details>

---

### `deps` — Dependency 分析

```bash
# 輸出 project 相依圖
roslyn-cli deps graph MyApp.sln --output json

# 偵測循環相依
roslyn-cli deps cycles MyApp.sln

# 列出 NuGet 套件相依
roslyn-cli deps nuget MyApp.csproj

# namespace 間的使用關係（只看 MyApp.* 範圍）
roslyn-cli deps namespaces MyApp.sln --filter "MyApp.*"
```

<details>
<summary>JSON 輸出範例</summary>

```json
{
  "command": "deps graph",
  "projects": [
    {
      "name": "MyApp.Web",
      "path": "src/MyApp.Web/MyApp.Web.csproj",
      "dependsOn": ["MyApp.Services", "MyApp.Domain"]
    },
    {
      "name": "MyApp.Services",
      "path": "src/MyApp.Services/MyApp.Services.csproj",
      "dependsOn": ["MyApp.Domain", "MyApp.Infrastructure"]
    }
  ]
}
```

</details>

---

### `sym` — Symbol 資訊查詢

```bash
# 取得完整 Symbol 資訊
roslyn-cli sym info MyApp.sln --symbol "OrderService"

# Wildcard 搜尋所有 *Service class
roslyn-cli sym search MyApp.sln --symbol "*Service" --filter class

# 列出某 class 的所有 public 成員
roslyn-cli sym list MyApp.sln --symbol "OrderService" --visibility public

# 繼承 / 介面層級
roslyn-cli sym hierarchy MyApp.sln --symbol "OrderService"
```

<details>
<summary>JSON 輸出範例</summary>

```json
{
  "command": "sym info",
  "symbol": "OrderService",
  "fullyQualifiedName": "MyApp.Services.OrderService",
  "kind": "Class",
  "accessibility": "Public",
  "isAbstract": false,
  "isSealed": false,
  "isStatic": false,
  "containingNamespace": "MyApp.Services",
  "containingAssembly": "MyApp.Services",
  "baseType": "System.Object",
  "interfaces": ["IOrderService"],
  "attributes": ["RegisterService"],
  "location": {
    "file": "Services/OrderService.cs",
    "line": 12
  },
  "members": {
    "methods": 8,
    "properties": 2,
    "fields": 1
  }
}
```

</details>

---

### `dead` — Dead Code 偵測

```bash
# 找出未使用的 public method
roslyn-cli dead unused MyApp.sln --filter method --visibility public

# 找出 cyclomatic complexity 超過 10 的方法
roslyn-cli dead complexity MyApp.sln --threshold 10

# 找出所有使用 [Obsolete] API 的地方
roslyn-cli dead obsolete MyApp.sln
```

<details>
<summary>JSON 輸出範例</summary>

```json
{
  "command": "dead unused",
  "unusedSymbols": [
    {
      "symbol": "OrderService.LegacyCalculate",
      "kind": "Method",
      "accessibility": "Public",
      "file": "Services/OrderService.cs",
      "line": 87
    }
  ],
  "totalCount": 1
}
```

</details>

---

### `diag` — 診斷資訊

```bash
# 編譯錯誤清單
roslyn-cli diag errors MyApp.sln --output json

# 警告清單
roslyn-cli diag warnings MyApp.sln

# 執行 Roslyn Analyzer 靜態分析
roslyn-cli diag analyze MyApp.sln
```

---

## MCP Server 模式

`--mcp-stdio` 旗標讓 `roslyn-cli` 以 [MCP](https://modelcontextprotocol.io) stdio transport 啟動，AI Agent（如 Claude、Copilot 等）可透過 MCP protocol 直接呼叫各項分析功能，無需手動執行指令。

```bash
roslyn-cli --mcp-stdio MyApp.sln
```

### MCP Tool 對應表

| MCP Tool 名稱 | 對應 CLI 指令 |
|---|---|
| `find_callers` | `refs callers` |
| `find_usages` | `refs usages` |
| `find_implementations` | `refs impls` |
| `find_overrides` | `refs overrides` |
| `get_symbol_info` | `sym info` |
| `search_symbols` | `sym search` |
| `list_members` | `sym list` |
| `get_hierarchy` | `sym hierarchy` |
| `analyze_dependencies` | `deps graph` |
| `find_cycles` | `deps cycles` |
| `get_nuget_deps` | `deps nuget` |
| `find_unused` | `dead unused` |
| `analyze_complexity` | `dead complexity` |
| `find_obsolete` | `dead obsolete` |
| `get_diagnostics` | `diag errors` + `diag warnings` |
| `run_analyzers` | `diag analyze` |

### 在 Claude Desktop 設定

在 `claude_desktop_config.json` 中加入：

```json
{
  "mcpServers": {
    "roslyn-cli": {
      "command": "roslyn-cli",
      "args": ["--mcp-stdio", "C:/Projects/MyApp/MyApp.sln"]
    }
  }
}
```

---

## 注意事項

- **MSBuildWorkspace** 啟動時會透過 `MSBuildLocator.RegisterDefaults()` 自動偵測本機 .NET SDK，請確認目標 Solution 所需的 SDK 版本已安裝。
- **跨 Project 參考查詢**（`refs` 系列）建議以 `.sln` 為 target；以單一 `.csproj` 為目標時，其他 project 的參考將不在分析範圍內，可能產生漏報。
- **`--output mcp`** 格式輸出符合 MCP tool result 規範的 JSON，供 AI Agent 直接解析，與 `--mcp-stdio` 模式搭配使用。
- **`--mcp-stdio`** 模式與一般 CLI 模式共用同一套 Roslyn 分析核心，差異僅在 transport 層。

---

## 相依套件

| NuGet Package | 用途 |
|---|---|
| `Microsoft.CodeAnalysis.Workspaces.MSBuild` | 開啟 Solution / Project |
| `Microsoft.Build.Locator` | 自動偵測 MSBuild |
| `System.CommandLine` | CLI 解析 |
| `ModelContextProtocol` | MCP stdio server |

---

## License

MIT
