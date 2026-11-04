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
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<RoslynAnalysisTools>();

        await builder.Build().RunAsync();
    }

}
