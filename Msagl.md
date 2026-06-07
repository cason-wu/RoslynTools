[automatic-graph-layout](https://github.com/microsoft/automatic-graph-layout)



MSAGL 在 NuGet 上通常拆分為幾個包，你可以依需求安裝最單純的部分：

* Microsoft.Msagl： 核心佈局引擎（最單純，純邏輯運算）。
* Microsoft.Msagl.Drawing： 定義點、線、顏色等繪圖屬性。
* Microsoft.Msagl.GraphViewerGDI： 用於 WinForms 的介面控制項。
* Microsoft.Msagl.WpfGraphControl： 用於 WPF 的介面控制項。



Microsoft.CodeAnalysis.FindSymbols.SymbolFinder

```csharp
// 1. 開啟 Solution
var workspace = MSBuildWorkspace.Create();
var solution = await workspace.OpenSolutionAsync(solutionPath);

// 2. 找到要分析的 Symbol (例如某個 Method)
ISymbol methodSymbol = ...; 

// 3. 尋找所有引用 (等同於 Shift+F12)
var references = await SymbolFinder.FindReferencesAsync(methodSymbol, solution);

foreach (var refSummary in references) {
    foreach (var location in refSummary.Locations) {
        Console.WriteLine($"引用於: {location.Document.Name}, 位置: {location.Location.GetLineSpan()}");
    }
}
```