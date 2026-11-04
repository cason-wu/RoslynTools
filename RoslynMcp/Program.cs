using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RoslynLib.Infrastructure;
using RoslynMcp.Tools;

internal class Program
{
    private static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // Keep stdio quiet so MCP clients only receive protocol messages on the process streams.
        builder.Logging.ClearProviders();

        builder.Services
            .AddSingleton<RoslynWorkspaceLoader>()
            .AddSingleton<RoslynCliAnalysisService>()
            .AddSingleton<RoslynWorkspaceTargetSession>();

        builder.Services
            .AddMcpServer(options => options.ServerInstructions = ServerInstructions)
            .WithStdioServerTransport()
            .WithTools<RoslynAnalysisTools>();

        await builder.Build().RunAsync();
    }

    private const string ServerInstructions = """
        Roslyn semantic analysis for C# / .NET code: callers, usages, implementations, hierarchy, symbol search, diagnostics.
        Prefer these tools over text search when you need compiler-accurate answers (overloads, inheritance, interface dispatch).

        Workflow:
        1. Call load_target once with an absolute path to a .sln/.slnx/.csproj (or a single .cs file). Loading a solution can take several seconds.
        2. Run any analysis tool. Edits to files on disk are picked up automatically before each query; you do not need to reload after editing.
           Adding/removing files or editing project files triggers an automatic full reload.
        3. After editing a file, call get_diagnostics with file=<path> to check that document for compiler errors quickly.

        Symbols:
        - Pass a simple name, Type.Member, a signature such as Type.Method(int, string), or a fully qualified name.
        - If a name is ambiguous the tool fails and lists the candidates; retry with one of the listed signatures, or select by file + line (+ column).
        - Use search_symbols with wildcards ('*Service', 'Order*') when you do not know the exact name.

        Reading code:
        - get_document_symbols gives a file outline with line ranges; get_symbol_source returns just one declaration's code.
        - get_symbol_info returns the signature, XML doc summary, and declaration line ranges without the body.
        - find_callers (incoming) and find_callees (outgoing) navigate the call graph.

        Results:
        - File paths are relative to the target directory reported by load_target / get_loaded_target; lines and columns are 1-based.
        - List results are capped by maxResults (default 100). totalCount is the full count and truncated tells whether items were cut.
        """;

}
