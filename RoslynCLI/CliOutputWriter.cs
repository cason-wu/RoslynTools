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
                builder.AppendLine($"Command: {callers.Command}");
                builder.AppendLine($"Symbol: {callers.FullyQualifiedName}");
                builder.AppendLine($"Callers: {callers.TotalCount}");
                foreach (var caller in callers.Callers)
                    builder.AppendLine($"- {caller.CallerSymbol} [{caller.File}:{caller.Line}:{caller.Column}] {caller.Snippet}");
                break;

            case UsagesResult usages:
                builder.AppendLine($"Command: {usages.Command}");
                builder.AppendLine($"Symbol: {usages.FullyQualifiedName}");
                builder.AppendLine($"Usages: {usages.TotalCount}");
                foreach (var usage in usages.Usages)
                    builder.AppendLine($"- {usage.File}:{usage.Line}:{usage.Column} {usage.Snippet}");
                break;

            case ImplementationsResult implementations:
                builder.AppendLine($"Command: {implementations.Command}");
                builder.AppendLine($"Symbol: {implementations.FullyQualifiedName}");
                builder.AppendLine($"Implementations: {implementations.TotalCount}");
                foreach (var implementation in implementations.Implementations)
                    builder.AppendLine($"- {implementation.Symbol} [{implementation.File}:{implementation.Line}]");
                break;

            case OverridesResult overrides:
                builder.AppendLine($"Command: {overrides.Command}");
                builder.AppendLine($"Symbol: {overrides.FullyQualifiedName}");
                builder.AppendLine($"Overrides: {overrides.TotalCount}");
                foreach (var item in overrides.Overrides)
                    builder.AppendLine($"- {item.Symbol} [{item.File}:{item.Line}]");
                break;

            case DependencyGraphResult dependencyGraph:
                builder.AppendLine($"Command: {dependencyGraph.Command}");
                foreach (var project in dependencyGraph.Projects)
                    builder.AppendLine($"- {project.Name} -> {(project.DependsOn.Count == 0 ? "(none)" : string.Join(", ", project.DependsOn))}");
                break;

            case DependencyCyclesResult dependencyCycles:
                builder.AppendLine($"Command: {dependencyCycles.Command}");
                builder.AppendLine($"Cycles: {dependencyCycles.TotalCount}");
                foreach (var cycle in dependencyCycles.Cycles)
                    builder.AppendLine($"- {string.Join(" -> ", cycle.Projects)}");
                break;

            case NugetDependenciesResult nugetDependencies:
                builder.AppendLine($"Command: {nugetDependencies.Command}");
                foreach (var project in nugetDependencies.Projects)
                {
                    builder.AppendLine($"- {project.Project}");
                    foreach (var package in project.Packages)
                        builder.AppendLine($"  - {package.PackageId} {package.Version}");
                }
                break;

            case NamespaceDependenciesResult namespaceDependencies:
                builder.AppendLine($"Command: {namespaceDependencies.Command}");
                builder.AppendLine($"Dependencies: {namespaceDependencies.TotalCount}");
                foreach (var dependency in namespaceDependencies.Dependencies)
                    builder.AppendLine($"- {dependency.SourceNamespace} -> {dependency.TargetNamespace} ({dependency.ReferenceCount})");
                break;

            case SymbolInfoResult symbolInfo:
                builder.AppendLine($"Command: {symbolInfo.Command}");
                builder.AppendLine($"Symbol: {symbolInfo.FullyQualifiedName}");
                builder.AppendLine($"Kind: {symbolInfo.Kind}");
                builder.AppendLine($"Accessibility: {symbolInfo.Accessibility}");
                builder.AppendLine($"Namespace: {symbolInfo.ContainingNamespace}");
                builder.AppendLine($"Assembly: {symbolInfo.ContainingAssembly}");
                builder.AppendLine($"BaseType: {symbolInfo.BaseType}");
                builder.AppendLine($"Location: {symbolInfo.Location.File}:{symbolInfo.Location.Line}");
                builder.AppendLine($"Members: methods={symbolInfo.Members.Methods}, properties={symbolInfo.Members.Properties}, fields={symbolInfo.Members.Fields}, events={symbolInfo.Members.Events}, nestedTypes={symbolInfo.Members.NestedTypes}");
                if (symbolInfo.Interfaces.Count > 0)
                    builder.AppendLine($"Interfaces: {string.Join(", ", symbolInfo.Interfaces)}");
                if (symbolInfo.Attributes.Count > 0)
                    builder.AppendLine($"Attributes: {string.Join(", ", symbolInfo.Attributes)}");
                break;

            case SymbolSearchResult symbolSearch:
                builder.AppendLine($"Command: {symbolSearch.Command}");
                builder.AppendLine($"Pattern: {symbolSearch.Pattern}");
                builder.AppendLine($"Matches: {symbolSearch.TotalCount}");
                foreach (var item in symbolSearch.Symbols)
                    builder.AppendLine($"- {item.FullyQualifiedName} [{item.Kind}] {item.File}:{item.Line}");
                break;

            case SymbolListResult symbolList:
                builder.AppendLine($"Command: {symbolList.Command}");
                builder.AppendLine($"Symbol: {symbolList.FullyQualifiedName}");
                builder.AppendLine($"Members: {symbolList.TotalCount}");
                foreach (var member in symbolList.Members)
                    builder.AppendLine($"- {member.Symbol} [{member.Kind}] {member.Accessibility} {member.TypeName}");
                break;

            case SymbolHierarchyResult symbolHierarchy:
                builder.AppendLine($"Command: {symbolHierarchy.Command}");
                builder.AppendLine($"Symbol: {symbolHierarchy.FullyQualifiedName}");
                if (symbolHierarchy.BaseTypes.Count > 0)
                {
                    builder.AppendLine("BaseTypes:");
                    foreach (var item in symbolHierarchy.BaseTypes)
                        builder.AppendLine($"- {item.Symbol}");
                }
                if (symbolHierarchy.Interfaces.Count > 0)
                {
                    builder.AppendLine("Interfaces:");
                    foreach (var item in symbolHierarchy.Interfaces)
                        builder.AppendLine($"- {item.Symbol}");
                }
                if (symbolHierarchy.DerivedTypes.Count > 0)
                {
                    builder.AppendLine("DerivedTypes:");
                    foreach (var item in symbolHierarchy.DerivedTypes)
                        builder.AppendLine($"- {item.Symbol}");
                }
                if (symbolHierarchy.Implementations.Count > 0)
                {
                    builder.AppendLine("Implementations:");
                    foreach (var item in symbolHierarchy.Implementations)
                        builder.AppendLine($"- {item.Symbol}");
                }
                break;

            case UnusedSymbolsResult unusedSymbols:
                builder.AppendLine($"Command: {unusedSymbols.Command}");
                builder.AppendLine($"Unused symbols: {unusedSymbols.TotalCount}");
                foreach (var item in unusedSymbols.UnusedSymbols)
                    builder.AppendLine($"- {item.Symbol} [{item.Kind}] {item.File}:{item.Line}");
                break;

            case ComplexityAnalysisResult complexity:
                builder.AppendLine($"Command: {complexity.Command}");
                builder.AppendLine($"Threshold: {complexity.Threshold}");
                builder.AppendLine($"Methods: {complexity.TotalCount}");
                foreach (var item in complexity.Methods)
                    builder.AppendLine($"- {item.Symbol} complexity={item.Complexity} {item.File}:{item.Line}");
                break;

            case ObsoleteUsagesResult obsolete:
                builder.AppendLine($"Command: {obsolete.Command}");
                builder.AppendLine($"Obsolete usages: {obsolete.TotalCount}");
                foreach (var item in obsolete.ObsoleteUsages)
                    builder.AppendLine($"- {item.DiagnosticId} {item.File}:{item.Line}:{item.Column} {item.Message}");
                break;

            case DiagnosticsResult diagnostics:
                builder.AppendLine($"Command: {diagnostics.Command}");
                builder.AppendLine($"Diagnostics: {diagnostics.TotalCount}");
                foreach (var item in diagnostics.Diagnostics)
                    builder.AppendLine($"- {item.Id} [{item.Severity}] {item.File}:{item.Line}:{item.Column} {item.Message}");
                break;

            default:
                builder.Append(JsonSerializer.Serialize(result, JsonOptions));
                break;
        }

        return builder.ToString().TrimEnd();
    }
}
