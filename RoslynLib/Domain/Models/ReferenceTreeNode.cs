using Microsoft.CodeAnalysis;

namespace RoslynLib.Domain.Models;

public enum ReferenceKind
{
    Invocation,
    Read,
    Write,
    TypeOf,
    NameOf,
    Other,
}

public sealed class ReferenceTreeNode : RoslynTreeNode
{
    public ISymbol? TargetSymbol { get; init; }
    public bool IsFileGroup { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public int LineNumber { get; init; }
    public int Column { get; init; }
    public ReferenceKind ReferenceKind { get; init; }
    public string LineText { get; init; } = string.Empty;
    /// <summary>Formatted enclosing member name (method with param types, property, field, etc.).</summary>
    public string ContainingSymbol { get; init; } = string.Empty;
    public string ContainingNamespace { get; init; } = string.Empty;
    public string ContainingTypeName { get; init; } = string.Empty;
    public string ContainingContext { get; init; } = string.Empty;
    public string ProjectName { get; init; } = string.Empty;
    public string AssemblyName { get; init; } = string.Empty;
    public string Accessibility { get; init; } = string.Empty;
    public int OccurrenceCount { get; init; }
    public string LocationDisplay { get; init; } = string.Empty;
}
