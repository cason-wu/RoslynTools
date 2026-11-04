using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.Imaging.Interop;
using System.Collections.ObjectModel;
using RoslynTree.Infrastructure;
using RoslynLib.Domain.Models;

namespace RoslynTree.ViewModels;

/// <summary>
/// Represents a single type entry in the project browser.
/// </summary>
public partial class NodeTypeViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    public ProjectNode Node { get; }
    public string Name => Node.DisplayName;
    public string FullName => Node.FullName;
    public SymbolNodeKind Kind => Node.Kind;
    public string SourceFileName => Node.SourceFileName;
    public string ProjectName => Node.ProjectName;
    public string MetadataText => _metadataText.Value;
    public string TipText => _tipText.Value;

    public ImageMoniker Icon { get; }

    public ObservableCollection<NodeMemberViewModel> Members { get; } = [];
    private readonly INamedTypeSymbol _typeSymbol;
    private readonly Lazy<string> _metadataText;
    private readonly Lazy<string> _tipText;

    public NodeTypeViewModel(ProjectNode node)
    {
        this.Node = node;
        _typeSymbol = node.Symbol;
        _metadataText = new Lazy<string>(BuildMetadataText);
        _tipText = new Lazy<string>(BuildTipText);

        foreach (var member in node.Members)
            Members.Add(new NodeMemberViewModel(member));

        Icon = VisualStudioSymbolIconMap.GetIcon(Kind, _typeSymbol.DeclaredAccessibility);
    }

    private string BuildMetadataText()
    {
        var parts = new[]
        {
            string.IsNullOrWhiteSpace(Node.ProjectName) ? null : Node.ProjectName,
            string.IsNullOrWhiteSpace(Node.SourceDisplay) ? null : Node.SourceDisplay,
        };

        return string.Join(" • ", parts.Where(static value => !string.IsNullOrWhiteSpace(value))!);
    }

    private string BuildTipText()
    {
        var lines = new List<string>
        {
            Node.FullName,
        };

        if (!string.IsNullOrWhiteSpace(Node.ProjectName))
            lines.Add($"Project: {Node.ProjectName}");

        if (!string.IsNullOrWhiteSpace(Node.AssemblyName))
            lines.Add($"Assembly: {Node.AssemblyName}");

        if (!string.IsNullOrWhiteSpace(Node.SourceDisplay))
            lines.Add($"Source: {Node.SourceDisplay}");

        return string.Join(Environment.NewLine, lines);
    }
}
