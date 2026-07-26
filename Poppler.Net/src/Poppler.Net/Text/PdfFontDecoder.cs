using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Poppler.Core;

namespace Poppler.Text;

internal sealed partial class PdfFontDecoder
{
    private readonly Dictionary<byte, string> _differences = new();
    private readonly Dictionary<byte, string> _differenceNames = new();
    private readonly Dictionary<byte, string> _programEncoding = new();
    private readonly Dictionary<byte, string> _programEncodingNames = new();
    private readonly PdfCMap _toUnicode;
    private readonly PdfCMap _encodingCMap;
    private readonly PdfCidMetrics? _cidMetrics;
    private readonly PdfOpenTypeCmap? _openTypeCmap;
    private readonly PdfTrueTypeFont? _trueTypeFont;
    private readonly PdfCffFont? _cffFont;
    private readonly PdfType1Font? _type1Font;
    private readonly ushort[]? _cidToGlyph;
    private readonly double[]? _widths;
    private readonly int _firstCharacter;
    private readonly double _missingWidth;
    private readonly double _type3WidthScale;
    private readonly bool _composite;
    private readonly bool _cidToGlyphIdentity;
    private readonly string _simpleEncoding;
    private readonly string? _collection;
    private readonly PdfDocumentCore _document;
    private readonly PdfDictionary? _type3CharProcs;
    private readonly PdfObject? _type3Resources;
    private readonly global::Poppler.PdfMatrix _type3FontMatrix =
        global::Poppler.PdfMatrix.Identity;

    public PdfFontDecoder(
        string resourceName,
        PdfDictionary dictionary,
        PdfDocumentCore document)
    {
        ArgumentNullException.ThrowIfNull(resourceName);
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(document);
        _document = document;

        string declaredSubtype =
            dictionary.GetValueOrNull("Subtype").AsName(document) ?? "Unknown";
        _composite = declaredSubtype == "Type0";
        PdfDictionary effectiveDictionary = dictionary;
        if (_composite &&
            dictionary.GetValueOrNull("DescendantFonts").AsArray(document) is { Count: > 0 } descendants &&
            descendants[0].AsDictionary(document) is { } descendant)
        {
            effectiveDictionary = descendant;
        }

        string effectiveSubtype =
            effectiveDictionary.GetValueOrNull("Subtype").AsName(document) ?? declaredSubtype;
        if (declaredSubtype == "Type3")
        {
            _type3CharProcs =
                dictionary.GetValueOrNull("CharProcs").AsDictionary(document);
            _type3Resources = dictionary.GetValueOrNull("Resources");
            _type3FontMatrix = ReadType3Matrix(dictionary, document);
        }
        Name =
            dictionary.GetValueOrNull("BaseFont").AsName(document) ??
            effectiveDictionary.GetValueOrNull("BaseFont").AsName(document) ??
            dictionary.GetValueOrNull("Name").AsName(document) ??
            "Unknown";

        PdfDictionary? descriptor =
            effectiveDictionary.GetValueOrNull("FontDescriptor").AsDictionary(document);
        (EmbeddedFontFormat embeddedFormat, byte[]? fontProgram) =
            ReadEmbeddedFont(descriptor, document);
        EmbeddedFontFormat actualFormat = embeddedFormat;
        if (fontProgram is { Length: > 0 })
        {
            if (embeddedFormat is EmbeddedFontFormat.TrueType or EmbeddedFontFormat.OpenType)
            {
                _openTypeCmap = PdfOpenTypeCmap.TryParse(
                    fontProgram,
                    document.Options.MaximumCMapMappings);
                _trueTypeFont = PdfTrueTypeFont.TryParse(fontProgram);
            }
            if (embeddedFormat is EmbeddedFontFormat.Cff or EmbeddedFontFormat.OpenType)
                _cffFont = PdfCffFont.TryParse(fontProgram);
            if (embeddedFormat == EmbeddedFontFormat.Type1)
                _type1Font = PdfType1Font.TryParse(fontProgram);
            if (fontProgram.Length >= 4 &&
                fontProgram.AsSpan(0, 4).SequenceEqual("OTTO"u8))
            {
                actualFormat = EmbeddedFontFormat.OpenType;
            }
        }

        PdfStream? toUnicode = dictionary.GetValueOrNull("ToUnicode").AsStream(document);
        _toUnicode = toUnicode is null
            ? PdfCMap.Empty(document.Options.MaximumCMapMappings)
            : PdfCMap.Parse(
                document.Decode(toUnicode),
                document.Options.MaximumCMapMappings);

        if (_composite)
        {
            _encodingCMap = ReadCompositeEncoding(dictionary, document);
            _simpleEncoding = "";
            _cidMetrics = new PdfCidMetrics(effectiveDictionary, document);
            _collection = ReadCollection(effectiveDictionary, document);
            (_cidToGlyphIdentity, _cidToGlyph) =
                ReadCidToGlyph(effectiveDictionary, document);
            _firstCharacter = 0;
            _missingWidth = 1000;
            _type3WidthScale = 1;
        }
        else
        {
            _encodingCMap = PdfCMap.Empty(document.Options.MaximumCMapMappings);
            _simpleEncoding = ReadSimpleEncoding(dictionary, document);
            ReadDifferences(dictionary, document);
            if (fontProgram is { Length: > 0 } &&
                actualFormat == EmbeddedFontFormat.Type1)
            {
                ReadType1ProgramEncoding(fontProgram);
            }

            _firstCharacter = dictionary.GetValueOrNull("FirstChar").AsInteger(document) ?? 0;
            PdfArray? widths = dictionary.GetValueOrNull("Widths").AsArray(document);
            if (widths is not null)
            {
                _widths = new double[widths.Count];
                for (int index = 0; index < widths.Count; index++)
                    _widths[index] = widths[index].AsNumber(document) ?? 0;
            }

            _missingWidth =
                descriptor?.GetValueOrNull("MissingWidth").AsNumber(document) ??
                Base14DefaultWidth(Name);
            _type3WidthScale = declaredSubtype == "Type3"
                ? ReadType3WidthScale(dictionary, document)
                : 1;
        }

        Ascent = NormalizeMetric(
            descriptor?.GetValueOrNull("Ascent").AsNumber(document),
            0.8);
        Descent = NormalizeMetric(
            descriptor?.GetValueOrNull("Descent").AsNumber(document),
            -0.2);
        FontWritingMode writingMode =
            _composite ? _encodingCMap.WritingMode : FontWritingMode.Horizontal;
        string encodingName = _composite
            ? ReadEncodingName(dictionary, document, _encodingCMap.Name)
            : _simpleEncoding;
        PdfFontType type = DetermineFontType(
            declaredSubtype,
            effectiveSubtype,
            actualFormat);
        Info = new FontInfo(
            resourceName,
            Name,
            type,
            encodingName,
            writingMode,
            fontProgram is not null,
            actualFormat,
            fontProgram?.Length ?? 0,
            IsSubsetName(Name),
            _toUnicode.HasUnicodeMappings,
            _collection);
    }

    public string Name { get; }
    public FontInfo Info { get; }
    public FontWritingMode WritingMode => Info.WritingMode;
    public double Ascent { get; }
    public double Descent { get; }
    internal bool IsType3 => Info.Type == PdfFontType.Type3;

    public static PdfFontDecoder CreateFallback(PdfDocumentCore document) =>
        new(
            "Fallback",
            new PdfDictionary(new Dictionary<string, PdfObject>(StringComparer.Ordinal)
            {
                ["Subtype"] = new PdfName("Type1"),
                ["BaseFont"] = new PdfName("Helvetica"),
                ["Encoding"] = new PdfName("WinAnsiEncoding")
            }),
            document);

    public IReadOnlyList<PdfDecodedGlyph> DecodeGlyphs(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            return Array.Empty<PdfDecodedGlyph>();
        var result = new List<PdfDecodedGlyph>(_composite ? (bytes.Length + 1) / 2 : bytes.Length);
        if (!_composite)
        {
            Span<byte> source = stackalloc byte[1];
            foreach (byte value in bytes)
            {
                source[0] = value;
                string text = _toUnicode.TryGetUnicode(source, out string? mapped)
                    ? mapped
                    : DecodeSimple(value);
                double width = GetSimpleWidth(value) * _type3WidthScale;
                result.Add(new PdfDecodedGlyph(
                    value,
                    value,
                    1,
                    text,
                    width,
                    0,
                    0,
                    0,
                    value == 0x20));
            }

            return result;
        }

        int position = 0;
        while (position < bytes.Length)
        {
            PdfCharCode code = _encodingCMap.ReadCode(bytes, position, fallbackLength: 2);
            int length = code.Length > 0 ? code.Length : 1;
            ReadOnlySpan<byte> source = bytes.Slice(position, Math.Min(length, bytes.Length - position));
            uint cid = _encodingCMap.GetCid(source, code.Value);
            string text = _toUnicode.TryGetUnicode(source, out string? mapped)
                ? mapped
                : DecodeComposite(cid);

            double advanceX;
            double advanceY;
            double originX;
            double originY;
            if (WritingMode == FontWritingMode.Vertical)
            {
                (advanceY, originX, originY) = _cidMetrics!.GetVertical(cid);
                advanceX = 0;
            }
            else
            {
                advanceX = _cidMetrics!.GetWidth(cid);
                advanceY = originX = originY = 0;
            }

            result.Add(new PdfDecodedGlyph(
                code.Value,
                cid,
                source.Length,
                text,
                advanceX,
                advanceY,
                originX,
                originY,
                source.Length == 1 && code.Value == 0x20));
            position += source.Length;
        }

        return result;
    }

    public string Decode(ReadOnlySpan<byte> bytes) =>
        string.Concat(DecodeGlyphs(bytes).Select(glyph => glyph.Text));

    internal bool TryGetGlyphOutline(
        Rune rune,
        out PdfGraphicsPath path,
        out double advance,
        out double ascent,
        out double descent)
    {
        path = new PdfGraphicsPath(Array.Empty<PdfPathSegment>());
        advance = 0;
        ascent = _trueTypeFont?.Ascent ?? Ascent;
        descent = _trueTypeFont?.Descent ?? Descent;
        if (_openTypeCmap is not null &&
            _openTypeCmap.TryGetGlyph(rune.Value, out uint glyph))
        {
            if (_trueTypeFont?.TryGetGlyph(glyph, out path, out advance) == true)
                return true;
            if (_cffFont?.TryGetGlyph(glyph, out path, out advance) == true)
                return true;
        }

        if (_cffFont?.TryGetGlyph(rune, out path, out advance) == true)
            return true;
        return _type1Font?.TryGetGlyph(rune, out path, out advance) == true;
    }

    internal bool TryGetGlyphOutline(
        PdfDecodedGlyph decoded,
        out PdfGraphicsPath path,
        out double advance,
        out double ascent,
        out double descent)
    {
        path = new PdfGraphicsPath(Array.Empty<PdfPathSegment>());
        advance = 0;
        ascent = _trueTypeFont?.Ascent ?? Ascent;
        descent = _trueTypeFont?.Descent ?? Descent;

        uint glyph;
        if (_composite)
        {
            if (_cidToGlyph is not null)
            {
                if (decoded.Cid >= _cidToGlyph.Length)
                    return false;
                glyph = _cidToGlyph[(int)decoded.Cid];
            }
            else if (_cidToGlyphIdentity)
            {
                glyph = decoded.Cid;
            }
            else
            {
                if (_cffFont?.TryGetGlyphByCid(
                        decoded.Cid,
                        out path,
                        out advance) == true)
                {
                    return true;
                }
                return false;
            }
        }
        else if (_openTypeCmap is not null &&
                 _openTypeCmap.TryGetGlyphForCharacterCode(
                     decoded.CharacterCode,
                     out uint sourceGlyph))
        {
            glyph = sourceGlyph;
        }
        else
        {
            Rune first = decoded.Text.EnumerateRunes().FirstOrDefault();
            if (first.Value == 0)
            {
                return false;
            }
            if (_openTypeCmap is null ||
                !_openTypeCmap.TryGetGlyph(first.Value, out glyph))
            {
                if (_cffFont?.TryGetGlyph(first, out path, out advance) == true)
                    return true;
                return _type1Font?.TryGetGlyph(first, out path, out advance) == true;
            }
        }

        if (_trueTypeFont?.TryGetGlyph(glyph, out path, out advance) == true)
            return true;
        if (_cffFont?.TryGetGlyph(glyph, out path, out advance) == true)
            return true;
        if (_composite &&
            _cffFont?.TryGetGlyphByCid(decoded.Cid, out path, out advance) == true)
        {
            return true;
        }
        Rune fallback = decoded.Text.EnumerateRunes().FirstOrDefault();
        if (fallback.Value != 0 &&
            _type1Font?.TryGetGlyph(fallback, out path, out advance) == true)
        {
            return true;
        }
        return false;
    }

    internal bool TryGetType3GlyphProgram(
        PdfDecodedGlyph decoded,
        out byte[] program,
        out global::Poppler.PdfMatrix fontMatrix,
        out PdfObject? resources,
        out string glyphName)
    {
        program = Array.Empty<byte>();
        fontMatrix = _type3FontMatrix;
        resources = _type3Resources;
        glyphName = "";
        if (!IsType3 ||
            decoded.CharacterCode > byte.MaxValue ||
            _type3CharProcs is null)
        {
            return false;
        }

        byte code = (byte)decoded.CharacterCode;
        glyphName = _differenceNames.GetValueOrDefault(code) ??
                    _programEncodingNames.GetValueOrDefault(code) ??
                    FindType3GlyphName(decoded.Text);
        if (string.IsNullOrEmpty(glyphName) ||
            _type3CharProcs.GetValueOrNull(glyphName).AsStream(_document) is not { } stream)
        {
            return false;
        }

        program = _document.Decode(stream);
        return true;
    }

    private string FindType3GlyphName(string text)
    {
        if (_type3CharProcs is null)
            return "";
        foreach (string name in _type3CharProcs.Keys)
        {
            if (string.Equals(
                    PdfGlyphNames.ToUnicode(name),
                    text,
                    StringComparison.Ordinal))
            {
                return name;
            }
        }

        return "";
    }

    private string DecodeSimple(byte value)
    {
        if (_differences.TryGetValue(value, out string? difference))
            return difference;
        if (_programEncoding.TryGetValue(value, out string? embedded))
            return embedded;
        return PdfGlyphNames.DecodeEncodingByte(value, _simpleEncoding);
    }

    private string DecodeComposite(uint cid)
    {
        uint glyph = cid;
        if (_cidToGlyph is not null)
        {
            if (cid < _cidToGlyph.Length)
                glyph = _cidToGlyph[(int)cid];
        }
        else if (!_cidToGlyphIdentity && _collection is not "Adobe-Identity" and not "Adobe-UCS")
        {
            glyph = 0;
        }

        if (_openTypeCmap is not null &&
            _openTypeCmap.TryGetUnicode(glyph, out int scalar) &&
            Rune.TryCreate(scalar, out Rune mapped))
        {
            return mapped.ToString();
        }

        if (_collection is "Adobe-Identity" or "Adobe-UCS" &&
            cid <= 0x10FFFF &&
            Rune.TryCreate((int)cid, out Rune identity))
        {
            return identity.ToString();
        }

        return "\uFFFD";
    }

    private double GetSimpleWidth(byte value)
    {
        int index = value - _firstCharacter;
        return _widths is not null && index >= 0 && index < _widths.Length
            ? _widths[index]
            : _missingWidth;
    }

    private static PdfCMap ReadCompositeEncoding(
        PdfDictionary dictionary,
        PdfDocumentCore document)
    {
        PdfObject? encoding = dictionary.GetValueOrNull("Encoding");
        string? name = encoding.AsName(document);
        if (name is not null)
        {
            FontWritingMode mode = name.EndsWith("-V", StringComparison.Ordinal)
                ? FontWritingMode.Vertical
                : FontWritingMode.Horizontal;
            return PdfCMap.Identity(mode, document.Options.MaximumCMapMappings);
        }

        PdfStream? stream = encoding.AsStream(document);
        return stream is null
            ? PdfCMap.Identity(FontWritingMode.Horizontal, document.Options.MaximumCMapMappings)
            : PdfCMap.Parse(
                document.Decode(stream),
                document.Options.MaximumCMapMappings);
    }

    private static string ReadEncodingName(
        PdfDictionary dictionary,
        PdfDocumentCore document,
        string fallback)
    {
        PdfObject? encoding = dictionary.GetValueOrNull("Encoding");
        return encoding.AsName(document) ??
               encoding.AsStream(document)?.Dictionary.GetValueOrNull("CMapName").AsName(document) ??
               fallback;
    }

    private static string ReadSimpleEncoding(
        PdfDictionary dictionary,
        PdfDocumentCore document)
    {
        PdfObject? encoding = dictionary.GetValueOrNull("Encoding");
        string? direct = encoding.AsName(document);
        if (direct is not null)
            return direct;
        PdfDictionary? encodingDictionary = encoding.AsDictionary(document);
        string? baseEncoding =
            encodingDictionary?.GetValueOrNull("BaseEncoding").AsName(document);
        if (baseEncoding is not null)
            return baseEncoding;

        string baseFont = dictionary.GetValueOrNull("BaseFont").AsName(document) ?? "";
        if (baseFont.EndsWith("Symbol", StringComparison.Ordinal))
            return "Symbol";
        if (baseFont.EndsWith("ZapfDingbats", StringComparison.Ordinal))
            return "ZapfDingbats";
        return "StandardEncoding";
    }

    private void ReadDifferences(PdfDictionary dictionary, PdfDocumentCore document)
    {
        PdfDictionary? encoding =
            dictionary.GetValueOrNull("Encoding").AsDictionary(document);
        PdfArray? differences =
            encoding?.GetValueOrNull("Differences").AsArray(document);
        if (differences is null)
            return;

        int current = 0;
        foreach (PdfObject value in differences)
        {
            PdfObject resolved = value.Resolve(document);
            if (resolved is PdfNumber { IsInteger: true } number &&
                number.Value is >= 0 and <= 255)
            {
                current = (int)number.Value;
            }
            else if (resolved is PdfName name && current is >= 0 and <= 255)
            {
                _differences[(byte)current] = PdfGlyphNames.ToUnicode(name.Value);
                _differenceNames[(byte)current] = name.Value;
                current++;
            }
        }
    }

    private void ReadType1ProgramEncoding(byte[] program)
    {
        string clearText = Encoding.Latin1.GetString(program, 0, Math.Min(program.Length, 1_048_576));
        foreach (Match match in Type1EncodingRegex().Matches(clearText))
        {
            if (byte.TryParse(
                    match.Groups[1].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out byte code))
            {
                _programEncoding[code] = PdfGlyphNames.ToUnicode(match.Groups[2].Value);
                _programEncodingNames[code] = match.Groups[2].Value;
            }
        }
    }

    private static (EmbeddedFontFormat Format, byte[]? Bytes) ReadEmbeddedFont(
        PdfDictionary? descriptor,
        PdfDocumentCore document)
    {
        if (descriptor is null)
            return (EmbeddedFontFormat.None, null);

        PdfStream? stream;
        EmbeddedFontFormat format;
        if ((stream = descriptor.GetValueOrNull("FontFile2").AsStream(document)) is not null)
        {
            format = EmbeddedFontFormat.TrueType;
        }
        else if ((stream = descriptor.GetValueOrNull("FontFile3").AsStream(document)) is not null)
        {
            string? subtype = stream.Dictionary.GetValueOrNull("Subtype").AsName(document);
            format = subtype switch
            {
                "Type1C" or "CIDFontType0C" => EmbeddedFontFormat.Cff,
                "OpenType" => EmbeddedFontFormat.OpenType,
                _ => EmbeddedFontFormat.Cff
            };
        }
        else if ((stream = descriptor.GetValueOrNull("FontFile").AsStream(document)) is not null)
        {
            format = EmbeddedFontFormat.Type1;
        }
        else
        {
            return (EmbeddedFontFormat.None, null);
        }

        try
        {
            return (format, document.Decode(stream));
        }
        catch (PdfException)
        {
            return (format, stream.EncodedBytes.ToArray());
        }
    }

    private static (bool Identity, ushort[]? Map) ReadCidToGlyph(
        PdfDictionary dictionary,
        PdfDocumentCore document)
    {
        PdfObject? value = dictionary.GetValueOrNull("CIDToGIDMap");
        if (value.AsName(document) == "Identity" || value is null)
            return (true, null);
        PdfStream? stream = value.AsStream(document);
        if (stream is null)
            return (false, null);
        byte[] bytes = document.Decode(stream);
        if (bytes.Length / 2 > document.Options.MaximumCMapMappings)
        {
            throw new PdfLimitException(
                $"CIDToGIDMap exceeds the {document.Options.MaximumCMapMappings} mapping limit.");
        }
        var result = new ushort[bytes.Length / 2];
        for (int index = 0; index < result.Length; index++)
            result[index] = (ushort)((bytes[index * 2] << 8) | bytes[index * 2 + 1]);
        return (false, result);
    }

    private static string? ReadCollection(
        PdfDictionary dictionary,
        PdfDocumentCore document)
    {
        PdfDictionary? info =
            dictionary.GetValueOrNull("CIDSystemInfo").AsDictionary(document);
        string? registry = ReadText(info?.GetValueOrNull("Registry"), document);
        string? ordering = ReadText(info?.GetValueOrNull("Ordering"), document);
        return registry is not null && ordering is not null
            ? $"{registry}-{ordering}"
            : "Adobe-Identity";
    }

    private static string? ReadText(PdfObject? value, PdfDocumentCore document) =>
        value?.Resolve(document) switch
        {
            PdfString text => text.Text,
            PdfName name => name.Value,
            _ => null
        };

    private static PdfFontType DetermineFontType(
        string declaredSubtype,
        string effectiveSubtype,
        EmbeddedFontFormat embeddedFormat)
    {
        if (declaredSubtype == "Type0")
        {
            return effectiveSubtype switch
            {
                "CIDFontType0" => PdfFontType.CidType0,
                "CIDFontType2" => PdfFontType.CidType2,
                _ => PdfFontType.Unknown
            };
        }

        if (declaredSubtype == "Type3")
            return PdfFontType.Type3;
        if (embeddedFormat == EmbeddedFontFormat.OpenType)
            return PdfFontType.OpenType;
        if (embeddedFormat == EmbeddedFontFormat.Cff)
            return PdfFontType.Type1C;
        return declaredSubtype switch
        {
            "Type1" or "MMType1" => PdfFontType.Type1,
            "TrueType" => PdfFontType.TrueType,
            _ => PdfFontType.Unknown
        };
    }

    private static double ReadType3WidthScale(
        PdfDictionary dictionary,
        PdfDocumentCore document)
    {
        PdfArray? matrix = dictionary.GetValueOrNull("FontMatrix").AsArray(document);
        double a = matrix is { Count: >= 1 }
            ? matrix[0].AsNumber(document) ?? 0.001
            : 0.001;
        double b = matrix is { Count: >= 2 }
            ? matrix[1].AsNumber(document) ?? 0
            : 0;
        double scale = Math.Sqrt(a * a + b * b);
        return scale * 1000;
    }

    private static global::Poppler.PdfMatrix ReadType3Matrix(
        PdfDictionary dictionary,
        PdfDocumentCore document)
    {
        PdfArray? values =
            dictionary.GetValueOrNull("FontMatrix").AsArray(document);
        if (values is not { Count: >= 6 })
            return new global::Poppler.PdfMatrix(0.001, 0, 0, 0.001, 0, 0);
        var numbers = new double[6];
        for (int index = 0; index < numbers.Length; index++)
        {
            double? value = values[index].AsNumber(document);
            if (!value.HasValue || !double.IsFinite(value.Value))
                return new global::Poppler.PdfMatrix(0.001, 0, 0, 0.001, 0, 0);
            numbers[index] = value.Value;
        }
        return new global::Poppler.PdfMatrix(
            numbers[0], numbers[1], numbers[2],
            numbers[3], numbers[4], numbers[5]);
    }

    private static double NormalizeMetric(double? value, double fallback) =>
        value.HasValue && double.IsFinite(value.Value)
            ? value.Value / 1000
            : fallback;

    private static bool IsSubsetName(string name) =>
        name.Length > 7 &&
        name[6] == '+' &&
        name.AsSpan(0, 6).ToString().All(character => character is >= 'A' and <= 'Z');

    private static double Base14DefaultWidth(string name) =>
        name.Contains("Courier", StringComparison.Ordinal) ? 600 : 500;

    [GeneratedRegex(@"(?:^|\s)dup\s+([0-9]{1,3})\s+/([^\s\[\]{}()<>/%]+)\s+put(?:\s|$)")]
    private static partial Regex Type1EncodingRegex();
}
