using System.Globalization;
using Poppler;
using Poppler.Rendering;

return Cli.Run(args);

internal static class Cli
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return 0;
        }

        try
        {
            return args[0] switch
            {
                "info" => Info(args),
                "text" => Text(args),
                "fonts" => Fonts(args),
                "annotations" => Annotations(args),
                "outline" => Outline(args),
                "forms" => Forms(args),
                "layers" => Layers(args),
                "graphics" => Graphics(args),
                "images" => Images(args),
                "render" => Render(args),
                "attachments" => Attachments(args),
                "svg" => Svg(args),
                "version" or "--version" => Version(),
                _ => UsageError($"Unknown command '{args[0]}'.")
            };
        }
        catch (Exception exception) when (
            exception is PdfException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return 1;
        }
    }

    private static int Info(string[] args)
    {
        RequireCount(args, 2, "info requires an input PDF.");
        using Document document = LoadDocument(args, 1);
        Console.WriteLine($"PDF version:        {document.PdfVersion}");
        Console.WriteLine($"Pages:              {document.Pages}");
        Console.WriteLine($"Encrypted:          {YesNo(document.IsEncrypted)}");
        Console.WriteLine($"Locked:             {YesNo(document.IsLocked)}");
        if (document.EncryptionInfo is { } encryption)
        {
            Console.WriteLine($"Security revision:  {encryption.Revision}");
            Console.WriteLine($"Encryption:         {encryption.StreamAlgorithm}");
            Console.WriteLine($"Key length:         {encryption.KeyLengthBits} bits");
        }
        Console.WriteLine($"Linearized:         {YesNo(document.IsLinearized)}");
        Console.WriteLine($"Xref repaired:      {YesNo(document.XrefWasRepaired)}");
        if (document.IsLocked)
        {
            WriteDiagnostics(document);
            return 0;
        }

        Console.WriteLine($"Page mode:          {document.PageMode}");
        Console.WriteLine($"Page layout:        {document.PageLayout}");
        Console.WriteLine($"Form type:          {document.FormType}");
        Console.WriteLine($"Form fields:        {document.FormFields.Count}");
        Console.WriteLine($"Needs appearances:  {YesNo(document.FormNeedsAppearances)}");
        Console.WriteLine($"Optional layers:    {document.OptionalContentGroups.Count}");
        Console.WriteLine($"JavaScript present: {YesNo(document.HasJavaScript)}");
        Console.WriteLine($"Embedded files:     {document.EmbeddedFiles.Count}");
        foreach ((string key, string value) in
                 document.Information.OrderBy(
                     pair => pair.Key,
                     StringComparer.Ordinal))
        {
            Console.WriteLine($"{key,-20} {value}");
        }
        if (document.PdfId is { } id)
        {
            Console.WriteLine($"Permanent ID:       {id.PermanentId}");
            Console.WriteLine($"Update ID:          {id.UpdateId}");
        }

        WriteDiagnostics(document);
        return 0;
    }

    private static int Text(string[] args)
    {
        RequireCount(args, 2, "text requires an input PDF.");
        using Document document = LoadDocument(args, 1);
        EnsureUnlocked(document);
        TextLayout layout = args.Contains("--raw", StringComparer.Ordinal)
            ? TextLayout.RawOrder
            : args.Contains("--reading-order", StringComparer.Ordinal)
                ? TextLayout.NonRawNonPhysical
                : TextLayout.Physical;
        int? pageNumber = GetPageOption(args);
        if (pageNumber is not null)
        {
            Console.WriteLine(document.CreatePage(ToIndex(pageNumber.Value, document)).Text(
                layout: layout));
            return 0;
        }

        for (int index = 0; index < document.Pages; index++)
        {
            if (index > 0)
                Console.WriteLine();
            Page page = document.CreatePage(index);
            Console.WriteLine($"--- Page {page.Number} ({page.Label}) ---");
            Console.WriteLine(page.Text(layout: layout));
        }

        return 0;
    }

    private static int Fonts(string[] args)
    {
        RequireCount(args, 2, "fonts requires an input PDF.");
        using Document document = LoadDocument(args, 1);
        EnsureUnlocked(document);
        int? pageNumber = GetPageOption(args);
        IEnumerable<Page> pages = pageNumber is null
            ? Enumerable.Range(0, document.Pages).Select(document.CreatePage)
            : new[] { document.CreatePage(ToIndex(pageNumber.Value, document)) };

        Console.WriteLine(
            "page resource name                             type       encoding       embedded subset unicode mode");
        foreach (Page page in pages)
        {
            foreach (FontInfo font in page.Fonts)
            {
                Console.WriteLine(
                    $"{page.Number,4} {font.ResourceName,-8} {Truncate(font.Name, 32),-32} " +
                    $"{font.Type,-10} {Truncate(font.Encoding, 14),-14} " +
                    $"{YesNo(font.IsEmbedded),-8} {YesNo(font.IsSubset),-6} " +
                    $"{YesNo(font.HasToUnicode),-7} {font.WritingMode}");
            }
        }

        return 0;
    }

    private static int Attachments(string[] args)
    {
        RequireCount(args, 3, "attachments requires an input PDF and output directory.");
        string outputDirectory = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(outputDirectory);
        using Document document = LoadDocument(args, 1);
        EnsureUnlocked(document);
        foreach (EmbeddedFile file in document.EmbeddedFiles)
        {
            string safeName = Path.GetFileName(file.Name);
            if (string.IsNullOrWhiteSpace(safeName))
                safeName = "attachment.bin";
            string path = UniquePath(outputDirectory, safeName);
            file.SaveTo(path);
            Console.WriteLine($"{file.Name} -> {path} ({file.Size} bytes)");
        }

        return 0;
    }

    private static int Annotations(string[] args)
    {
        RequireCount(args, 2, "annotations requires an input PDF.");
        using Document document = LoadDocument(args, 1);
        EnsureUnlocked(document);
        int? pageNumber = GetPageOption(args);
        IEnumerable<Page> pages = pageNumber is null
            ? Enumerable.Range(0, document.Pages).Select(document.CreatePage)
            : new[] { document.CreatePage(ToIndex(pageNumber.Value, document)) };
        int total = 0;
        foreach (Page page in pages)
        {
            for (int index = 0; index < page.Annotations.Count; index++)
            {
                PdfAnnotation annotation = page.Annotations[index];
                Console.WriteLine(
                    $"page {page.Number} annotation {index + 1}: " +
                    $"{annotation.Type} ({annotation.Subtype}) " +
                    $"rect={annotation.Rectangle} flags={annotation.Flags} " +
                    $"visible={YesNo(annotation.IsVisible)} " +
                    $"appearance={YesNo(annotation.HasAppearance)}");
                if (!string.IsNullOrEmpty(annotation.Id))
                    Console.WriteLine($"  id: {annotation.Id}");
                if (!string.IsNullOrEmpty(annotation.ParentId))
                    Console.WriteLine($"  parent/reply-to: {annotation.ParentId}");
                if (!string.IsNullOrEmpty(annotation.PopupId))
                    Console.WriteLine($"  popup: {annotation.PopupId}");
                if (!string.IsNullOrWhiteSpace(annotation.Contents))
                {
                    Console.WriteLine(
                        $"  contents: {SingleLine(annotation.Contents)}");
                }
                if (annotation.Attachment is { } attachment)
                {
                    Console.WriteLine(
                        $"  attachment: {attachment.Name} " +
                        $"({attachment.Size} bytes, {attachment.MimeType})");
                }
                WriteAnnotationAction(annotation.Action);
                total++;
            }
        }

        Console.WriteLine($"{total} annotation(s).");
        return 0;
    }

    private static int Layers(string[] args)
    {
        RequireCount(args, 2, "layers requires an input PDF.");
        using Document document = LoadDocument(args, 1);
        EnsureUnlocked(document);
        PdfOptionalContentConfiguration? configuration =
            document.DefaultOptionalContentConfiguration;
        Console.WriteLine(
            $"Optional-content groups: {document.OptionalContentGroups.Count}; " +
            $"configuration={configuration?.Name ?? "(default)"}; " +
            $"base={configuration?.BaseState.ToString() ?? "On"}");
        foreach (PdfOptionalContentGroup group in document.OptionalContentGroups)
        {
            Console.WriteLine(
                $"{group.Id,-12} {(group.IsVisible ? "on " : "off")} " +
                $"{(group.IsLocked ? "locked  " : "unlocked")} " +
                $"{group.Name} [{string.Join(",", group.Intents)}]");
        }

        if (configuration is not null)
        {
            foreach (IReadOnlyList<string> radioGroup in configuration.RadioButtonGroups)
                Console.WriteLine($"radio: {string.Join(", ", radioGroup)}");
        }
        return 0;
    }

    private static int Outline(string[] args)
    {
        RequireCount(args, 2, "outline requires an input PDF.");
        using Document document = LoadDocument(args, 1);
        EnsureUnlocked(document);

        var stack = new Stack<(PdfOutlineItem Item, int Depth)>();
        for (int index = document.OutlineItems.Count - 1; index >= 0; index--)
            stack.Push((document.OutlineItems[index], 0));

        int total = 0;
        while (stack.Count > 0)
        {
            (PdfOutlineItem item, int depth) = stack.Pop();
            total++;
            string style = item.IsBold && item.IsItalic
                ? "bold+italic"
                : item.IsBold
                    ? "bold"
                    : item.IsItalic
                        ? "italic"
                        : "regular";
            string color = item.Color is { } itemColor
                ? string.Join(
                    ",",
                    new[]
                    {
                        itemColor.Component1,
                        itemColor.Component2,
                        itemColor.Component3
                    }.Select(component => component.ToString(
                        "0.###",
                        CultureInfo.InvariantCulture)))
                : "default";
            string destination = item.Destination is { } target
                ? $"page {target.PageNumber} {target.Type}" +
                  (target.NamedDestination is { Length: > 0 } name
                      ? $" ({SingleLine(name)})"
                      : "")
                : "none";
            Console.WriteLine(
                $"{new string(' ', depth * 2)}- {SingleLine(item.Title)} " +
                $"[{(item.IsOpen ? "open" : "closed")}; {style}; " +
                $"color={color}; action={item.Action.Type}; " +
                $"destination={destination}]");

            for (int index = item.Children.Count - 1; index >= 0; index--)
                stack.Push((item.Children[index], depth + 1));
        }

        Console.WriteLine($"{total} outline item(s).");
        WriteDiagnostics(document);
        return 0;
    }

    private static int Forms(string[] args)
    {
        RequireCount(args, 2, "forms requires an input PDF.");
        using Document document = LoadDocument(args, 1);
        EnsureUnlocked(document);
        int? pageNumber = GetPageOption(args);
        int? pageIndex = pageNumber is null
            ? null
            : ToIndex(pageNumber.Value, document);
        IEnumerable<PdfFormField> fields = pageIndex is null
            ? document.FormFields
            : document.FormFields.Where(field =>
                field.Widgets.Any(widget => widget.PageIndex == pageIndex));

        Console.WriteLine(
            $"AcroForm fields: {document.FormFields.Count}; " +
            $"NeedAppearances={YesNo(document.FormNeedsAppearances)}");
        int total = 0;
        foreach (PdfFormField field in fields)
        {
            string name = string.IsNullOrEmpty(field.FullyQualifiedName)
                ? "(unnamed)"
                : field.FullyQualifiedName;
            string value = field.IsSigned
                ? "(signed)"
                : string.Join(", ", field.Values.Select(SingleLine));
            Console.WriteLine(
                $"{name}: {field.Type}" +
                (field.ButtonType == PdfButtonType.None
                    ? ""
                    : $"/{field.ButtonType}") +
                $" flags={field.Flags} value={value}");
            foreach (PdfFormWidget widget in field.Widgets)
            {
                if (pageIndex is not null && widget.PageIndex != pageIndex)
                    continue;
                Console.WriteLine(
                    $"  widget page={widget.PageNumber?.ToString() ?? "unmapped"} " +
                    $"rect={widget.Rectangle} state={widget.AppearanceState} " +
                    $"appearance={YesNo(widget.HasAppearance)}");
            }
            total++;
        }

        Console.WriteLine($"{total} field(s).");
        return 0;
    }

    private static int Graphics(string[] args)
    {
        RequireCount(args, 2, "graphics requires an input PDF.");
        using Document document = LoadDocument(args, 1);
        EnsureUnlocked(document);
        int? pageNumber = GetPageOption(args);
        IEnumerable<Page> pages = pageNumber is null
            ? Enumerable.Range(0, document.Pages).Select(document.CreatePage)
            : new[] { document.CreatePage(ToIndex(pageNumber.Value, document)) };
        foreach (Page page in pages)
        {
            IReadOnlyList<PdfGraphicsElement> elements = page.Graphics;
            Console.WriteLine(
                $"page {page.Number}: {elements.Count} elements; " +
                $"{elements.OfType<PdfPathElement>().Count()} paths, " +
                $"{elements.OfType<PdfTextElement>().Count()} text runs, " +
                $"{elements.OfType<PdfImageElement>().Count()} images, " +
                $"{elements.OfType<PdfShadingElement>().Count() + elements.OfType<PdfMeshShadingElement>().Count()} shadings, " +
                $"{elements.OfType<PdfTransparencyGroupElement>().Count()} transparency groups");
        }

        return 0;
    }

    private static int Images(string[] args)
    {
        RequireCount(args, 3, "images requires an input PDF and output directory.");
        string outputDirectory = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(outputDirectory);
        using Document document = LoadDocument(args, 1);
        EnsureUnlocked(document);
        int? pageNumber = GetPageOption(args);
        IEnumerable<Page> pages = pageNumber is null
            ? Enumerable.Range(0, document.Pages).Select(document.CreatePage)
            : new[] { document.CreatePage(ToIndex(pageNumber.Value, document)) };
        int total = 0;
        foreach (Page page in pages)
        {
            int imageNumber = 0;
            foreach (PdfImage image in page.Images)
            {
                imageNumber++;
                string fileName =
                    $"page-{page.Number:0000}-image-{imageNumber:0000}.png";
                string path = UniquePath(outputDirectory, fileName);
                image.SavePng(path);
                Console.WriteLine(
                    $"{image.ResourceName} -> {path} " +
                    $"({image.Width}x{image.Height}, {image.ColorSpace}, {image.Compression})");
                total++;
            }
        }

        Console.WriteLine($"{total} decoded image(s).");
        return 0;
    }

    private static int Svg(string[] args)
    {
        RequireCount(args, 3, "svg requires an input PDF and output SVG.");
        using Document document = LoadDocument(args, 1);
        EnsureUnlocked(document);
        int pageNumber = GetPageOption(args) ?? 1;
        var options = new SvgRenderOptions
        {
            DrawTextBounds = args.Contains("--bounds", StringComparer.Ordinal),
            DrawImageBounds = args.Contains("--image-bounds", StringComparer.Ordinal),
            OptionalContentVisibility = GetLayerOverrides(args)
        };
        document.CreatePage(ToIndex(pageNumber, document)).SaveSvg(args[2], options);
        Console.WriteLine(Path.GetFullPath(args[2]));
        return 0;
    }

    private static int Render(string[] args)
    {
        RequireCount(args, 3, "render requires an input PDF and output PNG.");
        using Document document = LoadDocument(args, 1);
        EnsureUnlocked(document);
        int pageNumber = GetPageOption(args) ?? 1;
        var options = new RasterRenderOptions
        {
            Dpi = GetDoubleOption(args, "--dpi") ?? 96,
            Antialiasing = GetIntegerOption(args, "--antialias") ?? 4,
            Transparent = args.Contains("--transparent", StringComparer.Ordinal),
            UseFontSubstitution =
                !args.Contains("--no-font-substitution", StringComparer.Ordinal),
            FontDirectories = GetStringOptions(args, "--font-dir"),
            OptionalContentVisibility = GetLayerOverrides(args)
        };
        document.CreatePage(ToIndex(pageNumber, document)).SavePng(args[2], options);
        Console.WriteLine(Path.GetFullPath(args[2]));
        return 0;
    }

    private static int Version()
    {
        Console.WriteLine(
            $"poppler-net {Document.PortVersion} (source port target: Poppler {Document.UpstreamVersion})");
        return 0;
    }

    private static void WriteAnnotationAction(
        PdfAnnotationAction action,
        string indent = "  ")
    {
        switch (action.Type)
        {
            case PdfAnnotationActionType.Uri:
                Console.WriteLine($"{indent}action: URI {action.Uri}");
                break;
            case PdfAnnotationActionType.GoTo when action.Destination is { } destination:
                Console.WriteLine(
                    $"{indent}action: GoTo page {destination.PageNumber} " +
                    $"{destination.Type}" +
                    (destination.NamedDestination is null
                        ? ""
                        : $" ({destination.NamedDestination})"));
                break;
            case PdfAnnotationActionType.GoTo:
                Console.WriteLine(
                    $"{indent}action: GoTo {action.NamedTarget ?? "unresolved"}");
                break;
            case PdfAnnotationActionType.Named:
                Console.WriteLine($"{indent}action: Named {action.NamedTarget}");
                break;
            case PdfAnnotationActionType.GoToRemote:
                Console.WriteLine(
                    $"{indent}action: GoToRemote {action.FileName} " +
                    $"{action.NamedTarget} new-window={action.NewWindow}");
                break;
            case PdfAnnotationActionType.Launch:
                Console.WriteLine(
                    $"{indent}action: Launch {action.FileName} " +
                    $"new-window={action.NewWindow}");
                break;
            case PdfAnnotationActionType.JavaScript:
                Console.WriteLine(
                    $"{indent}action: JavaScript " +
                    $"{Truncate(SingleLine(action.Script ?? ""), 120)}");
                break;
            case PdfAnnotationActionType.SubmitForm:
            case PdfAnnotationActionType.ResetForm:
                Console.WriteLine(
                    $"{indent}action: {action.Type} {action.FileName} " +
                    $"fields=[{string.Join(", ", action.Fields)}] " +
                    $"flags={action.Flags}");
                break;
            case PdfAnnotationActionType.ImportData:
                Console.WriteLine($"{indent}action: ImportData {action.FileName}");
                break;
            case PdfAnnotationActionType.Hide:
                Console.WriteLine(
                    $"{indent}action: Hide hidden={action.IsHidden} " +
                    $"targets=[{string.Join(", ", action.Fields)}]");
                break;
            case PdfAnnotationActionType.SetOptionalContentState:
                Console.WriteLine(
                    $"{indent}action: SetOCGState " +
                    $"[{string.Join(", ", action.StateChanges)}]");
                break;
            case PdfAnnotationActionType.Rendition:
            case PdfAnnotationActionType.Transition:
            case PdfAnnotationActionType.GoToThreeDView:
                Console.WriteLine(
                    $"{indent}action: {action.Type} {action.NamedTarget} " +
                    $"flags={action.Flags}");
                break;
            case PdfAnnotationActionType.Unsupported:
                Console.WriteLine(
                    $"{indent}action: Unsupported {action.NamedTarget}");
                break;
        }
        foreach (PdfAnnotationAction next in action.NextActions)
            WriteAnnotationAction(next, indent + "  ");
    }

    private static int? GetPageOption(string[] args)
    {
        int option = Array.IndexOf(args, "--page");
        if (option < 0)
            return null;
        if (option + 1 >= args.Length ||
            !int.TryParse(args[option + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int page) ||
            page < 1)
        {
            throw new ArgumentException("--page requires a positive, one-based page number.");
        }

        return page;
    }

    private static int? GetIntegerOption(string[] args, string option)
    {
        int index = Array.IndexOf(args, option);
        if (index < 0)
            return null;
        if (index + 1 >= args.Length ||
            !int.TryParse(
                args[index + 1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value))
        {
            throw new ArgumentException($"{option} requires an integer value.");
        }

        return value;
    }

    private static double? GetDoubleOption(string[] args, string option)
    {
        int index = Array.IndexOf(args, option);
        if (index < 0)
            return null;
        if (index + 1 >= args.Length ||
            !double.TryParse(
                args[index + 1],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double value) ||
            !double.IsFinite(value))
        {
            throw new ArgumentException($"{option} requires a finite numeric value.");
        }

        return value;
    }

    private static Document LoadDocument(string[] args, int inputIndex) =>
        Document.LoadFromFile(
            args[inputIndex],
            ownerPassword: GetStringOption(args, "--owner-password"),
            userPassword: GetStringOption(args, "--user-password"),
            options: new PdfReadOptions
            {
                CMapDirectories = GetStringOptions(args, "--cmap-dir")
            });

    private static string GetStringOption(string[] args, string option)
    {
        int index = Array.IndexOf(args, option);
        if (index < 0)
            return "";
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"{option} requires a value.");
        return args[index + 1];
    }

    private static IReadOnlyList<string> GetStringOptions(
        string[] args,
        string option)
    {
        var values = new List<string>();
        for (int index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], option, StringComparison.Ordinal))
                continue;
            if (index + 1 >= args.Length ||
                args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"{option} requires a value.");
            }
            values.Add(args[++index]);
        }
        return values;
    }

    private static IReadOnlyDictionary<string, bool> GetLayerOverrides(
        string[] args)
    {
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (string value in GetStringOptions(args, "--layer"))
        {
            string[] parts = value.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]))
            {
                throw new ArgumentException(
                    "--layer requires GROUP-ID=on or GROUP-ID=off.");
            }
            bool visible = parts[1].ToLowerInvariant() switch
            {
                "on" or "true" or "1" => true,
                "off" or "false" or "0" => false,
                _ => throw new ArgumentException(
                    "--layer requires GROUP-ID=on or GROUP-ID=off.")
            };
            result[parts[0]] = visible;
        }
        return result;
    }

    private static void EnsureUnlocked(Document document)
    {
        if (document.IsLocked)
        {
            throw new PdfEncryptedException();
        }
    }

    private static void WriteDiagnostics(Document document)
    {
        foreach (PdfDiagnostic diagnostic in document.Diagnostics)
            Console.Error.WriteLine($"{diagnostic.Severity}: {diagnostic.Code}: {diagnostic.Message}");
    }

    private static int ToIndex(int pageNumber, Document document)
    {
        int index = pageNumber - 1;
        if ((uint)index >= (uint)document.Pages)
            throw new ArgumentOutOfRangeException(nameof(pageNumber), $"Page {pageNumber} does not exist.");
        return index;
    }

    private static string UniquePath(string directory, string fileName)
    {
        string path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
            return path;
        string stem = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);
        for (int suffix = 2; suffix < int.MaxValue; suffix++)
        {
            path = Path.Combine(directory, $"{stem}-{suffix}{extension}");
            if (!File.Exists(path))
                return path;
        }

        throw new IOException($"Could not choose a unique path for '{fileName}'.");
    }

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..(length - 1)] + "…";

    private static string SingleLine(string value) =>
        value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    private static void RequireCount(string[] args, int count, string message)
    {
        if (args.Length < count)
            throw new ArgumentException(message);
    }

    private static int UsageError(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine("Run 'poppler-net --help' for usage.");
        return 2;
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            poppler-net — managed-only Poppler 26.07 port

            Usage:
              poppler-net info <input.pdf> [password options]
              poppler-net text <input.pdf> [--page N] [--raw|--reading-order] [password options]
              poppler-net fonts <input.pdf> [--page N] [password options]
              poppler-net annotations <input.pdf> [--page N] [password options]
              poppler-net outline <input.pdf> [password options]
              poppler-net forms <input.pdf> [--page N] [password options]
              poppler-net layers <input.pdf> [password options]
              poppler-net graphics <input.pdf> [--page N] [password options]
              poppler-net images <input.pdf> <output-dir> [--page N] [password options]
              poppler-net render <input.pdf> <output.png> [--page N] [--dpi N] [--antialias 1|2|4|8] [--transparent] [--font-dir PATH] [--layer ID=on|off] [--no-font-substitution] [common options]
              poppler-net attachments <input.pdf> <output-dir> [password options]
              poppler-net svg <input.pdf> <output.svg> [--page N] [--bounds] [--image-bounds] [--layer ID=on|off] [password options]
              poppler-net version

            Password options:
              --user-password VALUE
              --owner-password VALUE

            Common options:
              --cmap-dir PATH       Add a directory containing Adobe CMap data.

            Page numbers accepted by the CLI are one-based.
            """);
    }
}
