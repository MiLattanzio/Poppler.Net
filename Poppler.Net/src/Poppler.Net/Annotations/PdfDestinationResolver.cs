using System.Collections.ObjectModel;
using Poppler.Core;
using Poppler.DocumentModel;

namespace Poppler.Annotations;

internal sealed class PdfDestinationResolver
{
    private readonly PdfDocumentCore _document;
    private readonly PdfDictionary _catalog;
    private readonly IReadOnlyList<PdfPageNode> _pages;
    private readonly Lazy<IReadOnlyDictionary<string, PdfObject>> _namedObjects;
    private readonly Lazy<IReadOnlyDictionary<string, PdfDestination>> _namedDestinations;

    public PdfDestinationResolver(
        PdfDocumentCore document,
        PdfDictionary catalog,
        IReadOnlyList<PdfPageNode> pages)
    {
        _document = document;
        _catalog = catalog;
        _pages = pages;
        _namedObjects = new Lazy<IReadOnlyDictionary<string, PdfObject>>(
            ReadNamedObjects);
        _namedDestinations = new Lazy<IReadOnlyDictionary<string, PdfDestination>>(
            ReadNamedDestinations);
    }

    public IReadOnlyDictionary<string, PdfDestination> NamedDestinations =>
        _namedDestinations.Value;

    public PdfDestination? Resolve(PdfObject? value, string? namedDestination = null)
        => Resolve(
            value,
            namedDestination,
            new HashSet<string>(StringComparer.Ordinal),
            depth: 0);

    public PdfDestination? ResolveNamed(string name) =>
        ResolveNamed(
            name,
            new HashSet<string>(StringComparer.Ordinal),
            depth: 0);

    private PdfDestination? Resolve(
        PdfObject? value,
        string? namedDestination,
        HashSet<string> activeNames,
        int depth)
    {
        if (value is null)
            return null;
        if (depth > _document.Options.MaximumTreeDepth)
            throw new PdfLimitException("Named-destination indirection is too deep.");
        PdfObject resolved = value.Resolve(_document);
        if (resolved is PdfName name)
            return ResolveNamed(name.Value, activeNames, depth + 1);
        if (resolved is PdfString text)
            return ResolveNamed(text.Text, activeNames, depth + 1);
        if (resolved is PdfDictionary dictionary)
        {
            return Resolve(
                dictionary.GetValueOrNull("D"),
                namedDestination,
                activeNames,
                depth + 1);
        }
        if (resolved is not PdfArray array || array.Count < 2)
            return null;

        int pageIndex = ResolvePageIndex(array[0]);
        if (pageIndex < 0)
            return null;
        string kindName = array[1].AsName(_document) ?? "";
        PdfDestinationType kind = kindName switch
        {
            "XYZ" => PdfDestinationType.Xyz,
            "Fit" => PdfDestinationType.Fit,
            "FitH" => PdfDestinationType.FitHorizontal,
            "FitV" => PdfDestinationType.FitVertical,
            "FitR" => PdfDestinationType.FitRectangle,
            "FitB" => PdfDestinationType.FitBoundingBox,
            "FitBH" => PdfDestinationType.FitBoundingBoxHorizontal,
            "FitBV" => PdfDestinationType.FitBoundingBoxVertical,
            _ => PdfDestinationType.Unknown
        };
        double? left = null;
        double? top = null;
        double? right = null;
        double? bottom = null;
        double? zoom = null;
        switch (kind)
        {
            case PdfDestinationType.Xyz:
                left = Number(array, 2);
                top = Number(array, 3);
                zoom = Number(array, 4);
                break;
            case PdfDestinationType.FitHorizontal:
            case PdfDestinationType.FitBoundingBoxHorizontal:
                top = Number(array, 2);
                break;
            case PdfDestinationType.FitVertical:
            case PdfDestinationType.FitBoundingBoxVertical:
                left = Number(array, 2);
                break;
            case PdfDestinationType.FitRectangle:
                left = Number(array, 2);
                bottom = Number(array, 3);
                right = Number(array, 4);
                top = Number(array, 5);
                break;
        }

        return new PdfDestination(
            pageIndex,
            kind,
            left,
            top,
            right,
            bottom,
            zoom,
            namedDestination);
    }

    private PdfDestination? ResolveNamed(
        string name,
        HashSet<string> activeNames,
        int depth)
    {
        if (!_namedObjects.Value.TryGetValue(name, out PdfObject? value))
            return null;
        if (!activeNames.Add(name))
        {
            _document.AddDiagnostic(
                PdfDiagnosticSeverity.Warning,
                "destination.named.circular",
                $"Circular named destination '{name}' was skipped.");
            return null;
        }
        try
        {
            PdfDestination? destination =
                Resolve(value, name, activeNames, depth + 1);
            if (destination is null ||
                destination.NamedDestination == name)
            {
                return destination;
            }
            return new PdfDestination(
                destination.PageIndex,
                destination.Type,
                destination.Left,
                destination.Top,
                destination.Right,
                destination.Bottom,
                destination.Zoom,
                name);
        }
        finally
        {
            activeNames.Remove(name);
        }
    }

    private IReadOnlyDictionary<string, PdfDestination> ReadNamedDestinations()
    {
        var result = new SortedDictionary<string, PdfDestination>(
            StringComparer.Ordinal);
        foreach (string name in _namedObjects.Value.Keys)
        {
            if (ResolveNamed(name) is { } destination)
                result[name] = destination;
        }

        return new ReadOnlyDictionary<string, PdfDestination>(result);
    }

    private IReadOnlyDictionary<string, PdfObject> ReadNamedObjects()
    {
        var result = new Dictionary<string, PdfObject>(StringComparer.Ordinal);
        if (_catalog.GetValueOrNull("Dests").AsDictionary(_document) is { } direct)
        {
            foreach ((string name, PdfObject value) in direct)
                result[name] = value;
        }

        PdfDictionary? names = _catalog.GetValueOrNull("Names").AsDictionary(_document);
        PdfObject? destinationTree = names?.GetValueOrNull("Dests");
        if (destinationTree is not null)
        {
            var active = new HashSet<PdfReference>();
            ReadNameTree(destinationTree, result, active, depth: 0);
        }

        return new ReadOnlyDictionary<string, PdfObject>(result);
    }

    private void ReadNameTree(
        PdfObject nodeObject,
        Dictionary<string, PdfObject> result,
        HashSet<PdfReference> active,
        int depth)
    {
        if (depth > _document.Options.MaximumTreeDepth)
            throw new PdfLimitException("Named-destination tree is too deep.");
        PdfReference? reference = nodeObject as PdfReference;
        if (reference is not null && !active.Add(reference))
            throw new PdfFormatException("Circular named-destination tree.");
        try
        {
            PdfDictionary? node = nodeObject.AsDictionary(_document);
            if (node is null)
                return;
            if (node.GetValueOrNull("Names").AsArray(_document) is { } names)
            {
                for (int index = 0; index + 1 < names.Count; index += 2)
                {
                    string? name = names[index].Resolve(_document) switch
                    {
                        PdfString text => text.Text,
                        PdfName pdfName => pdfName.Value,
                        _ => null
                    };
                    if (name is not null)
                        result[name] = names[index + 1];
                    if (result.Count > _document.Options.MaximumCollectionItems)
                    {
                        throw new PdfLimitException(
                            "Named-destination count exceeds the configured limit.");
                    }
                }
            }

            if (node.GetValueOrNull("Kids").AsArray(_document) is { } kids)
            {
                foreach (PdfObject kid in kids)
                    ReadNameTree(kid, result, active, depth + 1);
            }
        }
        finally
        {
            if (reference is not null)
                active.Remove(reference);
        }
    }

    private int ResolvePageIndex(PdfObject value)
    {
        if (value is PdfReference reference)
        {
            for (int index = 0; index < _pages.Count; index++)
            {
                if (_pages[index].SourceReference?.Equals(reference) == true)
                    return index;
            }
        }

        PdfObject resolved = value.Resolve(_document);
        if (resolved is PdfNumber { IsInteger: true } number &&
            number.Value is >= 0 and <= int.MaxValue)
        {
            int index = (int)number.Value;
            return index < _pages.Count ? index : -1;
        }
        if (resolved is PdfDictionary dictionary)
        {
            for (int index = 0; index < _pages.Count; index++)
            {
                if (ReferenceEquals(_pages[index].Dictionary, dictionary))
                    return index;
            }
        }

        return -1;
    }

    private double? Number(PdfArray array, int index)
    {
        double? value =
            index < array.Count ? array[index].AsNumber(_document) : null;
        return value is { } number && double.IsFinite(number) ? number : null;
    }
}
