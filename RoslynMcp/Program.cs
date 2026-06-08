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

        // Configure all logs to go to stderr (stdout is used for the MCP protocol messages).
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

        builder.Services
            .AddSingleton<RoslynWorkspaceLoader>()
            .AddSingleton<RoslynCliAnalysisService>()
            .AddSingleton<RoslynWorkspaceTargetSession>();

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<RoslynAnalysisTools>();

        await builder.Build().RunAsync();
    }

}
