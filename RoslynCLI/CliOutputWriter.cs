using System.Text;
using System.Text.Json;
using RoslynLib.Infrastructure;

namespace RoslynCLI;

internal static class CliOutputWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static async Task WriteAsync(object result, string outputFormat, TextWriter writer)
    {
        var normalized = string.IsNullOrWhiteSpace(outputFormat)
            ? "json"
            : outputFormat.Trim().ToLowerInvariant();

        var payload = normalized switch
        {
            "text" => FormatText(result),
            "mcp" => JsonSerializer.Serialize(new
            {
                content = new[]
                {
                    new
                    {
                        type = "text",
                        text = FormatText(result),
                    },
                },
                structuredContent = result,
            }, JsonOptions),
            _ => JsonSerializer.Serialize(result, JsonOptions),
        };

        await writer.WriteLineAsync(payload).ConfigureAwait(false);
    }

    public static string FormatText(object result)
    {
        var builder = new StringBuilder();

        switch (result)
        {
            case CallersResult callers:
                AppendPair(builder, "Command", callers.Command);
                AppendPair(builder, "Symbol", callers.FullyQualifiedName);
                AppendPair(builder, "Callers", callers.TotalCount);
                foreach (var caller in callers.Callers)
                    AppendItem(builder, $"{caller.CallerSymbol} [{caller.CallerKind}] @ {caller.File}:{caller.Line}:{caller.Column} — {caller.Snippet}");
                break;

            case UsagesResult usages:
                AppendPair(builder, "Command", usages.Command);
                AppendPair(builder, "Symbol", usages.FullyQualifiedName);
                AppendPair(builder, "Usages", usages.TotalCount);
                foreach (var usage in usages.Usages)
                {
                    var containingContext = string.Join(
                        ".",
                        new[] { usage.ContainingNamespace, usage.ContainingType, usage.ContainingSymbol }
                            .Where(static value => !string.IsNullOrWhiteSpace(value)));
                    var prefix = string.IsNullOrWhiteSpace(containingContext)
                        ? string.Empty
                        : $" [{containingContext}]";
                    AppendItem(builder, $"{usage.File}:{usage.Line}:{usage.Column}{prefix} — {usage.Snippet}");
                }
                break;

            case ImplementationsResult implementations:
                AppendPair(builder, "Command", implementations.Command);
                AppendPair(builder, "Symbol", implementations.FullyQualifiedName);
                AppendPair(builder, "Implementations", implementations.TotalCount);
                foreach (var implementation in implementations.Implementations)
                    AppendItem(builder, $"{implementation.Symbol} [{implementation.Kind}] @ {implementation.File}:{implementation.Line} ({implementation.ContainingAssembly})");
                break;

            case OverridesResult overrides:
                AppendPair(builder, "Command", overrides.Command);
                AppendPair(builder, "Symbol", overrides.FullyQualifiedName);
                AppendPair(builder, "Overrides", overrides.TotalCount);
                foreach (var item in overrides.Overrides)
                    AppendItem(builder, $"{item.Symbol} [{item.Kind}] @ {item.File}:{item.Line} (type: {item.ContainingType})");
                break;

            case DependencyGraphResult dependencyGraph:
                AppendPair(builder, "Command", dependencyGraph.Command);
                foreach (var project in dependencyGraph.Projects)
                    AppendItem(builder, $"{project.Name} -> {(project.DependsOn.Count == 0 ? "(none)" : string.Join(", ", project.DependsOn))}");
                break;

            case DependencyCyclesResult dependencyCycles:
                AppendPair(builder, "Command", dependencyCycles.Command);
                AppendPair(builder, "Cycles", dependencyCycles.TotalCount);
                foreach (var cycle in dependencyCycles.Cycles)
                    AppendItem(builder, string.Join(" -> ", cycle.Projects));
                break;

            case NugetDependenciesResult nugetDependencies:
                AppendPair(builder, "Command", nugetDependencies.Command);
                foreach (var project in nugetDependencies.Projects)
                {
                    AppendItem(builder, project.Project);
                    foreach (var package in project.Packages)
                        AppendSubItem(builder, $"{package.PackageId} {package.Version}");
                }
                break;

            case NamespaceDependenciesResult namespaceDependencies:
                AppendPair(builder, "Command", namespaceDependencies.Command);
                AppendPair(builder, "Dependencies", namespaceDependencies.TotalCount);
                foreach (var dependency in namespaceDependencies.Dependencies)
                    AppendItem(builder, $"{dependency.SourceNamespace} -> {dependency.TargetNamespace} ({dependency.ReferenceCount})");
                break;

            case SymbolInfoResult symbolInfo:
                AppendPair(builder, "Command", symbolInfo.Command);
                AppendPair(builder, "Symbol", symbolInfo.FullyQualifiedName);
                AppendPair(builder, "Kind", symbolInfo.Kind);
                AppendPair(builder, "Accessibility", symbolInfo.Accessibility);
                AppendPair(builder, "Namespace", symbolInfo.ContainingNamespace);
                AppendPair(builder, "Assembly", symbolInfo.ContainingAssembly);
                AppendPair(builder, "BaseType", string.IsNullOrWhiteSpace(symbolInfo.BaseType) ? "(none)" : symbolInfo.BaseType);
                AppendPair(builder, "Signature", symbolInfo.Signature);
                if (!string.IsNullOrWhiteSpace(symbolInfo.ReturnType))
                    AppendPair(builder, "ReturnType", symbolInfo.ReturnType);
                if (!string.IsNullOrWhiteSpace(symbolInfo.Summary))
                    AppendPair(builder, "Summary", symbolInfo.Summary);
                AppendPair(builder, "Location", $"{symbolInfo.Location.File}:{symbolInfo.Location.Line}");
                if (symbolInfo.Declarations.Count > 0)
                    AppendPair(builder, "Declarations", string.Join(", ", symbolInfo.Declarations.Select(static item => $"{item.File}:{item.StartLine}-{item.EndLine}")));
                AppendPair(builder, "Members", $"methods={symbolInfo.Members.Methods}, properties={symbolInfo.Members.Properties}, fields={symbolInfo.Members.Fields}, events={symbolInfo.Members.Events}, nestedTypes={symbolInfo.Members.NestedTypes}");
                if (symbolInfo.Interfaces.Count > 0)
                    AppendPair(builder, "Interfaces", string.Join(", ", symbolInfo.Interfaces));
                if (symbolInfo.Attributes.Count > 0)
                    AppendPair(builder, "Attributes", string.Join(", ", symbolInfo.Attributes));
                break;

            case SymbolSourceResult symbolSource:
                AppendPair(builder, "Command", symbolSource.Command);
                AppendPair(builder, "Symbol", symbolSource.FullyQualifiedName);
                AppendPair(builder, "Declarations", symbolSource.TotalCount);
                foreach (var declaration in symbolSource.Declarations)
                {
                    var truncatedText = declaration.Truncated ? " (truncated by --max-lines)" : string.Empty;
                    builder.AppendLine($"--- {declaration.File}:{declaration.StartLine}-{declaration.EndLine}{truncatedText}");
                    builder.AppendLine(declaration.Source);
                }
                break;

            case DocumentSymbolsResult documentSymbols:
                AppendPair(builder, "Command", documentSymbols.Command);
                AppendPair(builder, "File", documentSymbols.File);
                AppendPair(builder, "Symbols", documentSymbols.TotalCount);
                foreach (var item in documentSymbols.Symbols)
                    builder.AppendLine($"{new string(' ', item.Depth * 2)}- {item.Symbol} [{item.Kind}, {item.Accessibility}] {item.StartLine}-{item.EndLine}");
                break;

            case CalleesResult callees:
                AppendPair(builder, "Command", callees.Command);
                AppendPair(builder, "Symbol", callees.FullyQualifiedName);
                AppendPair(builder, "Callees", callees.TotalCount);
                foreach (var callee in callees.Callees)
                {
                    var definition = callee.IsSource ? $"{callee.File}:{callee.Line}" : callee.ContainingAssembly;
                    AppendItem(builder, $"{callee.Symbol} [{callee.Kind}] @ {definition} — called at line {string.Join(", ", callee.CallLines)}");
                }
                break;

            case SymbolSearchResult symbolSearch:
                AppendPair(builder, "Command", symbolSearch.Command);
                AppendPair(builder, "Pattern", symbolSearch.Pattern);
                AppendPair(builder, "Matches", symbolSearch.TotalCount);
                foreach (var item in symbolSearch.Symbols)
                    AppendItem(builder, $"{item.FullyQualifiedName} [{item.Kind}] {item.File}:{item.Line}");
                break;

            case SymbolListResult symbolList:
                AppendPair(builder, "Command", symbolList.Command);
                AppendPair(builder, "Symbol", symbolList.FullyQualifiedName);
                AppendPair(builder, "Members", symbolList.TotalCount);
                foreach (var member in symbolList.Members)
                    AppendItem(builder, $"{member.Symbol} [{member.Kind}] {member.Accessibility} {member.TypeName} @ {member.File}:{member.Line}");
                break;

            case SymbolHierarchyResult symbolHierarchy:
                AppendPair(builder, "Command", symbolHierarchy.Command);
                AppendPair(builder, "Symbol", symbolHierarchy.FullyQualifiedName);
                if (symbolHierarchy.BaseTypes.Count > 0)
                {
                    AppendSection(builder, "BaseTypes");
                    foreach (var item in symbolHierarchy.BaseTypes)
                        AppendItem(builder, item.Symbol);
                }
                if (symbolHierarchy.Interfaces.Count > 0)
                {
                    AppendSection(builder, "Interfaces");
                    foreach (var item in symbolHierarchy.Interfaces)
                        AppendItem(builder, item.Symbol);
                }
                if (symbolHierarchy.DerivedTypes.Count > 0)
                {
                    AppendSection(builder, "DerivedTypes");
                    foreach (var item in symbolHierarchy.DerivedTypes)
                        AppendItem(builder, item.Symbol);
                }
                if (symbolHierarchy.Implementations.Count > 0)
                {
                    AppendSection(builder, "Implementations");
                    foreach (var item in symbolHierarchy.Implementations)
                        AppendItem(builder, item.Symbol);
                }
                break;

            case UnusedSymbolsResult unusedSymbols:
                AppendPair(builder, "Command", unusedSymbols.Command);
                AppendPair(builder, "Unused symbols", unusedSymbols.TotalCount);
                foreach (var item in unusedSymbols.UnusedSymbols)
                    AppendItem(builder, $"{item.Symbol} [{item.Kind}] {item.File}:{item.Line} ({item.Accessibility})");
                break;

            case ComplexityAnalysisResult complexity:
                AppendPair(builder, "Command", complexity.Command);
                AppendPair(builder, "Threshold", complexity.Threshold);
                AppendPair(builder, "Methods", complexity.TotalCount);
                foreach (var item in complexity.Methods)
                    AppendItem(builder, $"{item.Symbol} complexity={item.Complexity} @ {item.File}:{item.Line}");
                break;

            case ObsoleteUsagesResult obsolete:
                AppendPair(builder, "Command", obsolete.Command);
                AppendPair(builder, "Obsolete usages", obsolete.TotalCount);
                foreach (var item in obsolete.ObsoleteUsages)
                    AppendItem(builder, $"{item.DiagnosticId} {item.File}:{item.Line}:{item.Column} — {item.Message}");
                break;

            case DiagnosticsResult diagnostics:
                AppendPair(builder, "Command", diagnostics.Command);
                AppendPair(builder, "Diagnostics", diagnostics.TotalCount);
                foreach (var item in diagnostics.Diagnostics)
                {
                    var projectText = string.IsNullOrWhiteSpace(item.Project)
                        ? string.Empty
                        : $" ({item.Project})";
                    AppendItem(builder, $"{item.Id} [{item.Severity}] {item.File}:{item.Line}:{item.Column}{projectText} — {item.Message}");
                }
                break;

            default:
                builder.Append(JsonSerializer.Serialize(result, JsonOptions));
                break;
        }

        if (result.GetType().GetProperty("Truncated")?.GetValue(result) is true)
            builder.AppendLine("(Results truncated by --max-results.)");

        return builder.ToString().TrimEnd();
    }

    private static void AppendPair(StringBuilder builder, string label, object? value)
        => builder.AppendLine($"{label}: {value}");

    private static void AppendSection(StringBuilder builder, string title)
        => builder.AppendLine($"{title}:");

    private static void AppendItem(StringBuilder builder, string value)
        => builder.AppendLine($"- {value}");

    private static void AppendSubItem(StringBuilder builder, string value)
        => builder.AppendLine($"  - {value}");
}
