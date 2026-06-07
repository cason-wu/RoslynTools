using System.Text.Json;
using System.Text.Json.Nodes;
using RoslynLib.Domain.Models;
using RoslynLib.Infrastructure;

namespace RoslynCLI;

internal sealed class McpStdioServer(
    string targetPath,
    RoslynWorkspaceLoader workspaceLoader,
    RoslynCliAnalysisService analysisService)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _targetPath = targetPath;
    private readonly RoslynWorkspaceLoader _workspaceLoader = workspaceLoader;
    private readonly RoslynCliAnalysisService _analysisService = analysisService;

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        using var workspaceTarget = await _workspaceLoader.LoadAsync(_targetPath, cancellationToken).ConfigureAwait(false);

        while (true)
        {
            var line = await Console.In.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
                return 0;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            JsonNode? message;
            try
            {
                message = JsonNode.Parse(line);
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"Invalid MCP message: {ex.Message}").ConfigureAwait(false);
                continue;
            }

            if (message is not JsonObject json)
                continue;

            await HandleMessageAsync(json, workspaceTarget, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleMessageAsync(
        JsonObject message,
        LoadedWorkspaceTarget workspaceTarget,
        CancellationToken cancellationToken)
    {
        var method = message["method"]?.GetValue<string>();
        var idNode = message["id"];

        if (string.IsNullOrWhiteSpace(method))
        {
            if (idNode is not null)
                await WriteErrorAsync(idNode, -32600, "Invalid Request", cancellationToken).ConfigureAwait(false);

            return;
        }

        switch (method)
        {
            case "initialize":
                await WriteResultAsync(idNode, new JsonObject
                {
                    ["protocolVersion"] = "2025-06-18",
                    ["capabilities"] = new JsonObject
                    {
                        ["tools"] = new JsonObject
                        {
                            ["listChanged"] = false,
                        },
                    },
                    ["serverInfo"] = new JsonObject
                    {
                        ["name"] = "roslyn-cli",
                        ["version"] = typeof(McpStdioServer).Assembly.GetName().Version?.ToString() ?? "1.0.0",
                    },
                }, cancellationToken).ConfigureAwait(false);
                return;

            case "notifications/initialized":
                return;

            case "ping":
                await WriteResultAsync(idNode, new JsonObject(), cancellationToken).ConfigureAwait(false);
                return;

            case "tools/list":
                await WriteResultAsync(idNode, new JsonObject
                {
                    ["tools"] = BuildToolsArray(),
                }, cancellationToken).ConfigureAwait(false);
                return;

            case "tools/call":
                if (idNode is null)
                    return;

                try
                {
                    var callResult = await ExecuteToolCallAsync(message["params"] as JsonObject, workspaceTarget, cancellationToken)
                        .ConfigureAwait(false);
                    await WriteResultAsync(idNode, callResult, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await WriteErrorAsync(idNode, -32000, ex.Message, cancellationToken).ConfigureAwait(false);
                }
                return;

            default:
                if (idNode is not null)
                    await WriteErrorAsync(idNode, -32601, $"Method '{method}' not found.", cancellationToken).ConfigureAwait(false);
                return;
        }
    }

    private async Task<JsonObject> ExecuteToolCallAsync(
        JsonObject? parameters,
        LoadedWorkspaceTarget workspaceTarget,
        CancellationToken cancellationToken)
    {
        if (parameters is null)
            throw new InvalidOperationException("Missing tool parameters.");

        var toolName = parameters["name"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(toolName))
            throw new InvalidOperationException("Missing tool name.");

        var arguments = parameters["arguments"] as JsonObject ?? [];
        var options = BuildQueryOptions(arguments);
        var threshold = arguments["threshold"]?.GetValue<int?>() ?? 10;

        object result = toolName switch
        {
            "find_callers" => await _analysisService.FindCallersAsync(workspaceTarget, options, cancellationToken).ConfigureAwait(false),
            "find_usages" => await _analysisService.FindUsagesAsync(workspaceTarget, options, cancellationToken).ConfigureAwait(false),
            "find_implementations" => await _analysisService.FindImplementationsAsync(workspaceTarget, options, cancellationToken).ConfigureAwait(false),
            "find_overrides" => await _analysisService.FindOverridesAsync(workspaceTarget, options, cancellationToken).ConfigureAwait(false),
            "get_symbol_info" => await _analysisService.GetSymbolInfoAsync(workspaceTarget, options, cancellationToken).ConfigureAwait(false),
            "search_symbols" => await _analysisService.SearchSymbolsAsync(workspaceTarget, options, cancellationToken).ConfigureAwait(false),
            "list_members" => await _analysisService.ListMembersAsync(workspaceTarget, options, cancellationToken).ConfigureAwait(false),
            "get_hierarchy" => await _analysisService.GetHierarchyAsync(workspaceTarget, options, cancellationToken).ConfigureAwait(false),
            "analyze_dependencies" => await _analysisService.AnalyzeDependencyGraphAsync(workspaceTarget, cancellationToken).ConfigureAwait(false),
            "find_cycles" => await _analysisService.FindDependencyCyclesAsync(workspaceTarget, cancellationToken).ConfigureAwait(false),
            "get_nuget_deps" => await _analysisService.GetNugetDependenciesAsync(workspaceTarget, cancellationToken).ConfigureAwait(false),
            "find_unused" => await _analysisService.FindUnusedSymbolsAsync(workspaceTarget, options, cancellationToken).ConfigureAwait(false),
            "analyze_complexity" => await _analysisService.AnalyzeComplexityAsync(workspaceTarget, threshold, options, cancellationToken).ConfigureAwait(false),
            "find_obsolete" => await _analysisService.FindObsoleteUsagesAsync(workspaceTarget, options, cancellationToken).ConfigureAwait(false),
            "get_diagnostics" => new
            {
                errors = await _analysisService.GetErrorsAsync(workspaceTarget, options, cancellationToken).ConfigureAwait(false),
                warnings = await _analysisService.GetWarningsAsync(workspaceTarget, options, cancellationToken).ConfigureAwait(false),
            },
            "run_analyzers" => await _analysisService.RunAnalyzersAsync(workspaceTarget, options, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Tool '{toolName}' is not supported."),
        };

        return new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = CliOutputWriter.FormatText(result),
                },
            },
            ["structuredContent"] = JsonSerializer.SerializeToNode(result, JsonOptions),
        };
    }

    private static RoslynCliQueryOptions BuildQueryOptions(JsonObject arguments)
    {
        return new RoslynCliQueryOptions
        {
            Symbol = arguments["symbol"]?.GetValue<string?>(),
            Filter = arguments["filter"]?.GetValue<string?>(),
            Visibility = arguments["visibility"]?.GetValue<string?>() ?? "all",
            File = arguments["file"]?.GetValue<string?>(),
            Line = arguments["line"]?.GetValue<int?>(),
            Excludes = ReadStringArray(arguments, "exclude"),
        };
    }

    private static IReadOnlyList<string> ReadStringArray(JsonObject arguments, string name)
    {
        if (arguments[name] is JsonArray array)
        {
            return array
                .Select(item => item?.GetValue<string>())
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .ToList()
                .AsReadOnly();
        }

        var singleValue = arguments[name]?.GetValue<string?>();
        return string.IsNullOrWhiteSpace(singleValue)
            ? []
            : [singleValue];
    }

    private static JsonArray BuildToolsArray()
    {
        return new JsonArray(
            CreateTool(
                "find_callers",
                "Find callers",
                "Find all callers of a method symbol.",
                requiresSymbolOrLocation: true),
            CreateTool(
                "find_usages",
                "Find usages",
                "Find all usage locations for a symbol.",
                requiresSymbolOrLocation: true),
            CreateTool(
                "find_implementations",
                "Find implementations",
                "Find implementations of an interface or abstract symbol.",
                requiresSymbolOrLocation: true),
            CreateTool(
                "find_overrides",
                "Find overrides",
                "Find overrides of a virtual or abstract member.",
                requiresSymbolOrLocation: true),
            CreateTool(
                "get_symbol_info",
                "Get symbol info",
                "Get detailed metadata for a symbol.",
                requiresSymbolOrLocation: true),
            CreateTool(
                "search_symbols",
                "Search symbols",
                "Search symbols using a wildcard pattern.",
                requiresSymbolOrLocation: false,
                includeFilter: true,
                includeVisibility: true),
            CreateTool(
                "list_members",
                "List members",
                "List members of a type or namespace.",
                requiresSymbolOrLocation: true,
                includeFilter: true,
                includeVisibility: true),
            CreateTool(
                "get_hierarchy",
                "Get hierarchy",
                "Get inheritance and implementation hierarchy for a type.",
                requiresSymbolOrLocation: true),
            CreateTool(
                "analyze_dependencies",
                "Analyze dependencies",
                "Return the project dependency graph.",
                requiresSymbolOrLocation: false),
            CreateTool(
                "find_cycles",
                "Find cycles",
                "Detect project dependency cycles.",
                requiresSymbolOrLocation: false),
            CreateTool(
                "get_nuget_deps",
                "Get NuGet dependencies",
                "List NuGet package references.",
                requiresSymbolOrLocation: false),
            CreateTool(
                "find_unused",
                "Find unused symbols",
                "Find potentially unused symbols.",
                requiresSymbolOrLocation: false,
                includeFilter: true,
                includeVisibility: true),
            CreateTool(
                "analyze_complexity",
                "Analyze complexity",
                "Analyze method cyclomatic complexity.",
                requiresSymbolOrLocation: false,
                includeThreshold: true),
            CreateTool(
                "find_obsolete",
                "Find obsolete usages",
                "Find usages of obsolete APIs.",
                requiresSymbolOrLocation: false),
            CreateTool(
                "get_diagnostics",
                "Get diagnostics",
                "Return compilation errors and warnings.",
                requiresSymbolOrLocation: false),
            CreateTool(
                "run_analyzers",
                "Run analyzers",
                "Run available analyzers and compiler diagnostics.",
                requiresSymbolOrLocation: false));
    }

    private static JsonObject CreateTool(
        string name,
        string title,
        string description,
        bool requiresSymbolOrLocation,
        bool includeFilter = false,
        bool includeVisibility = false,
        bool includeThreshold = false)
    {
        var properties = new JsonObject();
        var required = new JsonArray();

        if (requiresSymbolOrLocation)
        {
            properties["symbol"] = CreateStringSchema("Symbol name or fully qualified name.");
            properties["file"] = CreateStringSchema("Optional file path used with line to resolve a symbol.");
            properties["line"] = CreateIntegerSchema("Optional 1-based line number used with file.");
        }

        if (includeFilter)
            properties["filter"] = CreateStringSchema("Optional symbol kind filter.");

        if (includeVisibility)
            properties["visibility"] = CreateStringSchema("Optional visibility filter.");

        properties["exclude"] = new JsonObject
        {
            ["type"] = "array",
            ["items"] = new JsonObject
            {
                ["type"] = "string",
            },
            ["description"] = "Optional glob-like exclude patterns.",
        };

        if (includeThreshold)
            properties["threshold"] = CreateIntegerSchema("Optional complexity threshold.");

        return new JsonObject
        {
            ["name"] = name,
            ["title"] = title,
            ["description"] = description,
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = required,
            },
        };
    }

    private static JsonObject CreateStringSchema(string description)
    {
        return new JsonObject
        {
            ["type"] = "string",
            ["description"] = description,
        };
    }

    private static JsonObject CreateIntegerSchema(string description)
    {
        return new JsonObject
        {
            ["type"] = "integer",
            ["description"] = description,
        };
    }

    private static async Task WriteResultAsync(JsonNode? id, JsonNode result, CancellationToken cancellationToken)
    {
        if (id is null)
            return;

        var message = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id.DeepClone(),
            ["result"] = result,
        };

        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(message, JsonOptions)).ConfigureAwait(false);
        await Console.Out.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteErrorAsync(JsonNode id, int code, string message, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id.DeepClone(),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message,
            },
        };

        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(payload, JsonOptions)).ConfigureAwait(false);
        await Console.Out.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
