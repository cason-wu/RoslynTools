using Microsoft.CodeAnalysis;
using System.IO;
using RoslynLib.Domain.Models;

namespace RoslynTree.ViewModels;

/// <summary>
/// ViewModel for a single node in the Call Tree.
/// Wraps a <see cref="CallTreeNode"/> and exposes call-specific properties.
/// </summary>
public sealed partial class CallTreeNodeViewModel : SymbolTreeNodeViewModel
{
    private readonly CallTreeNode _callModel;

    public CallTreeNode CallTreeModel => _callModel;
    public string ContainingType => _callModel.ContainingType;

    public override string Name => BuildName();

    public CallTreeNodeViewModel(
        CallTreeNode model,
        Func<SymbolTreeNodeViewModel, CancellationToken, Task<IReadOnlyList<RoslynTreeNode>>>? childrenLoader,
        SymbolTreeNodeViewModel? parent = null)
        : base(model, childrenLoader, parent)
    {
        _callModel = model;
    }

    protected override SymbolTreeNodeViewModel CreateChildViewModel(RoslynTreeNode child, SymbolTreeNodeViewModel parent)
        => new CallTreeNodeViewModel((CallTreeNode)child, _childrenLoader, parent);

    private string BuildName()
    {
        if (Kind != SymbolNodeKind.Method)
            return _callModel.Name;

        var methodPart = BuildMethodSignature(_callModel);
        var sourcePart = BuildSourceText(_callModel.FilePath, _callModel.LineNumber);

        return string.IsNullOrEmpty(sourcePart)
            ? methodPart
            : $"{methodPart} - {sourcePart}";
    }

    private static string BuildMethodSignature(CallTreeNode node)
    {
        var method = node.MethodSymbol;
        if (method is null)
            return node.Name;

        var parameterTypes = string.Join(", ",
            method.Parameters.Select(p => p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));

        return $"{method.Name}({parameterTypes})";
    }

    private static string BuildSourceText(string filePath, int lineNumber)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return string.Empty;

        var fileName = Path.GetFileName(filePath);
        if (string.IsNullOrWhiteSpace(fileName))
            return string.Empty;

        return lineNumber > 0
            ? $"{fileName}:{lineNumber}"
            : fileName;
    }
}
