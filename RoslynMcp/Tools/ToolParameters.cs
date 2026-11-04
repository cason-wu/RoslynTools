using System.Text.Json.Serialization;

namespace RoslynMcp.Tools;

[JsonConverter(typeof(JsonStringEnumConverter<SymbolKindFilter>))]
public enum SymbolKindFilter
{
    [JsonStringEnumMemberName("class")] Class,
    [JsonStringEnumMemberName("interface")] Interface,
    [JsonStringEnumMemberName("struct")] Struct,
    [JsonStringEnumMemberName("enum")] Enum,
    [JsonStringEnumMemberName("delegate")] Delegate,
    [JsonStringEnumMemberName("method")] Method,
    [JsonStringEnumMemberName("property")] Property,
    [JsonStringEnumMemberName("field")] Field,
    [JsonStringEnumMemberName("event")] Event,
    [JsonStringEnumMemberName("namespace")] Namespace,
}

[JsonConverter(typeof(JsonStringEnumConverter<VisibilityFilter>))]
public enum VisibilityFilter
{
    [JsonStringEnumMemberName("all")] All,
    [JsonStringEnumMemberName("public")] Public,
    [JsonStringEnumMemberName("internal")] Internal,
    [JsonStringEnumMemberName("private")] Private,
}

[JsonConverter(typeof(JsonStringEnumConverter<DiagnosticSeverityFilter>))]
public enum DiagnosticSeverityFilter
{
    [JsonStringEnumMemberName("error")] Error,
    [JsonStringEnumMemberName("warning")] Warning,
    [JsonStringEnumMemberName("all")] All,
}

internal static class ToolParameterText
{
    public const string Symbol =
        "Symbol to analyze. Accepts a simple name ('OrderService'), Type.Member ('OrderService.Calculate'), "
        + "a signature to pick an overload ('OrderService.Calculate(int, string)'), or a fully qualified name "
        + "('MyApp.Services.OrderService'). Ambiguous names return an error listing the candidates. "
        + "Optional when file and line are provided.";

    public const string File =
        "Source file used with line (and optionally column) to select the symbol at that position. "
        + "Absolute path, or relative to the loaded target directory.";

    public const string Line = "1-based line number; requires file.";

    public const string Column =
        "Optional 1-based column on that line. Use it when a line contains several symbols (for example 'line:column' from a diagnostic).";

    public const string Exclude =
        "Wildcard patterns ('*' and '?') matched against the full file path or the file name; matching files are omitted. "
        + "Examples: '*Tests*', '*/obj/*', '*.g.cs'.";

    public const string Filter = "Restrict results to one symbol kind.";

    public const string Visibility =
        "Restrict results by declared accessibility. 'private' also includes protected; 'internal' includes protected internal.";

    public const string MaxResults =
        "Maximum number of items to return (default 100; 0 means no limit). totalCount always reports the full count and truncated tells whether items were cut.";

    public const string Project =
        "Optional wildcard pattern matched against project names (see get_loaded_target for the list).";
}
