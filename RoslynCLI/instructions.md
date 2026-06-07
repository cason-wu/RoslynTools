# roslyn-cli 設計規格

## 工具名稱

```
roslyn-cli
```

安裝方式：`dotnet tool install -g roslyn-cli`

---

## 指令架構

```
roslyn-cli
├── refs      # 參考關係查詢
├── deps      # Dependency 分析
├── sym       # Symbol 資訊查詢
├── dead      # Dead Code 偵測
└── diag      # 診斷資訊
```

---

## 全域引數與選項

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
| `--line <number>` | 指定行號（搭配 `--file` 定位 Symbol） |
| `--file <path>` | 指定 `.cs` 檔案路徑 |
| `--mcp-stdio` | 以 MCP stdio 模式啟動 |

---

## 1. `refs` — 參考關係查詢

### 子指令

| 子指令 | 說明 |
|--------|------|
| `callers` | 找出所有呼叫指定 Symbol 的地方 |
| `usages` | 找出 Symbol 的所有使用位置 |
| `impls` | 找出 interface / abstract class 的所有實作 |
| `overrides` | 找出所有 override 關係 |

### 範例

```bash
# 找出誰呼叫了這個 method
roslyn-cli refs callers MyApp.sln --symbol "OrderService.CalculateTotal"

# 找出某 symbol 的所有使用位置
roslyn-cli refs usages MyApp.sln --symbol "IRepository" --output json

# 找出某 interface 的所有實作
roslyn-cli refs impls MyApp.sln --symbol "IUserRepository"

# 找出 override 關係
roslyn-cli refs overrides MyApp.sln --symbol "BaseController.OnActionExecuted"

# 以行號定位（類似 Code Lens 點擊）
roslyn-cli refs callers MyLib.csproj --file "Services/OrderService.cs" --line 42
```

### JSON 輸出格式（`refs callers`）

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

---

## 2. `deps` — Dependency 分析

### 子指令

| 子指令 | 說明 |
|--------|------|
| `graph` | 輸出 project 間的相依圖 |
| `cycles` | 偵測循環相依 |
| `nuget` | 列出 NuGet 套件相依 |
| `namespaces` | namespace 間的使用關係 |

### 範例

```bash
# 輸出 project 相依圖（JSON 供 AI 解讀）
roslyn-cli deps graph MyApp.sln --output json

# 偵測循環相依
roslyn-cli deps cycles MyApp.sln

# 列出 NuGet 套件相依
roslyn-cli deps nuget MyApp.csproj

# namespace 間的使用關係
roslyn-cli deps namespaces MyApp.sln --filter "MyApp.*"
```

### JSON 輸出格式（`deps graph`）

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

---

## 3. `sym` — Symbol 資訊查詢

### 子指令

| 子指令 | 說明 |
|--------|------|
| `info` | 取得 Symbol 的完整詳情（存取修飾詞、型別、屬性等） |
| `search` | Wildcard 搜尋 Symbol |
| `list` | 列出某 class / namespace 的所有成員 |
| `hierarchy` | 顯示繼承與介面層級 |

### 範例

```bash
# 取得完整 Symbol 資訊
roslyn-cli sym info MyApp.sln --symbol "OrderService"

# Wildcard 搜尋
roslyn-cli sym search MyApp.sln --symbol "*Service" --filter class

# 列出某 class 的所有成員
roslyn-cli sym list MyApp.sln --symbol "OrderService" --visibility public

# 繼承 / 介面層級
roslyn-cli sym hierarchy MyApp.sln --symbol "OrderService"
```

### JSON 輸出格式（`sym info`）

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

---

## 4. `dead` — Dead Code 偵測

### 子指令

| 子指令 | 說明 |
|--------|------|
| `unused` | 找出未被任何地方參考的 Symbol |
| `complexity` | 分析 cyclomatic complexity，找出高複雜度方法 |
| `obsolete` | 找出所有使用 `[Obsolete]` API 的地方 |

### 範例

```bash
# 找出未使用的 public method
roslyn-cli dead unused MyApp.sln --filter method --visibility public

# 複雜度分析（找高複雜度方法，threshold 預設 10）
roslyn-cli dead complexity MyApp.sln --threshold 10

# 找出所有使用 [Obsolete] API 的地方
roslyn-cli dead obsolete MyApp.sln
```

### JSON 輸出格式（`dead unused`）

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

---

## 5. `diag` — 診斷資訊

### 子指令

| 子指令 | 說明 |
|--------|------|
| `errors` | 列出所有編譯錯誤 |
| `warnings` | 列出所有警告 |
| `analyze` | 執行 Roslyn Analyzer 靜態分析 |

### 範例

```bash
# 編譯錯誤清單
roslyn-cli diag errors MyApp.sln --output json

# 警告清單
roslyn-cli diag warnings MyApp.sln

# 執行 Roslyn Analyzer
roslyn-cli diag analyze MyApp.sln
```

---

## MCP stdio 模式

以 MCP Server 模式啟動，AI 可透過 MCP protocol 直接呼叫：

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

---

## 實作建議的 NuGet Packages

| Package | 用途 |
|---|---|
| `Microsoft.CodeAnalysis.Workspaces.MSBuild` | 開啟 Solution / Project |
| `Microsoft.Build.Locator` | 自動偵測 MSBuild |
| `System.CommandLine` | CLI 解析（dotnet 官方） |
| `ModelContextProtocol` | MCP stdio server |

---

## 實作注意事項

- `MSBuildWorkspace` 需要機器上安裝對應版本的 .NET SDK，啟動時透過 `MSBuildLocator.RegisterDefaults()` 自動偵測。
- 跨 Project 的參考查詢（`refs` 系列）**必須在 Solution 層級**才能得到完整結果；單 Project 層級會漏掉其他 project 的參考。
- `--output mcp` 格式輸出符合 MCP tool result 規範的 JSON，供 AI agent 直接解析。
- `--mcp-stdio` 模式與一般 CLI 模式共用同一套 Roslyn 分析核心，差異僅在 transport 層。
