namespace RoslynCLI;

internal static class CliHelpText
{
    public static string Build()
    {
        return """
            roslyn-cli — Roslyn powered .NET symbol analysis CLI

            Usage
              roslyn-cli <group> <command> <target> [options]
              roslyn-cli <target> <group> <command> [options]

            Groups and commands
              refs callers|usages|impls|overrides
              deps graph|cycles|nuget|namespaces
              sym  info|search|list|hierarchy
              dead unused|complexity|obsolete
              diag errors|warnings|analyze

            Common options
              --output json|text|mcp    Output format (default: json)
              --symbol <name>           Symbol name or fully qualified name
              --filter <kind>           class|method|property|event|field|interface|struct|enum|delegate|namespace
              --visibility <level>      public|internal|private|all (default: all)
              --exclude <pattern>       Exclude glob-like path pattern (repeatable)
              --file <path>             Resolve symbol by file
              --line <number>           Resolve symbol by 1-based line number
              --threshold <number>      Complexity threshold for dead complexity (default: 10)
              --help, -h                Show this help text

            Examples
              roslyn-cli refs callers MyApp.sln --symbol "OrderService.CalculateTotal"
              roslyn-cli MyApp.sln sym search --symbol "*Service" --filter class --output text
              roslyn-cli diag errors MyApp.slnx --output json
            """;
    }
}