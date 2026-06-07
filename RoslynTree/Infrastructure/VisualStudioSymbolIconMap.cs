using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using RoslynLib.Domain.Models;
using RoslynLib.Infrastructure;

namespace RoslynTree.Infrastructure;

public static class VisualStudioSymbolIconMap
{
    private readonly struct IconSet
    {
        public IconSet(ImageMoniker publicIcon, ImageMoniker internalIcon, ImageMoniker protectedIcon, ImageMoniker privateIcon)
        {
            PublicIcon = publicIcon;
            InternalIcon = internalIcon;
            ProtectedIcon = protectedIcon;
            PrivateIcon = privateIcon;
        }

        public ImageMoniker PublicIcon { get; }
        public ImageMoniker InternalIcon { get; }
        public ImageMoniker ProtectedIcon { get; }
        public ImageMoniker PrivateIcon { get; }
    }

    // Use Visual Studio SDK native monikers.
    public static ImageMoniker GetIcon(SymbolNodeKind kind) => kind switch
    {
        SymbolNodeKind.Namespace     => KnownMonikers.Namespace,
        SymbolNodeKind.Class         => KnownMonikers.ClassPublic,
        SymbolNodeKind.Interface     => KnownMonikers.InterfacePublic,
        SymbolNodeKind.Struct        => KnownMonikers.StructurePublic,
        SymbolNodeKind.Enum          => KnownMonikers.EnumerationPublic,
        SymbolNodeKind.Delegate      => KnownMonikers.DelegatePublic,
        SymbolNodeKind.Method        => KnownMonikers.MethodPublic,
        SymbolNodeKind.Constructor   => KnownMonikers.MethodPublic,
        SymbolNodeKind.Property      => KnownMonikers.PropertyPublic,
        SymbolNodeKind.Field         => KnownMonikers.FieldPublic,
        SymbolNodeKind.Event         => KnownMonikers.EventPublic,
        SymbolNodeKind.Parameter     => KnownMonikers.Parameter,
        SymbolNodeKind.LocalVariable => KnownMonikers.LocalVariable,
        SymbolNodeKind.Reference     => KnownMonikers.Reference,
        _                            => KnownMonikers.QuestionMark,
    };

    public static SymbolNodeKind GetKind(ISymbol symbol)
        => RoslynSymbolHelper.GetKind(symbol);

    public static ImageMoniker GetIcon(SymbolNodeKind kind, Microsoft.CodeAnalysis.Accessibility accessibility)
    {
        if (!TryGetIconSet(kind, out var iconSet))
            return GetIcon(kind);

        return SelectByAccessibility(accessibility, iconSet);
    }

    private static bool TryGetIconSet(SymbolNodeKind kind, out IconSet iconSet)
    {
        switch (kind)
        {
            case SymbolNodeKind.Class:
                iconSet = new IconSet(KnownMonikers.ClassPublic, KnownMonikers.ClassInternal, KnownMonikers.ClassProtected, KnownMonikers.ClassPrivate);
                return true;
            case SymbolNodeKind.Interface:
                iconSet = new IconSet(KnownMonikers.InterfacePublic, KnownMonikers.InterfaceInternal, KnownMonikers.InterfaceProtected, KnownMonikers.InterfacePrivate);
                return true;
            case SymbolNodeKind.Struct:
                iconSet = new IconSet(KnownMonikers.StructurePublic, KnownMonikers.StructureInternal, KnownMonikers.StructureProtected, KnownMonikers.StructurePrivate);
                return true;
            case SymbolNodeKind.Enum:
                iconSet = new IconSet(KnownMonikers.EnumerationPublic, KnownMonikers.EnumerationInternal, KnownMonikers.EnumerationProtected, KnownMonikers.EnumerationPrivate);
                return true;
            case SymbolNodeKind.Delegate:
                iconSet = new IconSet(KnownMonikers.DelegatePublic, KnownMonikers.DelegateInternal, KnownMonikers.DelegateProtected, KnownMonikers.DelegatePrivate);
                return true;
            case SymbolNodeKind.Method:
            case SymbolNodeKind.Constructor:
                iconSet = new IconSet(KnownMonikers.MethodPublic, KnownMonikers.MethodInternal, KnownMonikers.MethodProtected, KnownMonikers.MethodPrivate);
                return true;
            case SymbolNodeKind.Property:
                iconSet = new IconSet(KnownMonikers.PropertyPublic, KnownMonikers.PropertyInternal, KnownMonikers.PropertyProtected, KnownMonikers.PropertyPrivate);
                return true;
            case SymbolNodeKind.Field:
                iconSet = new IconSet(KnownMonikers.FieldPublic, KnownMonikers.FieldInternal, KnownMonikers.FieldProtected, KnownMonikers.FieldPrivate);
                return true;
            case SymbolNodeKind.Event:
                iconSet = new IconSet(KnownMonikers.EventPublic, KnownMonikers.EventInternal, KnownMonikers.EventProtected, KnownMonikers.EventPrivate);
                return true;
            default:
                iconSet = default;
                return false;
        }
    }

    private static ImageMoniker SelectByAccessibility(Microsoft.CodeAnalysis.Accessibility accessibility, IconSet iconSet)
    {
        return accessibility switch
        {
            Microsoft.CodeAnalysis.Accessibility.Public => iconSet.PublicIcon,
            Microsoft.CodeAnalysis.Accessibility.Internal => iconSet.InternalIcon,
            Microsoft.CodeAnalysis.Accessibility.Protected => iconSet.ProtectedIcon,
            Microsoft.CodeAnalysis.Accessibility.Private => iconSet.PrivateIcon,
            Microsoft.CodeAnalysis.Accessibility.ProtectedOrInternal => iconSet.ProtectedIcon,
            Microsoft.CodeAnalysis.Accessibility.ProtectedAndInternal => iconSet.ProtectedIcon,
            _ => iconSet.PublicIcon,
        };
    }

}