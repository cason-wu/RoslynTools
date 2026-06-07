using Microsoft.CodeAnalysis;
using RoslynLib.Infrastructure;

namespace RoslynLib.Domain.Models;

public sealed class ProjectMemberNode
{
    private readonly Lazy<string> _displayName;
    private readonly Lazy<SymbolNodeKind> _kind;
    private readonly Lazy<string> _typeName;
    private readonly Lazy<(string FilePath, string FileName, int LineNumber)> _source;
    private readonly Lazy<string> _sourceDisplay;
    private readonly Lazy<string> _metadataText;
    private readonly Lazy<string> _tipText;

    public ProjectMemberNode(
        ISymbol symbol,
        string projectName,
        string assemblyName)
    {
        Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
        ProjectName = projectName ?? throw new ArgumentNullException(nameof(projectName));
        AssemblyName = assemblyName ?? throw new ArgumentNullException(nameof(assemblyName));
        _displayName = new Lazy<string>(() => RoslynSymbolHelper.BuildMemberDisplayName(Symbol));
        _kind = new Lazy<SymbolNodeKind>(() => RoslynSymbolHelper.GetKind(Symbol));
        _typeName = new Lazy<string>(() => RoslynSymbolHelper.BuildMemberTypeName(Symbol));
        _source = new Lazy<(string FilePath, string FileName, int LineNumber)>(() => RoslynSymbolHelper.GetSourceLocation(Symbol));
        _sourceDisplay = new Lazy<string>(() => RoslynSymbolHelper.BuildSourceText(FilePath, LineNumber));
        _metadataText = new Lazy<string>(BuildMetadataText);
        _tipText = new Lazy<string>(BuildTipText);
    }

    public ISymbol Symbol { get; }
    public string ProjectName { get; }
    public string AssemblyName { get; }
    public string Name => _displayName.Value;
    public SymbolNodeKind Kind => _kind.Value;
    public string TypeName => _typeName.Value;
    public string Accessibility => Symbol.DeclaredAccessibility.ToString();
    public bool IsStatic => Symbol.IsStatic;
    public string FilePath => _source.Value.FilePath;
    public string FileName => _source.Value.FileName;
    public int LineNumber => _source.Value.LineNumber;
    public string SourceDisplay => _sourceDisplay.Value;
    public string ContainingTypeName => Symbol.ContainingType?.ToDisplayString() ?? string.Empty;
    public string MetadataText => _metadataText.Value;
    public string TipText => _tipText.Value;

    private string BuildMetadataText()
    {
        var parts = new[]
        {
            string.IsNullOrWhiteSpace(ProjectName) ? null : ProjectName,
            string.IsNullOrWhiteSpace(SourceDisplay) ? null : SourceDisplay,
            string.IsNullOrWhiteSpace(Accessibility) ? null : Accessibility,
        };

        return string.Join(" • ", parts.Where(static value => !string.IsNullOrWhiteSpace(value))!);
    }

    private string BuildTipText()
    {
        var lines = new List<string>
        {
            Symbol.ToDisplayString(),
        };

        if (!string.IsNullOrWhiteSpace(ProjectName))
            lines.Add($"Project: {ProjectName}");

        if (!string.IsNullOrWhiteSpace(AssemblyName))
            lines.Add($"Assembly: {AssemblyName}");

        if (!string.IsNullOrWhiteSpace(ContainingTypeName))
            lines.Add($"Containing type: {ContainingTypeName}");

        if (!string.IsNullOrWhiteSpace(SourceDisplay))
            lines.Add($"Source: {SourceDisplay}");

        return string.Join(Environment.NewLine, lines);
    }
}