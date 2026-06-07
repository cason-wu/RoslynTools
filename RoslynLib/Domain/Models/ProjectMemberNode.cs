using Microsoft.CodeAnalysis;
using RoslynLib.Infrastructure;

namespace RoslynLib.Domain.Models;

public sealed class ProjectMemberNode
{
    private readonly Lazy<string> _displayName;
    private readonly Lazy<SymbolNodeKind> _kind;
    private readonly Lazy<string> _typeName;
    private readonly Lazy<(string FilePath, string FileName, int LineNumber)> _source;
    private readonly Lazy<string> _tipText;

    public ProjectMemberNode(ISymbol symbol)
    {
        Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
        _displayName = new Lazy<string>(() => RoslynSymbolHelper.BuildMemberDisplayName(Symbol));
        _kind = new Lazy<SymbolNodeKind>(() => RoslynSymbolHelper.GetKind(Symbol));
        _typeName = new Lazy<string>(() => RoslynSymbolHelper.BuildMemberTypeName(Symbol));
        _source = new Lazy<(string FilePath, string FileName, int LineNumber)>(() => RoslynSymbolHelper.GetSourceLocation(Symbol));
        _tipText = new Lazy<string>(BuildTipText);
    }

    public ISymbol Symbol { get; }
    public string Name => _displayName.Value;
    public SymbolNodeKind Kind => _kind.Value;
    public string TypeName => _typeName.Value;
    public string Accessibility => Symbol.DeclaredAccessibility.ToString();
    public bool IsStatic => Symbol.IsStatic;
    public string FilePath => _source.Value.FilePath;
    public string FileName => _source.Value.FileName;
    public int LineNumber => _source.Value.LineNumber;
    public string TipText => _tipText.Value;

    private string BuildTipText()
    {
        if (string.IsNullOrWhiteSpace(FileName))
            return Symbol.ToDisplayString();

        return LineNumber > 0
            ? $"{Symbol.ToDisplayString()}{Environment.NewLine}Source: {FileName}:{LineNumber}"
            : $"{Symbol.ToDisplayString()}{Environment.NewLine}Source: {FileName}";
    }
}