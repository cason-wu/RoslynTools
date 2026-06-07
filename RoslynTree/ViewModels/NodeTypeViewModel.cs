using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.Imaging.Interop;
using System.Collections.ObjectModel;
using System.Linq;
using RoslynTree.Infrastructure;
using RoslynLib.Domain.Models;
using RoslynLib.Infrastructure;

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
    public string Name => BuildTypeDisplayName();
    public string FullName => Node.FullName;
    public SymbolNodeKind Kind => Node.Kind;
    public string SourceFileName => Node.SourceFileName;
    public string TipText => string.IsNullOrWhiteSpace(SourceFileName)
        ? FullName
        : $"{FullName}{Environment.NewLine}Source: {SourceFileName}";

    public ImageMoniker Icon { get; }

    public ObservableCollection<NodeMemberViewModel> Members { get; } = [];
    private readonly INamedTypeSymbol _typeSymbol;

    public NodeTypeViewModel(ProjectNode node)
    {
        this.Node = node;
        _typeSymbol = node.Symbol;

        PopulateMembers(_typeSymbol);
        Icon = VisualStudioSymbolIconMap.GetIcon(Kind, _typeSymbol.DeclaredAccessibility);
    }

    private void PopulateMembers(INamedTypeSymbol type)
    {
        foreach (var member in type.GetMembers())
        {
            if (member.IsImplicitlyDeclared)
                continue;

            Members.Add(new NodeMemberViewModel(member));
        }
    }

    private string BuildTypeDisplayName()
    {
        if (_typeSymbol.TypeKind != TypeKind.Delegate)
            return Node.Name;

        var invoke = _typeSymbol.DelegateInvokeMethod;
        if (invoke is null)
            return Node.Name;

        var genericSuffix = _typeSymbol.TypeParameters.Length == 0
            ? string.Empty
            : $"<{string.Join(", ", _typeSymbol.TypeParameters.Select(tp => tp.Name))}>";

        var parameters = string.Join(", ", invoke.Parameters.Select(FormatParameter));
        var returnType = invoke.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        return $"{Node.Name}{genericSuffix}({parameters}) : {returnType}";
    }

    private static string FormatParameter(IParameterSymbol parameter)
    {
        var modifier = parameter.RefKind switch
        {
            RefKind.Ref => "ref ",
            RefKind.Out => "out ",
            RefKind.In => "in ",
            _ => string.Empty,
        };

        var paramsPrefix = parameter.IsParams ? "params " : string.Empty;
        var typeName = parameter.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        return $"{paramsPrefix}{modifier}{typeName}";
    }
}