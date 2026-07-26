using System.Globalization;
using System.Text;
using Poppler.Core;

namespace Poppler.Text;

internal static class PdfGlyphNames
{
    private static readonly IReadOnlyDictionary<string, string> Names =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["space"] = " ", ["nonbreakingspace"] = "\u00A0", ["nbspace"] = "\u00A0",
            ["exclam"] = "!", ["quotedbl"] = "\"", ["numbersign"] = "#", ["dollar"] = "$",
            ["percent"] = "%", ["ampersand"] = "&", ["quotesingle"] = "'",
            ["parenleft"] = "(", ["parenright"] = ")", ["asterisk"] = "*", ["plus"] = "+",
            ["comma"] = ",", ["hyphen"] = "-", ["minus"] = "-", ["period"] = ".",
            ["slash"] = "/", ["colon"] = ":", ["semicolon"] = ";", ["less"] = "<",
            ["equal"] = "=", ["greater"] = ">", ["question"] = "?", ["at"] = "@",
            ["bracketleft"] = "[", ["backslash"] = "\\", ["bracketright"] = "]",
            ["asciicircum"] = "^", ["underscore"] = "_", ["grave"] = "`",
            ["braceleft"] = "{", ["bar"] = "|", ["braceright"] = "}", ["asciitilde"] = "~",
            ["zero"] = "0", ["one"] = "1", ["two"] = "2", ["three"] = "3", ["four"] = "4",
            ["five"] = "5", ["six"] = "6", ["seven"] = "7", ["eight"] = "8", ["nine"] = "9",
            ["bullet"] = "•", ["endash"] = "–", ["emdash"] = "—", ["ellipsis"] = "…",
            ["quotedblleft"] = "“", ["quotedblright"] = "”", ["quoteleft"] = "‘",
            ["quoteright"] = "’", ["quotesinglbase"] = "‚", ["quotedblbase"] = "„",
            ["guillemotleft"] = "«", ["guillemotright"] = "»", ["guilsinglleft"] = "‹",
            ["guilsinglright"] = "›", ["Euro"] = "€", ["sterling"] = "£", ["yen"] = "¥",
            ["cent"] = "¢", ["currency"] = "¤", ["copyright"] = "©", ["registered"] = "®",
            ["trademark"] = "™", ["section"] = "§", ["paragraph"] = "¶", ["degree"] = "°",
            ["plusminus"] = "±", ["multiply"] = "×", ["divide"] = "÷", ["mu"] = "µ",
            ["logicalnot"] = "¬", ["brokenbar"] = "¦", ["ordfeminine"] = "ª",
            ["ordmasculine"] = "º", ["onequarter"] = "¼", ["onehalf"] = "½",
            ["threequarters"] = "¾", ["onesuperior"] = "¹", ["twosuperior"] = "²",
            ["threesuperior"] = "³", ["fi"] = "fi", ["fl"] = "fl", ["ff"] = "ff",
            ["ffi"] = "ffi", ["ffl"] = "ffl", ["AE"] = "Æ", ["ae"] = "æ",
            ["OE"] = "Œ", ["oe"] = "œ", ["Oslash"] = "Ø", ["oslash"] = "ø",
            ["Lslash"] = "Ł", ["lslash"] = "ł", ["Eth"] = "Ð", ["eth"] = "ð",
            ["Thorn"] = "Þ", ["thorn"] = "þ", ["germandbls"] = "ß", ["dotlessi"] = "ı",
            ["Agrave"] = "À", ["Aacute"] = "Á", ["Acircumflex"] = "Â", ["Atilde"] = "Ã",
            ["Adieresis"] = "Ä", ["Aring"] = "Å", ["Ccedilla"] = "Ç", ["Egrave"] = "È",
            ["Eacute"] = "É", ["Ecircumflex"] = "Ê", ["Edieresis"] = "Ë",
            ["Igrave"] = "Ì", ["Iacute"] = "Í", ["Icircumflex"] = "Î", ["Idieresis"] = "Ï",
            ["Ntilde"] = "Ñ", ["Ograve"] = "Ò", ["Oacute"] = "Ó", ["Ocircumflex"] = "Ô",
            ["Otilde"] = "Õ", ["Odieresis"] = "Ö", ["Ugrave"] = "Ù", ["Uacute"] = "Ú",
            ["Ucircumflex"] = "Û", ["Udieresis"] = "Ü", ["Yacute"] = "Ý",
            ["agrave"] = "à", ["aacute"] = "á", ["acircumflex"] = "â", ["atilde"] = "ã",
            ["adieresis"] = "ä", ["aring"] = "å", ["ccedilla"] = "ç", ["egrave"] = "è",
            ["eacute"] = "é", ["ecircumflex"] = "ê", ["edieresis"] = "ë",
            ["igrave"] = "ì", ["iacute"] = "í", ["icircumflex"] = "î", ["idieresis"] = "ï",
            ["ntilde"] = "ñ", ["ograve"] = "ò", ["oacute"] = "ó", ["ocircumflex"] = "ô",
            ["otilde"] = "õ", ["odieresis"] = "ö", ["ugrave"] = "ù", ["uacute"] = "ú",
            ["ucircumflex"] = "û", ["udieresis"] = "ü", ["yacute"] = "ý",
            ["ydieresis"] = "ÿ", ["Ydieresis"] = "Ÿ", ["Scaron"] = "Š", ["scaron"] = "š",
            ["Zcaron"] = "Ž", ["zcaron"] = "ž", ["florin"] = "ƒ",
            ["Alpha"] = "Α", ["Beta"] = "Β", ["Gamma"] = "Γ", ["Delta"] = "Δ",
            ["Epsilon"] = "Ε", ["Zeta"] = "Ζ", ["Eta"] = "Η", ["Theta"] = "Θ",
            ["Iota"] = "Ι", ["Kappa"] = "Κ", ["Lambda"] = "Λ", ["Mu"] = "Μ",
            ["Nu"] = "Ν", ["Xi"] = "Ξ", ["Omicron"] = "Ο", ["Pi"] = "Π",
            ["Rho"] = "Ρ", ["Sigma"] = "Σ", ["Tau"] = "Τ", ["Upsilon"] = "Υ",
            ["Phi"] = "Φ", ["Chi"] = "Χ", ["Psi"] = "Ψ", ["Omega"] = "Ω",
            ["alpha"] = "α", ["beta"] = "β", ["gamma"] = "γ", ["delta"] = "δ",
            ["epsilon"] = "ε", ["zeta"] = "ζ", ["eta"] = "η", ["theta"] = "θ",
            ["iota"] = "ι", ["kappa"] = "κ", ["lambda"] = "λ", ["nu"] = "ν",
            ["xi"] = "ξ", ["omicron"] = "ο", ["pi"] = "π", ["rho"] = "ρ",
            ["sigma"] = "σ", ["sigma1"] = "ς", ["tau"] = "τ", ["upsilon"] = "υ",
            ["phi"] = "φ", ["chi"] = "χ", ["psi"] = "ψ", ["omega"] = "ω",
            ["heart"] = "♥", ["club"] = "♣", ["diamond"] = "♦", ["spade"] = "♠",
            [".notdef"] = "\uFFFD"
        };

    private static readonly char[] MacRomanHigh =
    {
        'Ä','Å','Ç','É','Ñ','Ö','Ü','á','à','â','ä','ã','å','ç','é','è',
        'ê','ë','í','ì','î','ï','ñ','ó','ò','ô','ö','õ','ú','ù','û','ü',
        '†','°','¢','£','§','•','¶','ß','®','©','™','´','¨','≠','Æ','Ø',
        '∞','±','≤','≥','¥','µ','∂','Σ','Π','π','∫','ª','º','Ω','æ','ø',
        '¿','¡','¬','√','ƒ','≈','∆','«','»','…','\u00A0','À','Ã','Õ','Œ','œ',
        '–','—','“','”','‘','’','÷','◊','ÿ','Ÿ','⁄','€','‹','›','ﬁ','ﬂ',
        '‡','·','‚','„','‰','Â','Ê','Á','Ë','È','Í','Î','Ï','Ì','Ó','Ô',
        '\uF8FF','Ò','Ú','Û','Ù','ı','ˆ','˜','¯','˘','˙','˚','¸','˝','˛','ˇ'
    };

    public static string ToUnicode(string glyphName)
    {
        if (string.IsNullOrEmpty(glyphName))
            return "\uFFFD";

        int suffix = glyphName.IndexOf('.');
        if (suffix > 0)
            glyphName = glyphName[..suffix];
        if (glyphName.Contains('_'))
        {
            var builder = new StringBuilder();
            foreach (string component in glyphName.Split('_', StringSplitOptions.RemoveEmptyEntries))
                builder.Append(ToUnicode(component));
            return builder.Length == 0 ? "\uFFFD" : builder.ToString();
        }

        if (glyphName.Length == 1)
            return glyphName;
        if (Names.TryGetValue(glyphName, out string? value))
            return value;

        if (glyphName.StartsWith("uni", StringComparison.Ordinal) &&
            glyphName.Length > 3 &&
            (glyphName.Length - 3) % 4 == 0)
        {
            var builder = new StringBuilder();
            for (int index = 3; index < glyphName.Length; index += 4)
            {
                if (!ushort.TryParse(
                        glyphName.AsSpan(index, 4),
                        NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture,
                        out ushort code))
                {
                    return "\uFFFD";
                }

                builder.Append((char)code);
            }

            return builder.ToString();
        }

        if (glyphName.StartsWith('u') &&
            glyphName.Length is >= 5 and <= 7 &&
            int.TryParse(
                glyphName.AsSpan(1),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out int scalar) &&
            Rune.TryCreate(scalar, out Rune rune))
        {
            return rune.ToString();
        }

        return "\uFFFD";
    }

    public static string DecodeEncodingByte(byte value, string encoding)
    {
        if (encoding == "MacRomanEncoding" && value >= 0x80)
            return MacRomanHigh[value - 0x80].ToString();
        if (encoding == "Symbol")
            return DecodeSymbol(value);
        if (encoding == "ZapfDingbats")
            return DecodeZapf(value);
        if (encoding == "StandardEncoding")
        {
            return value switch
            {
                0x27 => "’",
                0x60 => "‘",
                0xA1 => "¡",
                0xA2 => "¢",
                0xA3 => "£",
                0xA5 => "¥",
                0xAE => "ﬁ",
                0xAF => "ﬂ",
                _ => value is >= 0x20 and <= 0x7E ? ((char)value).ToString() : "\uFFFD"
            };
        }

        return PdfTextEncoding.DecodeWindows1252(new[] { value });
    }

    private static string DecodeSymbol(byte value)
    {
        if (value is >= (byte)'A' and <= (byte)'Z')
        {
            const string capitals = "ΑΒΧΔΕΦΓΗΙϑΚΛΜΝΟΠΘΡΣΤΥςΩΞΨΖ";
            return capitals[value - 'A'].ToString();
        }

        if (value is >= (byte)'a' and <= (byte)'z')
        {
            const string lowercase = "αβχδεφγηιϕκλμνοπθρστυϖωξψζ";
            return lowercase[value - 'a'].ToString();
        }

        return value is >= 0x20 and <= 0x7E ? ((char)value).ToString() : "\uFFFD";
    }

    private static string DecodeZapf(byte value) => value switch
    {
        0x20 => " ",
        0x21 => "✁", 0x22 => "✂", 0x23 => "✃", 0x24 => "✄",
        0x25 => "☎", 0x26 => "✆", 0x27 => "✇", 0x28 => "✈",
        0x29 => "✉", 0x2A => "☛", 0x2B => "☞", 0x2C => "✌",
        0x33 => "✓", 0x34 => "✔", 0x35 => "✕", 0x36 => "✖",
        0x48 => "★", 0x49 => "✩", 0x6C => "●", 0x6D => "❍",
        _ => "\uFFFD"
    };
}
