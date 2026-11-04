# roslyn-cli 設計規格

## 工具定位

```text
roslyn-cli
```

- `roslyn-cli` 是 **一次性、stateless** 的命令列分析工具。
- 若需要 **stateful MCP stdio server**，請使用 `RoslynMcp` / `roslyn-mcp`。
- CLI 保留 `--output mcp`，但那只是輸出格式，不會啟動 server。

---

## 指令架構

```text
roslyn-cli
├── refs      # 參考關係查詢
├── deps      # Dependency 分析
├── sym       # Symbol 資訊查詢
├── dead      # Dead Code 偵測
└── diag      # 診斷資訊
```

---

## 支援 target

- `.sln`
- `.slnx`
- `.csproj`
- `.vbproj`
- `.fsproj`
- `.cs`

---

## 呼叫方式

兩種寫法都支援：

```text
roslyn-cli <group> <command> <target> [options]
roslyn-cli <target> <group> <command> [options]
```

### 全域選項

| 選項 | 說明 |
|------|------|
| `--output <format>` | `json` / `text` / `mcp`（預設 `json`） |
| `--symbol <name>` | 指定 Symbol 名稱、`Type.Member`、含參數的 signature 或 fully qualified name（歧義時報錯並列出候選） |
| `--filter <kind>` | `class` / `method` / `property` / `event` / `field` / `interface` / `struct` / `enum` / `delegate` / `namespace` |
| `--visibility <level>` | `public` / `internal` / `private` / `all` |
| `--exclude <pattern>` | 排除 glob-like 路徑（可多次使用） |
| `--line <number>` | 指定行號（搭配 `--file` 定位 Symbol） |
| `--file <path>` | 指定檔案路徑；`diag` 指令可用來只檢查單一檔案 |
| `--column <number>` | 指定欄位（搭配 `--file` / `--line`） |
| `--project <pattern>` | `diag` 指令只檢查名稱符合 wildcard 的專案 |
| `--max-results <number>` | 最多回傳 N 筆（`TotalCount` 為完整數量、`Truncated` 表示是否截斷） |
| `--threshold <number>` | `dead complexity` 門檻，預設 `10` |
| `--max-lines <number>` | `sym source` 每個宣告最多回傳的行數 |
| `--source-only` | `refs callees` 只列出 target 原始碼中的 callee |

---

## 指令群組

### `refs` — 參考關係查詢

| 子指令 | 說明 |
|--------|------|
| `callers` | 找出所有呼叫指定 method 的地方 |
| `callees` | 列出 method / property 內呼叫的 method 與 constructor（outgoing calls，僅 C#），可加 `--source-only` |
| `usages` | 找出 Symbol 的所有使用位置 |
| `impls` | 找出 interface / abstract type / member 的實作 |
| `overrides` | 找出 override 或衍生型別關係 |

### `deps` — Dependency 分析

| 子指令 | 說明 |
|--------|------|
| `graph` | 輸出 project 間的相依圖 |
| `cycles` | 偵測循環相依 |
| `nuget` | 列出 NuGet 套件相依 |
| `namespaces` | namespace 間的使用關係 |

### `sym` — Symbol 資訊查詢

| 子指令 | 說明 |
|--------|------|
| `info` | 取得 Symbol 詳情（含 signature、回傳型別、XML doc summary、宣告行範圍） |
| `search` | Wildcard 搜尋 Symbol |
| `list` | 列出 type / namespace 的成員 |
| `hierarchy` | 顯示繼承與介面層級 |
| `source` | 回傳 Symbol 宣告的原始碼（含 XML doc comment）與起訖行，可加 `--max-lines` |
| `outline` | 以 `--file` 指定檔案，列出 namespace / type / member 的大綱、起訖行與巢狀深度 |

### `dead` — Dead Code 偵測

| 子指令 | 說明 |
|--------|------|
| `unused` | 找出未使用 Symbol |
| `complexity` | 分析 cyclomatic complexity |
| `obsolete` | 找出使用 `[Obsolete]` API 的地方 |

### `diag` — 診斷資訊

| 子指令 | 說明 |
|--------|------|
| `errors` | 列出所有編譯錯誤 |
| `warnings` | 列出所有警告 |
| `analyze` | 執行 compiler diagnostics 與 analyzers |

---

## 範例

```bash
# 找出誰呼叫了這個 method
roslyn-cli refs callers MyApp.sln --symbol "OrderService.CalculateTotal"

# 以 target-first 寫法搜尋 symbol
roslyn-cli MyApp.sln sym search --symbol "*Service" --filter class --output text

# 列出 project graph
roslyn-cli deps graph MyApp.sln --output json

# 以檔案 + 行號定位 symbol
roslyn-cli refs callers MyLib.csproj --file "Services/OrderService.cs" --line 42

# 輸出 MCP tool result 風格 JSON（單次查詢）
roslyn-cli sym info MyApp.sln --symbol "OrderService" --output mcp
```

---

## 輸出格式

| 格式 | 用途 |
|---|---|
| `json` | 預設結構化輸出 |
| `text` | 人類可讀摘要 |
| `mcp` | MCP tool result 風格 JSON，含 `content` 與 `structuredContent` |

> `--output mcp` 僅調整包裝格式；如需 stdio MCP server，請使用 `RoslynMcp`。

---

## 與 `RoslynMcp` 的分工

| 專案 | 角色 |
|---|---|
| `RoslynCLI` | 一次性命令列查詢 |
| `RoslynMcp` | 透過 `load_target` / `get_loaded_target` / `release_target` 維護工作階段狀態的 MCP stdio server |

---

## 實作與相依

- `RoslynCLI` 不依賴額外 CLI framework，改採專案內建 parser。
- 核心分析邏輯由 `RoslynLib` 提供。
- 主要相依：

| Package | 用途 |
|---|---|
| `Microsoft.CodeAnalysis.Workspaces.MSBuild` | 開啟 solution / project |
| `Microsoft.CodeAnalysis.CSharp.Workspaces` | C# syntax / semantic model |
| `Microsoft.Build.Locator` | 自動偵測 MSBuild |

---

## 實作注意事項

- `MSBuildWorkspace` 需要機器上安裝對應版本的 SDK / workloads，啟動時透過 `MSBuildLocator.RegisterDefaults()` 自動偵測。
- 跨 project 的參考查詢（`refs` 系列）建議以 solution 層級 target 執行，結果會比較完整。
- `.cs` target 會以 adhoc project 載入，適合單檔快速分析。
