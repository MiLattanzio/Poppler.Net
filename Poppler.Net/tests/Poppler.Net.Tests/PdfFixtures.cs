using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Poppler.Net.Tests;

internal static class PdfFixtures
{
    public static byte[] Create(bool compressContent)
    {
        byte[] plainContent = Encoding.ASCII.GetBytes(
            "BT /F1 18 Tf 72 720 Td (Hello managed PDF ) Tj ET");
        byte[] content = compressContent ? Compress(plainContent) : plainContent;
        string filter = compressContent ? " /Filter /FlateDecode" : "";
        byte[] attachment = Encoding.ASCII.GetBytes("attachment payload");

        var objects = new[]
        {
            Ascii(
                "<< /Type /Catalog /Pages 2 0 R /PageMode /UseOutlines /PageLayout /SinglePage " +
                "/PageLabels << /Nums [0 << /P (A-) /S /D >>] >> " +
                "/Names << /EmbeddedFiles << /Names [(hello.txt) 7 0 R] >> >> >>"),
            Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Ascii(
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] " +
                "/Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>"),
            Stream($"<< /Length {content.Length}{filter} >>", content),
            Ascii(
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica " +
                "/Encoding /WinAnsiEncoding /FirstChar 0 /Widths [] >>"),
            Ascii(
                "<< /Title (Managed fixture) /Producer (Poppler.Net tests) " +
                "/CreationDate (D:20260726010000+02'00') >>"),
            Ascii(
                "<< /Type /Filespec /F (hello.txt) /UF (hello.txt) " +
                "/Desc (fixture attachment) /EF << /F 8 0 R >> >>"),
            Stream(
                $"<< /Type /EmbeddedFile /Subtype /text#2Fplain /Length {attachment.Length} " +
                $"/Params << /Size {attachment.Length} >> >>",
                attachment)
        };
        return BuildClassic(objects);
    }

    public static byte[] CreateWithXrefStream()
    {
        byte[] content = Ascii("BT /F1 16 Tf 50 700 Td (Compressed font object) Tj ET");
        byte[] fontObject = Ascii(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        byte[] objectStreamHeader = Ascii("5 0 ");
        byte[] objectStreamData = objectStreamHeader.Concat(fontObject).ToArray();

        using var output = new MemoryStream();
        Write(output, "%PDF-1.7\n%");
        output.Write(new byte[] { 0xE2, 0xE3, 0xCF, 0xD3 });
        Write(output, "\n");
        var offsets = new Dictionary<int, long>();
        WriteObject(output, offsets, 1, Ascii("<< /Type /Catalog /Pages 2 0 R >>"));
        WriteObject(output, offsets, 2, Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"));
        WriteObject(
            output,
            offsets,
            3,
            Ascii(
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] " +
                "/Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>"));
        WriteObject(output, offsets, 4, Stream($"<< /Length {content.Length} >>", content));
        WriteObject(
            output,
            offsets,
            6,
            Stream(
                $"<< /Type /ObjStm /N 1 /First {objectStreamHeader.Length} " +
                $"/Length {objectStreamData.Length} >>",
                objectStreamData));

        long xrefOffset = output.Position;
        offsets[7] = xrefOffset;
        byte[] xrefData = BuildXrefEntries(offsets);
        Write(
            output,
            $"7 0 obj\n<< /Type /XRef /Size 8 /Root 1 0 R /W [1 4 2] " +
            $"/Index [0 8] /Length {xrefData.Length} >>\nstream\n");
        output.Write(xrefData);
        Write(output, "\nendstream\nendobj\n");
        Write(output, $"startxref\n{xrefOffset}\n%%EOF\n");
        return output.ToArray();
    }

    public static byte[] CreateWithIncrementalUpdate()
    {
        byte[] original = Create(compressContent: false);
        int previousXref = ReadFinalStartXref(original);
        using var output = new MemoryStream();
        output.Write(original);
        long updatedInfoOffset = output.Position;
        Write(
            output,
            "6 0 obj\n<< /Title (Updated title) /Producer (Incremental update) >>\nendobj\n");
        long xrefOffset = output.Position;
        Write(output, "xref\n6 1\n");
        Write(output, $"{updatedInfoOffset:0000000000} 00000 n \n");
        Write(
            output,
            $"trailer\n<< /Size 9 /Root 1 0 R /Info 6 0 R /Prev {previousXref} >>\n");
        Write(output, $"startxref\n{xrefOffset}\n%%EOF\n");
        return output.ToArray();
    }

    public static byte[] CreateWithLeadingGarbage()
    {
        byte[] prefix = Ascii("transport-prefix\r\n");
        return prefix.Concat(Create(compressContent: false)).ToArray();
    }

    public static byte[] CreateWithBrokenStartXref()
    {
        return BreakStartXref(Create(compressContent: false));
    }

    public static byte[] CreateXrefStreamWithBrokenStartXref()
    {
        return BreakStartXref(CreateWithXrefStream());
    }

    private static byte[] BreakStartXref(byte[] result)
    {
        int marker = result.AsSpan().LastIndexOf("startxref"u8);
        int position = marker + "startxref".Length;
        while (position < result.Length && IsWhiteSpace(result[position]))
            position++;
        while (position < result.Length && result[position] is >= (byte)'0' and <= (byte)'9')
            result[position++] = (byte)'0';
        return result;
    }

    public static byte[] CreateWithWrongPageGeneration()
    {
        byte[] result = Create(compressContent: false);
        ReplaceSameLength(result, "/Kids [3 0 R]", "/Kids [3 1 R]");
        return result;
    }

    public static byte[] CreateWithInvalidFlateContent()
    {
        byte[] result = Create(compressContent: true);
        int filter = result.AsSpan().IndexOf("/Filter /FlateDecode"u8);
        int streamMarker = result.AsSpan(filter).IndexOf("stream\n"u8) + filter;
        int streamStart = streamMarker + "stream\n".Length;
        int streamEnd = result.AsSpan(streamStart).IndexOf("\nendstream"u8) + streamStart;
        result.AsSpan(streamStart, streamEnd - streamStart).Fill(0xFF);
        return result;
    }

    public static byte[] CreateWithoutEndOfFileMarker()
    {
        byte[] result = Create(compressContent: false);
        int marker = result.AsSpan().LastIndexOf("%%EOF"u8);
        return result.AsSpan(0, marker).ToArray();
    }

    private static byte[] BuildClassic(IReadOnlyList<byte[]> objects)
    {
        using var output = new MemoryStream();
        Write(output, "%PDF-1.7\n%");
        output.Write(new byte[] { 0xE2, 0xE3, 0xCF, 0xD3 });
        Write(output, "\n");
        var offsets = new List<long> { 0 };
        for (int index = 0; index < objects.Count; index++)
        {
            offsets.Add(output.Position);
            Write(output, $"{index + 1} 0 obj\n");
            output.Write(objects[index]);
            Write(output, "\nendobj\n");
        }

        long xref = output.Position;
        Write(output, $"xref\n0 {objects.Count + 1}\n");
        Write(output, "0000000000 65535 f \n");
        foreach (long offset in offsets.Skip(1))
            Write(output, $"{offset:0000000000} 00000 n \n");
        Write(
            output,
            $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R /Info 6 0 R " +
            "/ID [<00112233445566778899AABBCCDDEEFF> <FFEEDDCCBBAA99887766554433221100>] >>\n");
        Write(output, $"startxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }

    private static byte[] Stream(string dictionary, byte[] bytes)
    {
        using var output = new MemoryStream();
        output.Write(Ascii(dictionary));
        output.Write(Ascii("\nstream\n"));
        output.Write(bytes);
        output.Write(Ascii("\nendstream"));
        return output.ToArray();
    }

    private static void WriteObject(
        Stream output,
        IDictionary<int, long> offsets,
        int objectNumber,
        byte[] value)
    {
        offsets[objectNumber] = output.Position;
        Write(output, $"{objectNumber} 0 obj\n");
        output.Write(value);
        Write(output, "\nendobj\n");
    }

    private static byte[] BuildXrefEntries(IReadOnlyDictionary<int, long> offsets)
    {
        using var output = new MemoryStream();
        WriteXrefEntry(output, 0, 0, 65535);
        for (int objectNumber = 1; objectNumber <= 4; objectNumber++)
            WriteXrefEntry(output, 1, offsets[objectNumber], 0);
        WriteXrefEntry(output, 2, 6, 0);
        WriteXrefEntry(output, 1, offsets[6], 0);
        WriteXrefEntry(output, 1, offsets[7], 0);
        return output.ToArray();
    }

    private static void WriteXrefEntry(Stream output, byte type, long field1, int field2)
    {
        output.WriteByte(type);
        output.WriteByte((byte)(field1 >> 24));
        output.WriteByte((byte)(field1 >> 16));
        output.WriteByte((byte)(field1 >> 8));
        output.WriteByte((byte)field1);
        output.WriteByte((byte)(field2 >> 8));
        output.WriteByte((byte)field2);
    }

    private static int ReadFinalStartXref(byte[] data)
    {
        int marker = data.AsSpan().LastIndexOf("startxref"u8);
        int position = marker + "startxref".Length;
        while (position < data.Length && IsWhiteSpace(data[position]))
            position++;
        int start = position;
        while (position < data.Length && data[position] is >= (byte)'0' and <= (byte)'9')
            position++;
        return int.Parse(
            Encoding.ASCII.GetString(data, start, position - start),
            NumberStyles.None,
            CultureInfo.InvariantCulture);
    }

    private static void ReplaceSameLength(byte[] data, string oldValue, string newValue)
    {
        byte[] oldBytes = Ascii(oldValue);
        byte[] newBytes = Ascii(newValue);
        if (oldBytes.Length != newBytes.Length)
            throw new InvalidOperationException("Fixture replacements must preserve xref offsets.");
        int index = data.AsSpan().IndexOf(oldBytes);
        if (index < 0)
            throw new InvalidOperationException($"Fixture token '{oldValue}' was not found.");
        newBytes.CopyTo(data, index);
    }

    private static byte[] Compress(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(bytes);
        return output.ToArray();
    }

    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);

    private static void Write(Stream stream, string value) => stream.Write(Ascii(value));

    private static bool IsWhiteSpace(byte value) =>
        value is 0 or (byte)'\t' or (byte)'\n' or (byte)'\f' or (byte)'\r' or (byte)' ';
}
