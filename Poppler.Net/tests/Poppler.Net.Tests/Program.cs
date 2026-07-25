using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Poppler;

var tests = new (string Name, Action Test)[]
{
    ("classic xref, catalog and metadata", Tests.ReadsClassicDocument),
    ("page geometry, labels and text", Tests.ReadsPageAndText),
    ("Flate content stream", Tests.DecodesFlateContent),
    ("xref and compressed object streams", Tests.ReadsXrefAndObjectStreams),
    ("embedded file extraction", Tests.ExtractsEmbeddedFile),
    ("byte-for-byte save copy", Tests.SavesCopy),
    ("managed-only assembly", Tests.HasNoNativeInterop)
};

int failures = 0;
foreach ((string name, Action test) in tests)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {exception}");
    }
}

Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed");
return failures == 0 ? 0 : 1;

internal static class Tests
{
    public static void ReadsClassicDocument()
    {
        using Document document = Document.LoadFromData(PdfFixtures.Create(compressContent: false));
        Assert.Equal("1.7", document.PdfVersion);
        Assert.Equal(1, document.Pages);
        Assert.Equal("Managed fixture", document.Title);
        Assert.Equal("Poppler.Net tests", document.Producer);
        Assert.False(document.IsEncrypted);
        Assert.False(document.XrefWasRepaired);
        Assert.Equal(PageMode.UseOutlines, document.PageMode);
        Assert.Equal(PageLayout.SinglePage, document.PageLayout);
        Assert.True(document.PdfId is not null);
    }

    public static void ReadsPageAndText()
    {
        using Document document = Document.LoadFromData(PdfFixtures.Create(compressContent: false));
        Page page = document.CreatePage(0);
        Assert.Equal("A-1", page.Label);
        Assert.Equal(new PdfRectangle(0, 0, 612, 792), page.PageRect(PageBox.MediaBox));
        Assert.Equal(PageOrientation.Portrait, page.Orientation);
        string text = page.Text();
        Assert.Contains("Hello managed PDF", text);
        TextBox box = Assert.Single(page.TextList());
        Assert.Contains("Hello managed PDF", box.Text);
        Assert.Equal("Helvetica", box.FontName);
        Assert.True(page.Search("managed").Count == 1);
        Assert.Contains("<svg", page.RenderToSvg());
    }

    public static void DecodesFlateContent()
    {
        using Document document = Document.LoadFromData(PdfFixtures.Create(compressContent: true));
        Assert.Contains("Hello managed PDF", document.CreatePage(0).Text());
    }

    public static void ReadsXrefAndObjectStreams()
    {
        using Document document = Document.LoadFromData(PdfFixtures.CreateWithXrefStream());
        Assert.Equal(1, document.Pages);
        Assert.Contains("Compressed font object", document.CreatePage(0).Text());
    }

    public static void ExtractsEmbeddedFile()
    {
        using Document document = Document.LoadFromData(PdfFixtures.Create(compressContent: false));
        EmbeddedFile file = Assert.Single(document.EmbeddedFiles);
        Assert.Equal("hello.txt", file.Name);
        Assert.Equal("text/plain", file.MimeType);
        Assert.Equal("attachment payload", Encoding.ASCII.GetString(file.Data.Span));
    }

    public static void SavesCopy()
    {
        byte[] source = PdfFixtures.Create(compressContent: false);
        using Document document = Document.LoadFromData(source);
        string path = Path.Combine(Path.GetTempPath(), $"poppler-net-{Guid.NewGuid():N}.pdf");
        try
        {
            document.SaveACopy(path);
            Assert.SequenceEqual(source, File.ReadAllBytes(path));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    public static void HasNoNativeInterop()
    {
        Assembly assembly = typeof(Document).Assembly;
        MethodInfo[] methods = assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Static | BindingFlags.Instance))
            .ToArray();
        Assert.False(methods.Any(method =>
            method.GetCustomAttributes(typeof(DllImportAttribute), inherit: false).Length > 0));
        string[] references = assembly.GetReferencedAssemblies().Select(name => name.Name ?? "").ToArray();
        Assert.False(references.Any(name =>
            name.Contains("Cpp", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Native", StringComparison.OrdinalIgnoreCase)));
    }
}

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

    private static byte[] Compress(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(bytes);
        return output.ToArray();
    }

    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);

    private static void Write(Stream stream, string value) => stream.Write(Ascii(value));
}

internal static class Assert
{
    public static void True(bool condition)
    {
        if (!condition)
            throw new InvalidOperationException("Expected true.");
    }

    public static void False(bool condition) => True(!condition);

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    public static void Contains(string expectedPart, string actual)
    {
        if (!actual.Contains(expectedPart, StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected '{actual}' to contain '{expectedPart}'.");
    }

    public static T Single<T>(IReadOnlyList<T> values)
    {
        if (values.Count != 1)
            throw new InvalidOperationException($"Expected one item, got {values.Count}.");
        return values[0];
    }

    public static void SequenceEqual(byte[] expected, byte[] actual)
    {
        if (!expected.AsSpan().SequenceEqual(actual))
            throw new InvalidOperationException("Byte sequences differ.");
    }
}
