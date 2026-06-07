using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using System.Globalization;
using System.IO;
using System.Resources;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RoslynTree.Converters;

public sealed class ImageMonikerToImageSourceConverter : IValueConverter
{
    private static readonly object _lock = new();
    private static Dictionary<int, ImageSource>? _cache;

    private static readonly Dictionary<int, string> _resourceMap = new()
    {
        [KnownMonikers.FolderClosed.Id] = "png/folderclosed.16.16.png",
        [KnownMonikers.Namespace.Id] = "png/namespace.16.16.png",

        [KnownMonikers.ClassPublic.Id] = "png/classpublic.16.16.png",
        [KnownMonikers.ClassInternal.Id] = "png/classinternal.16.16.png",
        [KnownMonikers.ClassProtected.Id] = "png/classprotected.16.16.png",
        [KnownMonikers.ClassPrivate.Id] = "png/classprivate.16.16.png",

        [KnownMonikers.InterfacePublic.Id] = "png/interfacepublic.16.16.png",
        [KnownMonikers.InterfaceInternal.Id] = "png/interfaceinternal.16.16.png",
        [KnownMonikers.InterfaceProtected.Id] = "png/interfaceprotected.16.16.png",
        [KnownMonikers.InterfacePrivate.Id] = "png/interfaceprivate.16.16.png",

        [KnownMonikers.StructurePublic.Id] = "png/structurepublic.16.16.png",
        [KnownMonikers.StructureInternal.Id] = "png/structureinternal.16.16.png",
        [KnownMonikers.StructureProtected.Id] = "png/structureprotected.16.16.png",
        [KnownMonikers.StructurePrivate.Id] = "png/structureprivate.16.16.png",

        [KnownMonikers.EnumerationPublic.Id] = "png/enumerationpublic.16.16.png",
        [KnownMonikers.EnumerationInternal.Id] = "png/enumerationinternal.16.16.png",
        [KnownMonikers.EnumerationProtected.Id] = "png/enumerationprotected.16.16.png",
        [KnownMonikers.EnumerationPrivate.Id] = "png/enumerationprivate.16.16.png",

        [KnownMonikers.DelegatePublic.Id] = "png/delegatepublic.16.16.png",
        [KnownMonikers.DelegateInternal.Id] = "png/delegateinternal.16.16.png",
        [KnownMonikers.DelegateProtected.Id] = "png/delegateprotected.16.16.png",
        [KnownMonikers.DelegatePrivate.Id] = "png/delegateprivate.16.16.png",

        [KnownMonikers.MethodPublic.Id] = "png/methodpublic.16.16.png",
        [KnownMonikers.MethodInternal.Id] = "png/methodinternal.16.16.png",
        [KnownMonikers.MethodProtected.Id] = "png/methodprotected.16.16.png",
        [KnownMonikers.MethodPrivate.Id] = "png/methodprivate.16.16.png",

        [KnownMonikers.PropertyPublic.Id] = "png/propertypublic.16.16.png",
        [KnownMonikers.PropertyInternal.Id] = "png/propertyinternal.16.16.png",
        [KnownMonikers.PropertyProtected.Id] = "png/propertyprotected.16.16.png",
        [KnownMonikers.PropertyPrivate.Id] = "png/propertyprivate.16.16.png",

        [KnownMonikers.FieldPublic.Id] = "png/fieldpublic.16.16.png",
        [KnownMonikers.FieldInternal.Id] = "png/fieldinternal.16.16.png",
        [KnownMonikers.FieldProtected.Id] = "png/fieldprotected.16.16.png",
        [KnownMonikers.FieldPrivate.Id] = "png/fieldprivate.16.16.png",

        [KnownMonikers.EventPublic.Id] = "png/eventpublic.16.16.png",
        [KnownMonikers.EventInternal.Id] = "png/eventinternal.16.16.png",
        [KnownMonikers.EventProtected.Id] = "png/eventprotected.16.16.png",
        [KnownMonikers.EventPrivate.Id] = "png/eventprivate.16.16.png",

        [KnownMonikers.Parameter.Id] = "png/parameter.16.16.png",
        [KnownMonikers.LocalVariable.Id] = "png/localvariable.16.16.png",
        [KnownMonikers.Reference.Id] = "png/reference.16.16.png",
        [KnownMonikers.QuestionMark.Id] = "png/questionmark.16.16.png",
    };

    private static readonly Dictionary<int, int> _fallbackMonikerIdMap = new()
    {
        [KnownMonikers.Namespace.Id] = KnownMonikers.FolderClosed.Id,
        [KnownMonikers.ClassInternal.Id] = KnownMonikers.ClassPublic.Id,
        [KnownMonikers.ClassProtected.Id] = KnownMonikers.ClassPublic.Id,
        [KnownMonikers.ClassPrivate.Id] = KnownMonikers.ClassPublic.Id,
        [KnownMonikers.InterfaceInternal.Id] = KnownMonikers.InterfacePublic.Id,
        [KnownMonikers.InterfaceProtected.Id] = KnownMonikers.InterfacePublic.Id,
        [KnownMonikers.InterfacePrivate.Id] = KnownMonikers.InterfacePublic.Id,
        [KnownMonikers.StructureInternal.Id] = KnownMonikers.StructurePublic.Id,
        [KnownMonikers.StructureProtected.Id] = KnownMonikers.StructurePublic.Id,
        [KnownMonikers.StructurePrivate.Id] = KnownMonikers.StructurePublic.Id,
        [KnownMonikers.EnumerationInternal.Id] = KnownMonikers.EnumerationPublic.Id,
        [KnownMonikers.EnumerationProtected.Id] = KnownMonikers.EnumerationPublic.Id,
        [KnownMonikers.EnumerationPrivate.Id] = KnownMonikers.EnumerationPublic.Id,
        [KnownMonikers.DelegateInternal.Id] = KnownMonikers.DelegatePublic.Id,
        [KnownMonikers.DelegateProtected.Id] = KnownMonikers.DelegatePublic.Id,
        [KnownMonikers.DelegatePrivate.Id] = KnownMonikers.DelegatePublic.Id,
        [KnownMonikers.MethodInternal.Id] = KnownMonikers.MethodPublic.Id,
        [KnownMonikers.MethodProtected.Id] = KnownMonikers.MethodPublic.Id,
        [KnownMonikers.MethodPrivate.Id] = KnownMonikers.MethodPublic.Id,
        [KnownMonikers.PropertyInternal.Id] = KnownMonikers.PropertyPublic.Id,
        [KnownMonikers.PropertyProtected.Id] = KnownMonikers.PropertyPublic.Id,
        [KnownMonikers.PropertyPrivate.Id] = KnownMonikers.PropertyPublic.Id,
        [KnownMonikers.FieldInternal.Id] = KnownMonikers.FieldPublic.Id,
        [KnownMonikers.FieldProtected.Id] = KnownMonikers.FieldPublic.Id,
        [KnownMonikers.FieldPrivate.Id] = KnownMonikers.FieldPublic.Id,
        [KnownMonikers.EventInternal.Id] = KnownMonikers.EventPublic.Id,
        [KnownMonikers.EventProtected.Id] = KnownMonikers.EventPublic.Id,
        [KnownMonikers.EventPrivate.Id] = KnownMonikers.EventPublic.Id,
    };

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not ImageMoniker moniker)
            return GetFallbackImage();

        EnsureCacheInitialized();

        if (_cache!.TryGetValue(moniker.Id, out var image))
            return image;

        return GetFallbackImage();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;

    private static void EnsureCacheInitialized()
    {
        if (_cache is not null)
            return;

        lock (_lock)
        {
            if (_cache is not null)
                return;

            _cache = new Dictionary<int, ImageSource>();

            var resourceManager = new ResourceManager("Microsoft.VisualStudio.ImageCatalog.g", typeof(KnownMonikers).Assembly);

            foreach (var pair in _resourceMap)
            {
                var bitmap = LoadBitmapFromResource(resourceManager, pair.Value);
                if (bitmap is null)
                    continue;

                _cache[pair.Key] = bitmap;
            }

            ApplyMonikerFallbackAliases();
        }
    }

    private static BitmapImage? LoadBitmapFromResource(ResourceManager resourceManager, string resourceKey)
    {
        foreach (var culture in GetResourceCultureFallbackChain())
        {
            object? resource;
            try
            {
                resource = resourceManager.GetObject(resourceKey, culture);
            }
            catch
            {
                continue;
            }

            if (!TryReadResourceBytes(resource, out var bytes))
                continue;

            var bitmap = LoadBitmap(bytes);
            if (bitmap is not null)
                return bitmap;
        }

        return null;
    }

    private static IEnumerable<CultureInfo> GetResourceCultureFallbackChain()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        yield return CultureInfo.GetCultureInfo("en");
        seen.Add("en");

        var culture = CultureInfo.CurrentUICulture;
        while (!ReferenceEquals(culture, CultureInfo.InvariantCulture))
        {
            if (seen.Add(culture.Name))
                yield return culture;

            culture = culture.Parent;
        }

        if (seen.Add(string.Empty))
            yield return CultureInfo.InvariantCulture;
    }

    private static void ApplyMonikerFallbackAliases()
    {
        if (_cache is null)
            return;

        foreach (var pair in _fallbackMonikerIdMap)
        {
            if (_cache.ContainsKey(pair.Key))
                continue;

            if (_cache.TryGetValue(pair.Value, out var fallbackImage))
                _cache[pair.Key] = fallbackImage;
        }
    }

    private static ImageSource GetFallbackImage()
    {
        EnsureCacheInitialized();
        return _cache is not null && _cache.TryGetValue(KnownMonikers.QuestionMark.Id, out var fallback)
            ? fallback
            : new DrawingImage();
    }

    private static BitmapImage? LoadBitmap(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = ms;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryReadResourceBytes(object? value, out byte[] bytes)
    {
        if (value is null)
        {
            bytes = Array.Empty<byte>();
            return false;
        }

        if (value is byte[] rawBytes)
        {
            bytes = rawBytes;
            return true;
        }

        if (value is UnmanagedMemoryStream ums)
            return TryReadStream(ums, out bytes);

        if (value is Stream stream)
            return TryReadStream(stream, out bytes);

        bytes = Array.Empty<byte>();
        return false;
    }

    private static bool TryReadStream(Stream stream, out byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            bytes = ms.ToArray();
            return bytes.Length > 0;
        }
        catch
        {
            bytes = Array.Empty<byte>();
            return false;
        }
    }
}