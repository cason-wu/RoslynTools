# MSAGL 筆記

> 這份文件目前是 **未來圖形化探索的備忘錄**。  
> 現行程式碼中的正式 UI 仍以 `RoslynTree` 的 tree / list 視圖為主，尚未把 MSAGL 整合進產品功能。

[automatic-graph-layout](https://github.com/microsoft/automatic-graph-layout)

## 可用套件

MSAGL 在 NuGet 上通常拆分為幾個包，可依需求選擇：

- `Microsoft.Msagl`：核心佈局引擎（純邏輯運算）
- `Microsoft.Msagl.Drawing`：定義點、線、顏色等繪圖屬性
- `Microsoft.Msagl.GraphViewerGDI`：WinForms 控制項
- `Microsoft.Msagl.WpfGraphControl`：WPF 控制項

## Roslyn 查詢參考

`Microsoft.CodeAnalysis.FindSymbols.SymbolFinder` 可用來建構未來圖形化關係圖所需的資料來源：

```csharp
// 1. 開啟 Solution
var workspace = MSBuildWorkspace.Create();
var solution = await workspace.OpenSolutionAsync(solutionPath);

// 2. 找到要分析的 Symbol (例如某個 Method)
ISymbol methodSymbol = ...;

// 3. 尋找所有引用 (等同於 Shift+F12)
var references = await SymbolFinder.FindReferencesAsync(methodSymbol, solution);

foreach (var refSummary in references)
{
    foreach (var location in refSummary.Locations)
    {
        Console.WriteLine($"引用於: {location.Document.Name}, 位置: {location.Location.GetLineSpan()}");
    }
}
```