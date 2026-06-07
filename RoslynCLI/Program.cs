using RoslynLib.Domain.Models;
using RoslynLib.Infrastructure;

namespace RoslynCLI;

internal static class Program
{
    private static readonly RoslynWorkspaceLoader WorkspaceLoader = new();
    private static readonly RoslynCliAnalysisService AnalysisService = new();

    private static async Task<int> Main(string[] args)
    {
        var normalizedArgs = NormalizeArguments(args);

        if (ShouldShowHelp(normalizedArgs))
        {
            await Console.Out.WriteLineAsync(CliHelpText.Build()).ConfigureAwait(false);
            return 0;
        }

        CliArguments parsedArguments;
        try
        {
            parsedArguments = ParseArguments(normalizedArgs);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            await Console.Error.WriteLineAsync(string.Empty).ConfigureAwait(false);
            await Console.Error.WriteLineAsync(CliHelpText.Build()).ConfigureAwait(false);
            return 1;
        }

        try
        {
            using var workspaceTarget = await WorkspaceLoader.LoadAsync(parsedArguments.Target).ConfigureAwait(false);
            var result = await ExecuteAsync(parsedArguments, workspaceTarget).ConfigureAwait(false);
            await CliOutputWriter.WriteAsync(result, parsedArguments.Output, Console.Out).ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task<object> ExecuteAsync(
        CliArguments arguments,
        LoadedWorkspaceTarget workspaceTarget)
    {
        var group = arguments.Group.ToLowerInvariant();
        var command = arguments.Command.ToLowerInvariant();

        return (group, command) switch
        {
            ("refs", "callers") => await AnalysisService.FindCallersAsync(workspaceTarget, arguments.QueryOptions).ConfigureAwait(false),
            ("refs", "usages") => await AnalysisService.FindUsagesAsync(workspaceTarget, arguments.QueryOptions).ConfigureAwait(false),
            ("refs", "impls") => await AnalysisService.FindImplementationsAsync(workspaceTarget, arguments.QueryOptions).ConfigureAwait(false),
            ("refs", "overrides") => await AnalysisService.FindOverridesAsync(workspaceTarget, arguments.QueryOptions).ConfigureAwait(false),
            ("deps", "graph") => await AnalysisService.AnalyzeDependencyGraphAsync(workspaceTarget).ConfigureAwait(false),
            ("deps", "cycles") => await AnalysisService.FindDependencyCyclesAsync(workspaceTarget).ConfigureAwait(false),
            ("deps", "nuget") => await AnalysisService.GetNugetDependenciesAsync(workspaceTarget).ConfigureAwait(false),
            ("deps", "namespaces") => await AnalysisService.AnalyzeNamespaceDependenciesAsync(workspaceTarget, arguments.QueryOptions).ConfigureAwait(false),
            ("sym", "info") => await AnalysisService.GetSymbolInfoAsync(workspaceTarget, arguments.QueryOptions).ConfigureAwait(false),
            ("sym", "search") => await AnalysisService.SearchSymbolsAsync(workspaceTarget, arguments.QueryOptions).ConfigureAwait(false),
            ("sym", "list") => await AnalysisService.ListMembersAsync(workspaceTarget, arguments.QueryOptions).ConfigureAwait(false),
            ("sym", "hierarchy") => await AnalysisService.GetHierarchyAsync(workspaceTarget, arguments.QueryOptions).ConfigureAwait(false),
            ("dead", "unused") => await AnalysisService.FindUnusedSymbolsAsync(workspaceTarget, arguments.QueryOptions).ConfigureAwait(false),
            ("dead", "complexity") => await AnalysisService.AnalyzeComplexityAsync(workspaceTarget, arguments.Threshold ?? 10, arguments.QueryOptions).ConfigureAwait(false),
            ("dead", "obsolete") => await AnalysisService.FindObsoleteUsagesAsync(workspaceTarget, arguments.QueryOptions).ConfigureAwait(false),
            ("diag", "errors") => await AnalysisService.GetErrorsAsync(workspaceTarget, arguments.QueryOptions).ConfigureAwait(false),
            ("diag", "warnings") => await AnalysisService.GetWarningsAsync(workspaceTarget, arguments.QueryOptions).ConfigureAwait(false),
            ("diag", "analyze") => await AnalysisService.RunAnalyzersAsync(workspaceTarget, arguments.QueryOptions).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unknown command '{arguments.Group} {arguments.Command}'."),
        };
    }

    private static CliArguments ParseArguments(IReadOnlyList<string> args)
    {
        var positionals = new List<string>();
        var excludes = new List<string>();
        string output = "json";
        string? symbol = null;
        string? filter = null;
        string visibility = "all";
        string? file = null;
        int? line = null;
        int? threshold = null;

        for (var index = 0; index < args.Count; index++)
        {
            var token = args[index];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                positionals.Add(token);
                continue;
            }

            switch (token.ToLowerInvariant())
            {
                case "--output":
                    output = ReadNextValue(args, ref index, token);
                    break;
                case "--symbol":
                    symbol = ReadNextValue(args, ref index, token);
                    break;
                case "--filter":
                    filter = ReadNextValue(args, ref index, token);
                    break;
                case "--visibility":
                    visibility = ReadNextValue(args, ref index, token);
                    break;
                case "--exclude":
                    excludes.Add(ReadNextValue(args, ref index, token));
                    break;
                case "--file":
                    file = ReadNextValue(args, ref index, token);
                    break;
                case "--line":
                    line = int.Parse(ReadNextValue(args, ref index, token));
                    break;
                case "--threshold":
                    threshold = int.Parse(ReadNextValue(args, ref index, token));
                    break;
                default:
                    throw new InvalidOperationException($"Unknown option '{token}'.");
            }
        }

        if (positionals.Count != 3)
            throw new InvalidOperationException("Usage: roslyn-cli <group> <command> <target> [options]");

        ValidateOutput(output);

        return new CliArguments(
            positionals[0],
            positionals[1],
            positionals[2],
            output,
            threshold,
            new RoslynCliQueryOptions
            {
                Symbol = symbol,
                Filter = filter,
                Visibility = visibility,
                Excludes = excludes.AsReadOnly(),
                File = file,
                Line = line,
            });
    }

    private static string ReadNextValue(IReadOnlyList<string> args, ref int index, string option)
    {
        if (index + 1 >= args.Count)
            throw new InvalidOperationException($"Option '{option}' requires a value.");

        index++;
        return args[index];
    }

    private static void ValidateOutput(string output)
    {
        if (output is "json" or "text" or "mcp")
            return;

        throw new InvalidOperationException("--output must be one of: json, text, mcp.");
    }

    private static bool ShouldShowHelp(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
            return true;

        return args.Any(static arg => arg is "--help" or "-h")
            || (args.Count == 1 && string.Equals(args[0], "help", StringComparison.OrdinalIgnoreCase));
    }

    private static string[] NormalizeArguments(string[] args)
    {
        var firstNonOptionIndex = Array.FindIndex(args, static token => !token.StartsWith("-", StringComparison.Ordinal));
        if (firstNonOptionIndex < 0)
            return args;

        if (!LooksLikeTarget(args[firstNonOptionIndex]))
            return args;

        if (args.Length <= firstNonOptionIndex + 2)
            return args;

        if (args[firstNonOptionIndex + 1].StartsWith("-", StringComparison.Ordinal)
            || args[firstNonOptionIndex + 2].StartsWith("-", StringComparison.Ordinal))
        {
            return args;
        }

        var normalized = new List<string>(args.Length);
        normalized.AddRange(args.Take(firstNonOptionIndex));
        normalized.Add(args[firstNonOptionIndex + 1]);
        normalized.Add(args[firstNonOptionIndex + 2]);
        normalized.Add(args[firstNonOptionIndex]);

        normalized.AddRange(args.Skip(firstNonOptionIndex + 3));
        return normalized.ToArray();
    }

    private static bool LooksLikeTarget(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var extension = Path.GetExtension(token);
        return extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".cs", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record CliArguments(
        string Group,
        string Command,
        string Target,
        string Output,
        int? Threshold,
        RoslynCliQueryOptions QueryOptions);
}
