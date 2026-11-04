using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.Imaging.Interop;
using RoslynTree.Infrastructure;
using RoslynLib.Domain.Models;

namespace RoslynTree.ViewModels;

/// <summary>
/// Represents a single member (method / property / field / event / ctor) inside a type.
/// </summary>
public sealed class NodeMemberViewModel
{
    public ProjectMemberNode Node { get; }
    public string Name => Node.Name;
    public SymbolNodeKind Kind => Node.Kind;
    public ImageMoniker Icon { get; }
    public string TypeName => Node.TypeName;
    public string Accessibility => Node.Accessibility;
    public bool IsStatic => Node.IsStatic;
    public ISymbol Symbol => Node.Symbol;
    public string FileName => Node.FileName;
    public int LineNumber => Node.LineNumber;
    public string ProjectName => Node.ProjectName;
    public string MetadataText => _metadataText.Value;
    public string TipText => _tipText.Value;
    private readonly Lazy<string> _metadataText;
    private readonly Lazy<string> _tipText;

    public NodeMemberViewModel(ProjectMemberNode node)
    {
        Node = node;
        _metadataText = new Lazy<string>(BuildMetadataText);
        _tipText = new Lazy<string>(BuildTipText);

        var memberAccessibility = node.Symbol.DeclaredAccessibility;
        Icon = VisualStudioSymbolIconMap.GetIcon(Kind, memberAccessibility);
    }

    private string BuildMetadataText()
    {
        var parts = new[]
        {
            string.IsNullOrWhiteSpace(Node.ProjectName) ? null : Node.ProjectName,
            string.IsNullOrWhiteSpace(Node.SourceDisplay) ? null : Node.SourceDisplay,
            string.IsNullOrWhiteSpace(Node.Accessibility) ? null : Node.Accessibility,
        };

        return string.Join(" • ", parts.Where(static value => !string.IsNullOrWhiteSpace(value))!);
    }

    private string BuildTipText()
    {
        var lines = new List<string>
        {
            Node.Symbol.ToDisplayString(),
        };

        if (!string.IsNullOrWhiteSpace(Node.ProjectName))
            lines.Add($"Project: {Node.ProjectName}");

        if (!string.IsNullOrWhiteSpace(Node.AssemblyName))
            lines.Add($"Assembly: {Node.AssemblyName}");

        if (!string.IsNullOrWhiteSpace(Node.ContainingTypeName))
            lines.Add($"Containing type: {Node.ContainingTypeName}");

        if (!string.IsNullOrWhiteSpace(Node.SourceDisplay))
            lines.Add($"Source: {Node.SourceDisplay}");

        return string.Join(Environment.NewLine, lines);
    }
}
