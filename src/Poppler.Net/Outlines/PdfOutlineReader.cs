using System.Collections.ObjectModel;
using Poppler.Annotations;
using Poppler.Core;

namespace Poppler.Outlines;

internal static class PdfOutlineReader
{
    public static IReadOnlyList<PdfOutlineItem> Read(
        PdfDocumentCore document,
        PdfDictionary catalog,
        PdfDestinationResolver destinations)
    {
        PdfObject? rootSource = catalog.GetValueOrNull("Outlines");
        PdfDictionary? root = rootSource.AsDictionary(document);
        PdfObject? first = root?.GetValueOrNull("First");
        if (root is null || first is null)
            return Array.Empty<PdfOutlineItem>();

        var topLevel = new List<ItemBuilder>();
        var tasks = new Stack<ChainTask>();
        tasks.Push(new ChainTask(
            first,
            root.GetValueOrNull("Last"),
            rootSource!,
            topLevel,
            Depth: 1));

        var seenReferences = new HashSet<PdfReference>();
        var seenDictionaries = new HashSet<PdfDictionary>(
            ReferenceEqualityComparer.Instance);
        var actionReader = new PdfAnnotationReader.PdfActionReader(
            document,
            destinations,
            "outline");
        int itemCount = 0;

        while (tasks.Count > 0)
        {
            ChainTask task = tasks.Pop();
            if (task.Depth > document.Options.MaximumOutlineDepth)
            {
                throw new PdfLimitException(
                    "PDF outline depth exceeds the configured limit.");
            }

            PdfObject? current = task.First;
            PdfObject? previous = null;
            PdfObject? actualLast = null;
            var childTasks = new List<ChainTask>();
            while (current is not null)
            {
                PdfDictionary? dictionary = current.AsDictionary(document);
                if (dictionary is null)
                {
                    document.AddDiagnosticOnce(
                        PdfDiagnosticSeverity.Warning,
                        "outline.node.invalid",
                        "An outline item that is not a dictionary was skipped.");
                    break;
                }
                if (!MarkSeen(current, dictionary, seenReferences, seenDictionaries))
                {
                    document.AddDiagnosticOnce(
                        PdfDiagnosticSeverity.Warning,
                        "outline.node.repeated",
                        "A circular or repeated outline item was skipped.");
                    break;
                }
                if (++itemCount > document.Options.MaximumOutlineItems)
                {
                    throw new PdfLimitException(
                        "PDF outline item count exceeds the configured limit.");
                }

                ValidateLink(
                    dictionary.GetValueOrNull("Parent"),
                    task.Parent,
                    document,
                    "outline.parent.mismatch",
                    "An outline item has an inconsistent /Parent link.");
                if (previous is not null)
                {
                    ValidateLink(
                        dictionary.GetValueOrNull("Prev"),
                        previous,
                        document,
                        "outline.prev.mismatch",
                        "An outline item has an inconsistent /Prev link.");
                }

                string title = ReadTitle(dictionary, document);
                int flags = Math.Max(
                    0,
                    dictionary.GetValueOrNull("F").AsInteger(document) ?? 0);
                PdfAnnotationAction action =
                    actionReader.ReadAnnotationAction(dictionary);
                PdfDestination? destination =
                    action.Destination ??
                    destinations.Resolve(dictionary.GetValueOrNull("Dest"));
                var builder = new ItemBuilder(
                    title,
                    destination,
                    action,
                    (dictionary.GetValueOrNull("Count").AsInteger(document) ?? 0) > 0,
                    (flags & 2) != 0,
                    (flags & 1) != 0,
                    ReadColor(dictionary.GetValueOrNull("C"), document));
                task.Target.Add(builder);

                if (dictionary.GetValueOrNull("First") is { } childFirst)
                {
                    childTasks.Add(new ChainTask(
                        childFirst,
                        dictionary.GetValueOrNull("Last"),
                        current,
                        builder.Children,
                        task.Depth + 1));
                }

                previous = current;
                actualLast = current;
                current = dictionary.GetValueOrNull("Next");
            }

            if (task.Last is not null &&
                actualLast is not null &&
                !SameObject(task.Last, actualLast, document))
            {
                document.AddDiagnosticOnce(
                    PdfDiagnosticSeverity.Warning,
                    "outline.last.mismatch",
                    "An outline sibling chain has an inconsistent /Last link.");
            }

            for (int index = childTasks.Count - 1; index >= 0; index--)
                tasks.Push(childTasks[index]);
        }

        return Freeze(topLevel);
    }

    private static bool MarkSeen(
        PdfObject source,
        PdfDictionary dictionary,
        HashSet<PdfReference> references,
        HashSet<PdfDictionary> dictionaries) =>
        source is PdfReference reference
            ? references.Add(reference)
            : dictionaries.Add(dictionary);

    private static void ValidateLink(
        PdfObject? actual,
        PdfObject expected,
        PdfDocumentCore document,
        string diagnosticCode,
        string message)
    {
        if (actual is not null && !SameObject(actual, expected, document))
        {
            document.AddDiagnosticOnce(
                PdfDiagnosticSeverity.Warning,
                diagnosticCode,
                message);
        }
    }

    private static bool SameObject(
        PdfObject left,
        PdfObject right,
        PdfDocumentCore document)
    {
        if (left is PdfReference leftReference &&
            right is PdfReference rightReference)
        {
            return leftReference.Equals(rightReference);
        }

        return ReferenceEquals(left.Resolve(document), right.Resolve(document));
    }

    private static string ReadTitle(
        PdfDictionary dictionary,
        PdfDocumentCore document)
    {
        PdfObject? value = dictionary.GetValueOrNull("Title")?.Resolve(document);
        ReadOnlyMemory<byte> bytes = value switch
        {
            PdfString text => text.Bytes,
            PdfName name => System.Text.Encoding.UTF8.GetBytes(name.Value),
            _ => ReadOnlyMemory<byte>.Empty
        };
        if (bytes.Length > document.Options.MaximumOutlineTitleBytes)
        {
            throw new PdfLimitException(
                "PDF outline title exceeds the configured byte limit.");
        }

        return value switch
        {
            PdfString text => text.Text,
            PdfName name => name.Value,
            _ => ""
        };
    }

    private static PdfColor? ReadColor(
        PdfObject? value,
        PdfDocumentCore document)
    {
        PdfArray? components = value.AsArray(document);
        if (components is null || components.Count != 3)
            return null;
        double? red = components[0].AsNumber(document);
        double? green = components[1].AsNumber(document);
        double? blue = components[2].AsNumber(document);
        return red is { } r && green is { } g && blue is { } b &&
               double.IsFinite(r) && double.IsFinite(g) && double.IsFinite(b)
            ? PdfColor.Rgb(r, g, b)
            : null;
    }

    private static IReadOnlyList<PdfOutlineItem> Freeze(
        IReadOnlyList<ItemBuilder> roots)
    {
        var frozen = new Dictionary<ItemBuilder, PdfOutlineItem>(
            ReferenceEqualityComparer.Instance);
        var stack = new Stack<(ItemBuilder Item, bool Visited)>();
        for (int index = roots.Count - 1; index >= 0; index--)
            stack.Push((roots[index], false));

        while (stack.Count > 0)
        {
            (ItemBuilder item, bool visited) = stack.Pop();
            if (!visited)
            {
                stack.Push((item, true));
                for (int index = item.Children.Count - 1; index >= 0; index--)
                    stack.Push((item.Children[index], false));
                continue;
            }

            frozen[item] = new PdfOutlineItem(
                item.Title,
                item.Children.Select(child => frozen[child]),
                item.Destination,
                item.Action,
                item.IsOpen,
                item.IsBold,
                item.IsItalic,
                item.Color);
        }

        return new ReadOnlyCollection<PdfOutlineItem>(
            roots.Select(root => frozen[root]).ToArray());
    }

    private sealed record ChainTask(
        PdfObject First,
        PdfObject? Last,
        PdfObject Parent,
        List<ItemBuilder> Target,
        int Depth);

    private sealed class ItemBuilder
    {
        public ItemBuilder(
            string title,
            PdfDestination? destination,
            PdfAnnotationAction action,
            bool isOpen,
            bool isBold,
            bool isItalic,
            PdfColor? color)
        {
            Title = title;
            Destination = destination;
            Action = action;
            IsOpen = isOpen;
            IsBold = isBold;
            IsItalic = isItalic;
            Color = color;
        }

        public string Title { get; }
        public List<ItemBuilder> Children { get; } = new();
        public PdfDestination? Destination { get; }
        public PdfAnnotationAction Action { get; }
        public bool IsOpen { get; }
        public bool IsBold { get; }
        public bool IsItalic { get; }
        public PdfColor? Color { get; }
    }
}
