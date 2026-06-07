using Microsoft.CodeAnalysis;

namespace RoslynLib.Domain.Models;

public sealed class CallTreeNode : RoslynTreeNode
{
    public IMethodSymbol? MethodSymbol { get; init; }
    public string ContainingType { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public int LineNumber { get; init; }
    public bool IsCallee { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string SourceDisplay { get; init; } = string.Empty;
}
