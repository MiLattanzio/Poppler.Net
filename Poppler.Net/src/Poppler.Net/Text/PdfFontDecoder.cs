using System.Text.RegularExpressions;
using System.Text;
using Poppler.Core;

namespace Poppler.Text;

internal sealed class PdfFontDecoder
{
    private static readonly IReadOnlyDictionary<string, string> GlyphNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["space"] = " ", ["exclam"] = "!", ["quotedbl"] = "\"", ["numbersign"] = "#",
            ["dollar"] = "$", ["percent"] = "%", ["ampersand"] = "&", ["quotesingle"] = "'",
            ["parenleft"] = "(", ["parenright"] = ")", ["asterisk"] = "*", ["plus"] = "+",
            ["comma"] = ",", ["hyphen"] = "-", ["minus"] = "-", ["period"] = ".",
            ["slash"] = "/", ["colon"] = ":", ["semicolon"] = ";", ["less"] = "<",
            ["equal"] = "=", ["greater"] = ">", ["question"] = "?", ["at"] = "@",
            ["bracketleft"] = "[", ["backslash"] = "\\", ["bracketright"] = "]",
            ["asciicircum"] = "^", ["underscore"] = "_", ["grave"] = "`",
            ["braceleft"] = "{", ["bar"] = "|", ["braceright"] = "}", ["asciitilde"] = "~",
            ["bullet"] = "•", ["endash"] = "–", ["emdash"] = "—", ["ellipsis"] = "…",
            ["quotedblleft"] = "“", ["quotedblright"] = "”", ["quoteleft"] = "‘",
            ["quoteright"] = "’", ["Euro"] = "€", ["copyright"] = "©", ["registered"] = "®",
            ["trademark"] = "™", ["fi"] = "fi", ["fl"] = "fl", ["AE"] = "Æ", ["ae"] = "æ",
            ["OE"] = "Œ", ["oe"] = "œ", ["Oslash"] = "Ø", ["oslash"] = "ø",
            ["Lslash"] = "Ł", ["lslash"] = "ł", ["germandbls"] = "ß"
        };

    private readonly PdfCMap _cmap;
    private readonly Dictionary<byte, string> _differences = new();
    private readonly double[]? _widths;
    private readonly int _firstCharacter;
    private readonly bool _identityEncoding;

    public PdfFontDecoder(PdfDictionary dictionary, PdfDocumentCore document)
    {
        Name =
            dictionary.GetValueOrNull("BaseFont").AsName(document) ??
            dictionary.GetValueOrNull("Name").AsName(document) ??
            "Unknown";
        _identityEncoding =
            dictionary.GetValueOrNull("Encoding").AsName(document) is "Identity-H" or "Identity-V";

        PdfStream? toUnicode = dictionary.GetValueOrNull("ToUnicode").AsStream(document);
        _cmap = toUnicode is null ? new PdfCMap() : PdfCMap.Parse(document.Decode(toUnicode));

        PdfObject? encodingObject = dictionary.GetValueOrNull("Encoding");
        PdfDictionary? encoding = encodingObject.AsDictionary(document);
        PdfArray? differences = encoding?.GetValueOrNull("Differences").AsArray(document);
        if (differences is not null)
            ReadDifferences(differences);

        _firstCharacter = dictionary.GetValueOrNull("FirstChar").AsInteger(document) ?? 0;
        PdfArray? widths = dictionary.GetValueOrNull("Widths").AsArray(document);
        if (widths is not null)
        {
            _widths = new double[widths.Count];
            for (int index = 0; index < widths.Count; index++)
                _widths[index] = widths[index].AsNumber(document) ?? 500;
        }
    }

    public string Name { get; }

    public string Decode(ReadOnlySpan<byte> bytes)
    {
        if (_cmap.HasMappings)
            return _cmap.Decode(bytes, DecodeByte);
        if (_identityEncoding && bytes.Length % 2 == 0)
        {
            var chars = new List<char>(bytes.Length / 2);
            for (int index = 0; index < bytes.Length; index += 2)
            {
                int code = (bytes[index] << 8) | bytes[index + 1];
                chars.Add(code <= char.MaxValue ? (char)code : '\uFFFD');
            }

            return new string(chars.ToArray());
        }

        return string.Concat(bytes.ToArray().Select(DecodeByte));
    }

    public double GetAdvance(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            return 0;
        double width = 0;
        if (_identityEncoding)
            return bytes.Length / 2.0 * 1000;
        foreach (byte value in bytes)
        {
            int index = value - _firstCharacter;
            width += _widths is not null && index >= 0 && index < _widths.Length
                ? _widths[index]
                : 500;
        }

        return width;
    }

    private string DecodeByte(byte value)
    {
        if (_differences.TryGetValue(value, out string? result))
            return result;
        return PdfTextEncoding.DecodeWindows1252(new[] { value });
    }

    private void ReadDifferences(PdfArray differences)
    {
        int current = 0;
        foreach (PdfObject value in differences)
        {
            if (value is PdfNumber number && number.IsInteger)
            {
                current = checked((int)number.Value);
            }
            else if (value is PdfName name && current is >= 0 and <= 255)
            {
                _differences[(byte)current] = GlyphNameToText(name.Value);
                current++;
            }
        }
    }

    private static string GlyphNameToText(string name)
    {
        if (name.Length == 1)
            return name;
        if (GlyphNames.TryGetValue(name, out string? value))
            return value;
        Match match = Regex.Match(name, @"^(?:uni([0-9A-Fa-f]{4,})|u([0-9A-Fa-f]{4,6}))$");
        string hex = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        if (hex.Length >= 4 &&
            int.TryParse(hex[..Math.Min(6, hex.Length)], System.Globalization.NumberStyles.HexNumber, null, out int code) &&
            Rune.TryCreate(code, out Rune rune))
        {
            return rune.ToString();
        }

        return "\uFFFD";
    }
}
