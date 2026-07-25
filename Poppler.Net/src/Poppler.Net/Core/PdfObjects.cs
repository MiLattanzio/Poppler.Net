using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace Poppler.Core;

public abstract class PdfObject
{
}

public sealed class PdfNull : PdfObject
{
    private PdfNull()
    {
    }

    public static PdfNull Instance { get; } = new();
    public override string ToString() => "null";
}

public sealed class PdfBoolean : PdfObject
{
    public PdfBoolean(bool value) => Value = value;
    public bool Value { get; }
    public override string ToString() => Value ? "true" : "false";
}

public sealed class PdfNumber : PdfObject
{
    public PdfNumber(double value, bool isInteger = false)
    {
        Value = value;
        IsInteger = isInteger;
    }

    public double Value { get; }
    public bool IsInteger { get; }
    public int IntegerValue => checked((int)Value);
    public long LongValue => checked((long)Value);

    public override string ToString() =>
        Value.ToString(IsInteger ? "0" : "0.################", CultureInfo.InvariantCulture);
}

public sealed class PdfName : PdfObject, IEquatable<PdfName>
{
    public PdfName(string value) =>
        Value = value ?? throw new ArgumentNullException(nameof(value));

    public string Value { get; }

    public bool Equals(PdfName? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as PdfName);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
    public override string ToString() => "/" + Value;
}

public sealed class PdfString : PdfObject
{
    private readonly byte[] _bytes;

    public PdfString(ReadOnlySpan<byte> bytes) => _bytes = bytes.ToArray();
    public ReadOnlyMemory<byte> Bytes => _bytes;
    public string Text => PdfTextEncoding.DecodePdfString(_bytes);
    public override string ToString() => $"({Text})";
}

public sealed class PdfArray : PdfObject, IReadOnlyList<PdfObject>
{
    private readonly ReadOnlyCollection<PdfObject> _items;

    public PdfArray(IEnumerable<PdfObject> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = Array.AsReadOnly(items.ToArray());
    }

    public int Count => _items.Count;
    public PdfObject this[int index] => _items[index];
    public IEnumerator<PdfObject> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public override string ToString() => $"[{string.Join(" ", _items)}]";
}

public sealed class PdfDictionary : PdfObject, IReadOnlyDictionary<string, PdfObject>
{
    private readonly ReadOnlyDictionary<string, PdfObject> _items;

    public PdfDictionary(IEnumerable<KeyValuePair<string, PdfObject>> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = new ReadOnlyDictionary<string, PdfObject>(
            items.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    }

    public PdfDictionary(IDictionary<string, PdfObject> items)
        : this(items.AsEnumerable())
    {
    }

    public IEnumerable<string> Keys => _items.Keys;
    public IEnumerable<PdfObject> Values => _items.Values;
    public int Count => _items.Count;
    public PdfObject this[string key] => _items[key];
    public bool ContainsKey(string key) => _items.ContainsKey(key);
    public bool TryGetValue(string key, out PdfObject value) => _items.TryGetValue(key, out value!);
    public IEnumerator<KeyValuePair<string, PdfObject>> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public override string ToString() => $"<< {string.Join(" ", _items.Select(x => $"/{x.Key} {x.Value}"))} >>";
}

public sealed class PdfReference : PdfObject, IEquatable<PdfReference>
{
    public PdfReference(int objectNumber, int generation)
    {
        if (objectNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(objectNumber));
        if (generation < 0)
            throw new ArgumentOutOfRangeException(nameof(generation));
        ObjectNumber = objectNumber;
        Generation = generation;
    }

    public int ObjectNumber { get; }
    public int Generation { get; }

    public bool Equals(PdfReference? other) =>
        other is not null &&
        ObjectNumber == other.ObjectNumber &&
        Generation == other.Generation;

    public override bool Equals(object? obj) => Equals(obj as PdfReference);
    public override int GetHashCode() => HashCode.Combine(ObjectNumber, Generation);
    public override string ToString() => $"{ObjectNumber} {Generation} R";
}

public sealed class PdfStream : PdfObject
{
    private readonly byte[] _encodedBytes;

    public PdfStream(PdfDictionary dictionary, ReadOnlySpan<byte> encodedBytes)
    {
        Dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
        _encodedBytes = encodedBytes.ToArray();
    }

    public PdfDictionary Dictionary { get; }
    public ReadOnlyMemory<byte> EncodedBytes => _encodedBytes;
    public override string ToString() => $"{Dictionary} stream ({_encodedBytes.Length} bytes)";
}

internal sealed class PdfKeyword : PdfObject
{
    public PdfKeyword(string value) => Value = value;
    public string Value { get; }
    public override string ToString() => Value;
}

internal static class PdfTextEncoding
{
    private static readonly char[] Windows1252 =
    {
        '\u20AC', '\uFFFD', '\u201A', '\u0192', '\u201E', '\u2026', '\u2020', '\u2021',
        '\u02C6', '\u2030', '\u0160', '\u2039', '\u0152', '\uFFFD', '\u017D', '\uFFFD',
        '\uFFFD', '\u2018', '\u2019', '\u201C', '\u201D', '\u2022', '\u2013', '\u2014',
        '\u02DC', '\u2122', '\u0161', '\u203A', '\u0153', '\uFFFD', '\u017E', '\u0178'
    };

    public static string DecodePdfString(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes[2..]);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes[2..]);

        var builder = new StringBuilder(bytes.Length);
        foreach (byte value in bytes)
        {
            if (value is >= 0x80 and <= 0x9F)
                builder.Append(Windows1252[value - 0x80]);
            else
                builder.Append((char)value);
        }

        return builder.ToString();
    }

    public static string DecodeWindows1252(ReadOnlySpan<byte> bytes)
    {
        var builder = new StringBuilder(bytes.Length);
        foreach (byte value in bytes)
        {
            if (value is >= 0x80 and <= 0x9F)
                builder.Append(Windows1252[value - 0x80]);
            else
                builder.Append((char)value);
        }

        return builder.ToString();
    }
}
