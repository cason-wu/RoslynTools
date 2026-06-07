# roslyn-cli

> 以 [Roslyn](https://github.com/dotnet/roslyn) 為核心的 .NET 程式碼分析 CLI 工具。  
> `roslyn-cli` 專注於 **一次性、stateless** 的命令列查詢；若需要 **stateful MCP stdio server**，請改用 `RoslynMcp` / `roslyn-mcp`。

---

## 功能特色

- 🔍 **Reference queries** — callers / usages / implementations / overrides
- 🧭 **Dependency analysis** — project graph / cycles / NuGet / namespace dependencies
- 🏷️ **Symbol inspection** — info / search / list / hierarchy
- 🧹 **Dead code analysis** — unused / complexity / obsolete usages
- 📋 **Diagnostics** — compiler errors / warnings / analyzer diagnostics
- 🧩 **AI-friendly output** — `--output mcp` 會輸出 MCP tool result 風格的 JSON，但**不會啟動 MCP server**

---

## 系統需求

- .NET 10 SDK
- 分析目標所需的 SDK / workload 已安裝於本機
- 若使用 `.sln` / `.slnx` / `.csproj`，需可由 `MSBuildWorkspace` 正常開啟

---

## 支援的 target 類型

- `.sln`
- `.slnx`
- `.csproj`
- `.vbproj`
- `.fsproj`
- `.cs`

---

## 使用方式

CLI 目前同時接受兩種等價寫法：

```text
roslyn-cli <group> <command> <target> [options]
roslyn-cli <target> <group> <command> [options]
```

### Help

```bash
roslyn-cli --help
roslyn-cli -h
roslyn-cli help
```

### 常用選項

| 選項 | 說明 |
|---|---|
| `--output <format>` | `json` / `text` / `mcp`，預設 `json` |
| `--symbol <name>` | Symbol 名稱或 fully qualified name |
| `--filter <kind>` | `class` / `method` / `property` / `event` / `field` / `interface` / `struct` / `enum` / `delegate` / `namespace` |
| `--visibility <level>` | `public` / `internal` / `private` / `all`，預設 `all` |
| `--exclude <pattern>` | 排除 glob-like 路徑，可重複使用 |
| `--file <path>` | 指定檔案，搭配 `--line` 以位置定位 symbol |
| `--line <number>` | 指定 1-based 行號，搭配 `--file` 使用 |
| `--threshold <number>` | `dead complexity` 門檻，預設 `10` |

---

## 快速開始

```bash
# 找出誰呼叫了某個 method
roslyn-cli refs callers MyApp.sln --symbol "OrderService.CalculateTotal"

# 另一種等價寫法（target-first）
roslyn-cli MyApp.sln refs callers --symbol "OrderService.CalculateTotal"

# 找出 interface 的所有實作
roslyn-cli refs impls MyApp.slnx --symbol "IUserRepository"

# 輸出 project dependency graph
roslyn-cli deps graph MyApp.sln --output json

# 搜尋所有 *Service 類型
roslyn-cli sym search MyApp.sln --symbol "*Service" --filter class --output text

# 找出未使用 public method
roslyn-cli dead unused MyApp.sln --filter method --visibility public

# 以檔案 + 行號定位 symbol
roslyn-cli refs callers MyLib.csproj --file "Services/OrderService.cs" --line 42

# 以 MCP tool result 風格輸出單次查詢結果
roslyn-cli sym info MyApp.sln --symbol "OrderService" --output mcp
```

---

## 指令樹

```text
roslyn-cli
├── refs
│   ├── callers
│   ├── usages
│   ├── impls
│   └── overrides
├── deps
│   ├── graph
│   ├── cycles
│   ├── nuget
│   └── namespaces
├── sym
│   ├── info
│   ├── search
│   ├── list
│   └── hierarchy
├── dead
│   ├── unused
│   ├── complexity
│   └── obsolete
└── diag
    ├── errors
    ├── warnings
    └── analyze
```

---

## 輸出格式

### `json`

預設輸出格式，適合腳本或其他工具直接處理。

### `text`

適合人類閱讀的純文字摘要，會帶出 symbol / kind / file / line 等重點資訊。

### `mcp`

輸出 MCP tool result 風格的 JSON：

- `content`：文字摘要
- `structuredContent`：完整結構化資料

這個模式只改變輸出包裝格式，不提供 stdio transport，也不維護任何 server 狀態。

---

## 與 `RoslynMcp` 的分工

| 專案 | 角色 |
|---|---|
| `RoslynCLI` | 一次性、stateless 的命令列查詢 |
| `RoslynMcp` | stateful MCP stdio server，可透過 `load_target` / `get_loaded_target` / `release_target` 管理載入狀態 |

如果 AI agent 需要在同一個 target 上連續呼叫多個工具而不想重載 workspace，請使用 `RoslynMcp`。

---

## 注意事項

- `MSBuildWorkspace` 會透過 `MSBuildLocator.RegisterDefaults()` 自動偵測本機 SDK / MSBuild。
- `refs` 類查詢若要拿到跨 project 的完整結果，建議以 `.sln` / `.slnx` 為 target。
- `.cs` target 會以 in-memory adhoc project 載入，適合單檔快速分析，但不保證有完整跨專案語意。
- `--output mcp` 只調整輸出格式；它**不等同於** MCP server。

---

## 相依套件

`RoslynCLI` 本身目前**不依賴額外 CLI framework**，維持專案內建的輕量 parser；分析核心由 `RoslynLib` 提供。

| NuGet Package | 用途 |
|---|---|
| `Microsoft.CodeAnalysis.Workspaces.MSBuild` | 載入 solution / project |
| `Microsoft.CodeAnalysis.CSharp.Workspaces` | C# syntax / semantic model |
| `Microsoft.Build.Locator` | 啟動前定位 MSBuild |

---

## License

MIT
