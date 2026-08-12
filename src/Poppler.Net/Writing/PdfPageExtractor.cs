using System.Globalization;
using System.Text;
using Poppler.Core;
using Poppler.Core.Filters;
using Poppler.DocumentModel;

namespace Poppler.Writing;

internal static class PdfPageExtractor
{
    public static byte[] Extract(
        PdfDocumentCore document,
        PdfDictionary catalog,
        PdfPageNode page,
        IReadOnlyList<PdfPageNode> allPages,
        PdfPageExtractionOptions options)
    {
        return new Writer(document, catalog, page, allPages, options).Write();
    }

    private enum ObjectRole
    {
        Generic,
        Annotation
    }

    private sealed class MappedObject
    {
        public required int Number { get; init; }
        public PdfReference? SourceReference { get; init; }
        public PdfObject? SyntheticValue { get; init; }
        public required int ReferenceDepth { get; init; }
        public ObjectRole Role { get; set; }
        public bool IsWidget { get; set; }
    }

    private sealed class Writer
    {
        private static readonly string[] CatalogKeys =
        {
            "Extensions", "Lang", "MarkInfo", "Metadata", "OCProperties",
            "OutputIntents", "PageLayout", "PageMode", "ViewerPreferences"
        };

        private static readonly string[] PageKeys =
        {
            "AF", "BoxColorInfo", "Contents", "Dur", "Group", "ID",
            "LastModified", "Metadata", "OutputIntents", "PieceInfo", "PresSteps",
            "PZ", "SeparationInfo", "Tabs", "TemplateInstantiated", "Thumb",
            "Trans", "UserUnit", "VP"
        };

        private static readonly string[] AcroFormKeys =
        {
            "DA", "DR", "NeedAppearances", "Q", "SigFlags"
        };

        private static readonly string[] InheritableFieldKeys =
        {
            "DA", "DS", "DV", "Ff", "FT", "MaxLen", "Opt", "Q", "RV",
            "T", "TM", "TU", "V"
        };

        private readonly PdfDocumentCore _document;
        private readonly PdfDictionary _catalog;
        private readonly PdfPageNode _page;
        private readonly PdfPageExtractionOptions _options;
        private readonly HashSet<PdfReference> _pageReferences;
        private readonly Dictionary<PdfReference, MappedObject> _sourceObjects = new();
        private readonly List<MappedObject> _mappedObjects = new();
        private readonly List<int> _annotationObjects = new();
        private readonly List<int> _widgetObjects = new();
        private readonly BoundedOutput _output;
        private long _streamBytes;

        public Writer(
            PdfDocumentCore document,
            PdfDictionary catalog,
            PdfPageNode page,
            IReadOnlyList<PdfPageNode> allPages,
            PdfPageExtractionOptions options)
        {
            _document = document;
            _catalog = catalog;
            _page = page;
            _options = options;
            _pageReferences = allPages
                .Select(candidate => candidate.SourceReference)
                .OfType<PdfReference>()
                .ToHashSet();
            _output = new BoundedOutput(options.MaximumOutputBytes);
        }

        public byte[] Write()
        {
            PrepareAnnotations();
            WriteHeader();
            var offsets = new List<long> { 0 };
            WriteIndirectObject(1, offsets, WriteCatalog);
            WriteIndirectObject(2, offsets, WritePages);
            WriteIndirectObject(3, offsets, WritePage);

            for (int index = 0; index < _mappedObjects.Count; index++)
            {
                MappedObject mapped = _mappedObjects[index];
                WriteIndirectObject(
                    mapped.Number,
                    offsets,
                    () => WriteMappedObject(mapped));
            }

            long xrefOffset = _output.Position;
            _output.WriteAscii("xref\n");
            _output.WriteAscii($"0 {offsets.Count}\n");
            _output.WriteAscii("0000000000 65535 f \n");
            for (int number = 1; number < offsets.Count; number++)
            {
                _output.WriteAscii(
                    offsets[number].ToString("0000000000", CultureInfo.InvariantCulture));
                _output.WriteAscii(" 00000 n \n");
            }

            _output.WriteAscii("trailer\n<< /Root 1 0 R /Size ");
            _output.WriteAscii(offsets.Count.ToString(CultureInfo.InvariantCulture));
            _output.WriteAscii(" >>\nstartxref\n");
            _output.WriteAscii(xrefOffset.ToString(CultureInfo.InvariantCulture));
            _output.WriteAscii("\n%%EOF\n");
            return _output.ToArray();
        }

        private void PrepareAnnotations()
        {
            if (!_options.PreserveAnnotations)
                return;
            PdfObject? raw = _page.Dictionary.GetValueOrNull("Annots");
            if (raw is null)
                return;
            PdfArray annotations = raw.AsArray(_document) ??
                throw new PdfFormatException("Page /Annots is not an array.");
            foreach (PdfObject annotationObject in annotations)
            {
                PdfDictionary annotation = annotationObject.AsDictionary(_document) ??
                    throw new PdfFormatException("Page annotation is not a dictionary.");
                bool isWidget = annotation.GetValueOrNull("Subtype").AsName(_document) == "Widget";
                int number = annotationObject is PdfReference reference
                    ? MapSource(reference, ObjectRole.Annotation, 1, isWidget)
                    : MapSynthetic(annotation, ObjectRole.Annotation, 1, isWidget);
                _annotationObjects.Add(number);
                if (isWidget)
                    _widgetObjects.Add(number);
            }
        }

        private void WriteHeader()
        {
            string version = _document.PdfVersion;
            if (version.Length != 3 || version[1] != '.' ||
                !char.IsAsciiDigit(version[0]) || !char.IsAsciiDigit(version[2]))
            {
                version = "1.7";
            }
            _output.WriteAscii($"%PDF-{version}\n%");
            _output.Write(new byte[] { 0xE2, 0xE3, 0xCF, 0xD3 });
            _output.WriteAscii("\n");
        }

        private void WriteIndirectObject(
            int number,
            List<long> offsets,
            Action writeValue)
        {
            if (number != offsets.Count)
                throw new InvalidOperationException("Extracted PDF object numbers are not contiguous.");
            offsets.Add(_output.Position);
            _output.WriteAscii(number.ToString(CultureInfo.InvariantCulture));
            _output.WriteAscii(" 0 obj\n");
            writeValue();
            _output.WriteAscii("\nendobj\n");
        }

        private void WriteCatalog()
        {
            _output.WriteAscii("<< /Type /Catalog /Pages 2 0 R");
            foreach (string key in CatalogKeys)
            {
                if (!_catalog.TryGetValue(key, out PdfObject? value))
                    continue;
                WriteKey(key);
                WriteValue(value, 0, 0);
            }
            if (_widgetObjects.Count > 0)
            {
                WriteKey("AcroForm");
                WriteAcroForm();
            }
            _output.WriteAscii(" >>");
        }

        private void WritePages() =>
            _output.WriteAscii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");

        private void WritePage()
        {
            _output.WriteAscii("<< /Type /Page /Parent 2 0 R");
            WriteKey("MediaBox");
            WriteRectangle(_page.MediaBox);
            WriteKey("CropBox");
            WriteRectangle(_page.CropBox);
            WriteOptionalRectangle("BleedBox", _page.BleedBox);
            WriteOptionalRectangle("TrimBox", _page.TrimBox);
            WriteOptionalRectangle("ArtBox", _page.ArtBox);
            if (_page.Resources is not null)
            {
                WriteKey("Resources");
                WriteValue(_page.Resources, 0, 0);
            }
            if (_page.Rotation != 0)
            {
                WriteKey("Rotate");
                _output.WriteAscii(_page.Rotation.ToString(CultureInfo.InvariantCulture));
            }
            foreach (string key in PageKeys)
            {
                if (!_page.Dictionary.TryGetValue(key, out PdfObject? value))
                    continue;
                WriteKey(key);
                WriteValue(value, 0, 0);
            }
            if (_annotationObjects.Count > 0)
            {
                WriteKey("Annots");
                _output.WriteAscii("[");
                foreach (int number in _annotationObjects)
                {
                    _output.WriteAscii(number.ToString(CultureInfo.InvariantCulture));
                    _output.WriteAscii(" 0 R ");
                }
                _output.WriteAscii("]");
            }
            _output.WriteAscii(" >>");
        }

        private void WriteAcroForm()
        {
            PdfDictionary? source = null;
            try
            {
                source = _catalog.GetValueOrNull("AcroForm").AsDictionary(_document);
            }
            catch (PdfFormatException)
            {
                // Widgets remain usable as terminal fields without a damaged source form.
            }

            _output.WriteAscii("<< /Fields [");
            foreach (int number in _widgetObjects.Distinct())
            {
                _output.WriteAscii(number.ToString(CultureInfo.InvariantCulture));
                _output.WriteAscii(" 0 R ");
            }
            _output.WriteAscii("]");
            if (source is not null)
            {
                foreach (string key in AcroFormKeys)
                {
                    if (!source.TryGetValue(key, out PdfObject? value))
                        continue;
                    WriteKey(key);
                    WriteValue(value, 0, 0);
                }
            }
            _output.WriteAscii(" >>");
        }

        private void WriteMappedObject(MappedObject mapped)
        {
            PdfObject value = mapped.SourceReference is { } reference
                ? _document.Resolve(reference)
                : mapped.SyntheticValue!;
            if (mapped.Role == ObjectRole.Annotation)
            {
                if (value is not PdfDictionary annotation)
                    throw new PdfFormatException("Mapped annotation is not a dictionary.");
                WriteAnnotation(annotation, mapped.IsWidget, mapped.ReferenceDepth);
                return;
            }
            WriteValue(value, mapped.ReferenceDepth, 0);
        }

        private void WriteAnnotation(PdfDictionary annotation, bool isWidget, int referenceDepth)
        {
            Dictionary<string, PdfObject> values = annotation
                .Where(pair => pair.Key is not ("P" or "Parent" or "Kids" or "Dest" or "A" or "AA"))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            if (isWidget)
                MaterializeInheritedFieldValues(annotation, values);

            _output.WriteAscii("<< /P 3 0 R");
            foreach ((string key, PdfObject value) in values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                WriteKey(key);
                WriteValue(value, referenceDepth, 0);
            }
            if (annotation.TryGetValue("Dest", out PdfObject? destination) &&
                IsLocalDestination(destination))
            {
                WriteKey("Dest");
                WriteDestination(destination, referenceDepth);
            }
            if (annotation.TryGetValue("A", out PdfObject? action) &&
                TryGetAction(action, out PdfDictionary? actionDictionary))
            {
                PdfDictionary effectiveAction = actionDictionary!;
                string? kind = effectiveAction.GetValueOrNull("S").AsName(_document);
                PdfObject? target = effectiveAction.GetValueOrNull("D");
                if (kind != "GoTo" || (target is not null && IsLocalDestination(target)))
                {
                    WriteKey("A");
                    WriteAction(effectiveAction, referenceDepth);
                }
            }
            _output.WriteAscii(" >>");
        }

        private void MaterializeInheritedFieldValues(
            PdfDictionary widget,
            IDictionary<string, PdfObject> values)
        {
            PdfObject? parent = widget.GetValueOrNull("Parent");
            var visited = new HashSet<PdfReference>();
            int depth = 0;
            while (parent is not null)
            {
                if (++depth > _options.MaximumDepth)
                    throw new PdfLimitException("Form field inheritance exceeds the extraction depth limit.");
                if (parent is PdfReference reference && !visited.Add(reference))
                    throw new PdfFormatException("Circular form field hierarchy.");
                PdfDictionary? field = parent.AsDictionary(_document);
                if (field is null)
                    throw new PdfFormatException("Widget parent is not a dictionary.");
                foreach (string key in InheritableFieldKeys)
                {
                    if (!values.ContainsKey(key) && field.TryGetValue(key, out PdfObject? value))
                        values[key] = value;
                }
                parent = field.GetValueOrNull("Parent");
            }
        }

        private bool TryGetAction(PdfObject value, out PdfDictionary? action)
        {
            try
            {
                action = value.AsDictionary(_document);
                return action is not null;
            }
            catch (PdfFormatException)
            {
                action = null;
                return false;
            }
        }

        private void WriteAction(PdfDictionary action, int referenceDepth)
        {
            _output.WriteAscii("<<");
            foreach ((string key, PdfObject value) in action.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (key == "D" && action.GetValueOrNull("S").AsName(_document) == "GoTo")
                {
                    WriteKey(key);
                    WriteDestination(value, referenceDepth);
                }
                else
                {
                    WriteKey(key);
                    WriteValue(value, referenceDepth, 0);
                }
            }
            _output.WriteAscii(" >>");
        }

        private bool IsLocalDestination(PdfObject destination)
        {
            PdfObject resolved;
            try
            {
                resolved = destination is PdfReference reference && !_pageReferences.Contains(reference)
                    ? _document.Resolve(reference)
                    : destination;
            }
            catch (PdfException)
            {
                return false;
            }
            if (resolved is not PdfArray array || array.Count == 0)
                return false;
            return array[0] is PdfReference pageReference &&
                   _page.SourceReference is not null &&
                   pageReference.Equals(_page.SourceReference);
        }

        private void WriteDestination(PdfObject destination, int referenceDepth)
        {
            PdfObject resolved = destination is PdfReference reference && !_pageReferences.Contains(reference)
                ? _document.Resolve(reference)
                : destination;
            PdfArray array = resolved as PdfArray ??
                throw new PdfFormatException("Local destination is not an array.");
            _output.WriteAscii("[3 0 R");
            for (int index = 1; index < array.Count; index++)
            {
                _output.WriteAscii(" ");
                WriteValue(array[index], referenceDepth, 1);
            }
            _output.WriteAscii("]");
        }

        private void WriteValue(PdfObject value, int referenceDepth, int directDepth)
        {
            if (directDepth > _options.MaximumDepth)
                throw new PdfLimitException("Direct PDF object depth exceeds the extraction limit.");
            switch (value)
            {
                case PdfNull:
                    _output.WriteAscii("null");
                    break;
                case PdfBoolean boolean:
                    _output.WriteAscii(boolean.Value ? "true" : "false");
                    break;
                case PdfNumber number:
                    if (!double.IsFinite(number.Value))
                        throw new PdfFormatException("A PDF number is not finite.");
                    _output.WriteAscii(number.ToString());
                    break;
                case PdfName name:
                    WriteName(name.Value);
                    break;
                case PdfString text:
                    WriteHexString(text.Bytes.Span);
                    break;
                case PdfArray array:
                    _output.WriteAscii("[");
                    for (int index = 0; index < array.Count; index++)
                    {
                        if (index > 0)
                            _output.WriteAscii(" ");
                        WriteValue(array[index], referenceDepth, directDepth + 1);
                    }
                    _output.WriteAscii("]");
                    break;
                case PdfDictionary dictionary:
                    WriteDictionary(dictionary, referenceDepth, directDepth + 1);
                    break;
                case PdfReference reference:
                    WriteReference(reference, referenceDepth + 1);
                    break;
                case PdfStream stream:
                    WriteStream(stream, referenceDepth, directDepth + 1);
                    break;
                case PdfKeyword keyword:
                    WriteKeyword(keyword.Value);
                    break;
                default:
                    throw new PdfUnsupportedFeatureException(
                        $"standalone writer object {value.GetType().Name}");
            }
        }

        private void WriteDictionary(PdfDictionary dictionary, int referenceDepth, int directDepth)
        {
            _output.WriteAscii("<<");
            foreach ((string key, PdfObject value) in dictionary.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                WriteKey(key);
                WriteValue(value, referenceDepth, directDepth);
            }
            _output.WriteAscii(" >>");
        }

        private void WriteStream(PdfStream stream, int referenceDepth, int directDepth)
        {
            byte[] bytes = stream.EncodedBytes.ToArray();
            PdfDictionary dictionary = stream.Dictionary;
            bool normalizedFilters = ContainsCryptFilter(dictionary.GetValueOrNull("Filter"));
            PdfFilterPipeline.ImageSource? normalized = null;
            if (normalizedFilters)
            {
                normalized = PdfFilterPipeline.DecodeImageSource(stream, _document, _document.Options);
                bytes = normalized.Bytes;
            }
            AddStreamBytes(bytes.Length);

            _output.WriteAscii("<<");
            foreach ((string key, PdfObject value) in dictionary.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (key == "Length" ||
                    (normalizedFilters && key is ("Filter" or "DecodeParms" or "DL")))
                {
                    continue;
                }
                WriteKey(key);
                WriteValue(value, referenceDepth, directDepth);
            }
            if (normalized?.TerminalFilter is { } terminalFilter)
            {
                WriteKey("Filter");
                WriteName(terminalFilter);
                if (normalized.Parameters is not null)
                {
                    WriteKey("DecodeParms");
                    WriteValue(normalized.Parameters, referenceDepth, directDepth);
                }
            }
            WriteKey("Length");
            _output.WriteAscii(bytes.Length.ToString(CultureInfo.InvariantCulture));
            _output.WriteAscii(" >>\nstream\n");
            _output.Write(bytes);
            _output.WriteAscii("\nendstream");
        }

        private bool ContainsCryptFilter(PdfObject? filter)
        {
            if (filter is null)
                return false;
            PdfObject resolved = filter.Resolve(_document);
            return resolved switch
            {
                PdfName name => name.Value == "Crypt",
                PdfArray array => array.Any(ContainsCryptFilter),
                _ => false
            };
        }

        private void AddStreamBytes(int count)
        {
            if (_streamBytes > _options.MaximumStreamBytes - count)
                throw new PdfLimitException("Extracted PDF stream bytes exceed the configured limit.");
            _streamBytes += count;
        }

        private void WriteReference(PdfReference reference, int referenceDepth)
        {
            if (referenceDepth > _options.MaximumDepth)
                throw new PdfLimitException("PDF reference graph exceeds the extraction depth limit.");
            if (_page.SourceReference is not null && reference.Equals(_page.SourceReference))
            {
                _output.WriteAscii("3 0 R");
                return;
            }
            if (_pageReferences.Contains(reference))
            {
                _output.WriteAscii("null");
                return;
            }
            int number = MapSource(reference, ObjectRole.Generic, referenceDepth, false);
            _output.WriteAscii(number.ToString(CultureInfo.InvariantCulture));
            _output.WriteAscii(" 0 R");
        }

        private int MapSource(
            PdfReference reference,
            ObjectRole role,
            int referenceDepth,
            bool isWidget)
        {
            if (_sourceObjects.TryGetValue(reference, out MappedObject? existing))
            {
                if (role > existing.Role)
                    existing.Role = role;
                existing.IsWidget |= isWidget;
                return existing.Number;
            }
            EnsureObjectCapacity();
            var mapped = new MappedObject
            {
                Number = _mappedObjects.Count + 4,
                SourceReference = reference,
                ReferenceDepth = referenceDepth,
                Role = role,
                IsWidget = isWidget
            };
            _sourceObjects.Add(reference, mapped);
            _mappedObjects.Add(mapped);
            return mapped.Number;
        }

        private int MapSynthetic(
            PdfObject value,
            ObjectRole role,
            int referenceDepth,
            bool isWidget)
        {
            EnsureObjectCapacity();
            var mapped = new MappedObject
            {
                Number = _mappedObjects.Count + 4,
                SyntheticValue = value,
                ReferenceDepth = referenceDepth,
                Role = role,
                IsWidget = isWidget
            };
            _mappedObjects.Add(mapped);
            return mapped.Number;
        }

        private void EnsureObjectCapacity()
        {
            if (_mappedObjects.Count + 3 >= _options.MaximumObjects)
                throw new PdfLimitException("Extracted PDF object count exceeds the configured limit.");
        }

        private void WriteOptionalRectangle(string key, PdfRectangle? rectangle)
        {
            if (rectangle is null)
                return;
            WriteKey(key);
            WriteRectangle(rectangle.Value);
        }

        private void WriteRectangle(PdfRectangle rectangle)
        {
            _output.WriteAscii("[");
            WriteCoordinate(rectangle.Left);
            _output.WriteAscii(" ");
            WriteCoordinate(rectangle.Bottom);
            _output.WriteAscii(" ");
            WriteCoordinate(rectangle.Right);
            _output.WriteAscii(" ");
            WriteCoordinate(rectangle.Top);
            _output.WriteAscii("]");
        }

        private void WriteCoordinate(double value)
        {
            if (!double.IsFinite(value))
                throw new PdfFormatException("Page box contains a non-finite coordinate.");
            _output.WriteAscii(value.ToString("0.################", CultureInfo.InvariantCulture));
        }

        private void WriteKey(string key)
        {
            _output.WriteAscii(" ");
            WriteName(key);
            _output.WriteAscii(" ");
        }

        private void WriteName(string value)
        {
            _output.WriteAscii("/");
            foreach (byte current in Encoding.Latin1.GetBytes(value))
            {
                if (current is >= 33 and <= 126 &&
                    current is not ((byte)'#' or (byte)'%' or (byte)'(' or (byte)')' or
                        (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or
                        (byte)'{' or (byte)'}' or (byte)'/'))
                {
                    _output.WriteByte(current);
                }
                else
                {
                    _output.WriteAscii("#");
                    _output.WriteAscii(current.ToString("X2", CultureInfo.InvariantCulture));
                }
            }
        }

        private void WriteHexString(ReadOnlySpan<byte> bytes)
        {
            _output.WriteAscii("<");
            foreach (byte current in bytes)
                _output.WriteAscii(current.ToString("X2", CultureInfo.InvariantCulture));
            _output.WriteAscii(">");
        }

        private void WriteKeyword(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Any(character =>
                    character > 0x7E || char.IsWhiteSpace(character) ||
                    "()<>[]{}/%".Contains(character, StringComparison.Ordinal)))
            {
                throw new PdfFormatException("Unsafe PDF keyword in object graph.");
            }
            _output.WriteAscii(value);
        }
    }

    private sealed class BoundedOutput
    {
        private readonly long _maximumBytes;
        private readonly MemoryStream _stream = new();

        public BoundedOutput(long maximumBytes) => _maximumBytes = maximumBytes;
        public long Position => _stream.Position;

        public void WriteAscii(string value) => Write(Encoding.ASCII.GetBytes(value));
        public void WriteByte(byte value)
        {
            EnsureCapacity(1);
            _stream.WriteByte(value);
        }

        public void Write(ReadOnlySpan<byte> value)
        {
            EnsureCapacity(value.Length);
            _stream.Write(value);
        }

        public byte[] ToArray() => _stream.ToArray();

        private void EnsureCapacity(int count)
        {
            if (_stream.Length > _maximumBytes - count)
                throw new PdfLimitException("Extracted PDF exceeds the configured output-size limit.");
        }
    }
}
