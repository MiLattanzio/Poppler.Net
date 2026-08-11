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

    public static byte[] CreateCoveredTextFixture()
    {
        var objects = new[]
        {
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Ascii(
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] " +
                "/Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>"),
            ContentStream(
                "BT /F1 18 Tf 72 120 Td (Covered text) Tj ET " +
                "0 0 0 rg 70 112 140 28 re f"),
            Ascii(
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica " +
                "/Encoding /WinAnsiEncoding >>")
        };
        return BuildClassic(objects, infoObject: null);
    }

    public static byte[] CreateFunctionShadingFixture()
    {
        byte[] sampledRgb =
        {
            0, 0, 0,
            255, 0, 0,
            0, 255, 0,
            255, 255, 0
        };
        byte[] sampledRed = { 0, 255, 0, 255 };
        byte[] sampledGreen = { 0, 0, 255, 255 };
        byte[] sampledBlue = { 128, 128, 128, 128 };
        byte[] calculator = Ascii("{ 0 }");
        var objects = new[]
        {
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            Ascii("<< /Type /Pages /Kids [3 0 R 7 0 R 11 0 R 17 0 R 20 0 R] /Count 5 >>"),
            Ascii(
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] " +
                "/Resources << /Shading << /S 5 0 R >> >> /Contents 4 0 R >>"),
            ContentStream("/S sh"),
            Ascii(
                "<< /ShadingType 1 /ColorSpace /DeviceRGB /Domain [2 4 10 20] " +
                "/Matrix [50 0 0 10 -100 -100] /BBox [25 0 75 100] /Function 6 0 R >>"),
            Stream(
                $"<< /FunctionType 0 /Domain [2 4 10 20] /Range [0 1 0 1 0 1] " +
                $"/Size [2 2] /BitsPerSample 8 /Decode [0 1 0 1 0 1] " +
                $"/Length {sampledRgb.Length} >>",
                sampledRgb),
            Ascii(
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] " +
                "/Resources << /Shading << /S 9 0 R >> >> /Contents 8 0 R >>"),
            ContentStream("0 0 50 100 re W n /S sh"),
            Ascii(
                "<< /ShadingType 1 /ColorSpace /DeviceRGB /Domain [0 1 0 1] " +
                "/Matrix [100 0 0 100 0 0] /Function 10 0 R >>"),
            Stream(
                $"<< /FunctionType 4 /Domain [0 1 0 1] /Range [0 1 0 1 0 1] " +
                $"/Length {calculator.Length} >>",
                calculator),
            Ascii(
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] " +
                "/Resources << /Shading << /S 13 0 R >> >> /Contents 12 0 R >>"),
            ContentStream("/S sh"),
            Ascii(
                "<< /ShadingType 1 /ColorSpace /DeviceRGB " +
                "/Matrix [100 0 0 100 0 0] /Function [14 0 R 15 0 R 16 0 R] >>"),
            SampledComponent(sampledRed),
            SampledComponent(sampledGreen),
            SampledComponent(sampledBlue),
            Ascii(
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] " +
                "/Resources << /Shading << /S 19 0 R >> >> /Contents 18 0 R >>"),
            ContentStream("/S sh"),
            Ascii(
                "<< /ShadingType 1 /ColorSpace /DeviceRGB /Domain [0 1 0 1] " +
                "/Matrix [0 0 0 0 50 50] /Function 10 0 R >>"),
            Ascii(
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] " +
                "/Resources << /Pattern << /P 22 0 R >> >> /Contents 21 0 R >>"),
            ContentStream("/Pattern cs /P scn 0 0 100 100 re f"),
            Ascii(
                "<< /Type /Pattern /PatternType 2 /Matrix [100 0 0 100 0 0] " +
                "/Shading 23 0 R >>"),
            Ascii(
                "<< /ShadingType 1 /ColorSpace /DeviceRGB /Domain [0 1 0 1] " +
                "/Matrix [1 0 0 1 0 0] /Function 10 0 R >>")
        };
        return BuildClassic(objects, infoObject: null);
    }

    public static byte[] CreateBoundedFallbackStrokeFixture()
    {
        byte[] sampledRgb =
        {
            0, 0, 0,
            255, 0, 0,
            0, 255, 0,
            255, 255, 0
        };
        var objects = new[]
        {
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Ascii(
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] " +
                "/Resources << /Shading << /S 5 0 R >> >> /Contents 4 0 R >>"),
            ContentStream("1 j 0 J 2 w 10 10 m 10 30 l S /S sh"),
            Ascii(
                "<< /ShadingType 1 /ColorSpace /DeviceRGB /Domain [0 1 0 1] " +
                "/Matrix [25 0 0 25 50 50] /BBox [50 50 75 75] /Function 6 0 R >>"),
            Stream(
                $"<< /FunctionType 0 /Domain [0 1 0 1] /Range [0 1 0 1 0 1] " +
                $"/Size [2 2] /BitsPerSample 8 /Decode [0 1 0 1 0 1] " +
                $"/Length {sampledRgb.Length} >>",
                sampledRgb)
        };
        return BuildClassic(objects, infoObject: null);
    }

    private static byte[] SampledComponent(byte[] samples) =>
        Stream(
            $"<< /FunctionType 0 /Domain [0 1 0 1] /Range [0 1] " +
            $"/Size [2 2] /BitsPerSample 8 /Decode [0 1] " +
            $"/Length {samples.Length} >>",
            samples);

    private static byte[] ContentStream(string source)
    {
        byte[] bytes = Ascii(source);
        return Stream($"<< /Length {bytes.Length} >>", bytes);
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

    public static byte[] CreateSimpleFontFixture()
    {
        byte[] content = Ascii("BT /F1 20 Tf 72 700 Td <41424320> Tj ET");
        var objects = new[]
        {
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Ascii(
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 500 800] " +
                "/Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>"),
            Stream($"<< /Length {content.Length} >>", content),
            Ascii(
                "<< /Type /Font /Subtype /Type1 /BaseFont /ABCDEF+FixtureSerif " +
                "/Encoding << /BaseEncoding /WinAnsiEncoding " +
                "/Differences [65 /fi /uni20AC /u1F600] >> " +
                "/FirstChar 32 /LastChar 67 /Widths [" +
                string.Join(" ", Enumerable.Range(32, 36).Select(code => code switch
                {
                    32 => "250",
                    65 => "500",
                    66 => "600",
                    67 => "700",
                    _ => "0"
                })) +
                "] >>")
        };
        return BuildClassic(objects, infoObject: null);
    }

    public static byte[] CreateBase14MetricsFixture(
        string baseFont,
        string characterCodes)
    {
        byte[] content = Ascii(
            $"BT /F1 48 Tf 20 80 Td <{characterCodes}> Tj ET");
        string encoding =
            baseFont is "Symbol" or "ZapfDingbats"
                ? ""
                : " /Encoding /WinAnsiEncoding";
        var objects = new[]
        {
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Ascii(
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 220 120] " +
                "/Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>"),
            Stream($"<< /Length {content.Length} >>", content),
            Ascii(
                $"<< /Type /Font /Subtype /Type1 /BaseFont /{baseFont}" +
                $"{encoding} >>")
        };
        return BuildClassic(objects, infoObject: null);
    }

    public static byte[] CreateType0IdentityFixture(bool vertical)
    {
        string encoding = vertical ? "Identity-V" : "Identity-H";
        byte[] content = Ascii("BT /F0 20 Tf 100 650 Td <000100020003> Tj ET");
        byte[] toUnicode = Ascii(
            "/CIDInit /ProcSet findresource begin\n" +
            "12 dict begin begincmap\n" +
            "1 begincodespacerange <0000> <FFFF> endcodespacerange\n" +
            "1 beginbfchar <0001> <0041> endbfchar\n" +
            "1 beginbfrange <0002> <0003> <0062> endbfrange\n" +
            "endcmap end end");
        string metrics = vertical
            ? "/DW 1000 /W [1 [500 600 700]] /DW2 [880 -1000] " +
              "/W2 [1 [ -900 250 880 -1000 300 880 -1100 350 880 ]]"
            : "/DW 1000 /W [1 [500 600] 3 3 700]";
        var objects = new[]
        {
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Ascii(
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 500 800] " +
                "/Resources << /Font << /F0 5 0 R >> >> /Contents 4 0 R >>"),
            Stream($"<< /Length {content.Length} >>", content),
            Ascii(
                $"<< /Type /Font /Subtype /Type0 /BaseFont /FixtureCID /Encoding /{encoding} " +
                "/DescendantFonts [6 0 R] /ToUnicode 7 0 R >>"),
            Ascii(
                "<< /Type /Font /Subtype /CIDFontType2 /BaseFont /FixtureCID " +
                "/CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> " +
                $"/CIDToGIDMap /Identity {metrics} >>"),
            Stream($"<< /Length {toUnicode.Length} >>", toUnicode)
        };
        return BuildClassic(objects, infoObject: null);
    }

    public static byte[] CreateCustomCMapFixture()
    {
        byte[] content = Ascii("BT /F0 16 Tf 60 720 Td <202122> Tj ET");
        byte[] encoding = Ascii(
            "/CIDInit /ProcSet findresource begin\n" +
            "12 dict begin begincmap\n" +
            "/CMapName /Fixture-H def /WMode 0 def\n" +
            "1 begincodespacerange <01> <7F> endcodespacerange\n" +
            "1 begincidrange <20> <22> 100 endcidrange\n" +
            "endcmap end end");
        byte[] toUnicode = Ascii(
            "1 begincodespacerange <01> <7F> endcodespacerange\n" +
            "1 beginbfrange <20> <22> <0041> endbfrange");
        var objects = new[]
        {
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Ascii(
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 500 800] " +
                "/Resources << /Font << /F0 5 0 R >> >> /Contents 4 0 R >>"),
            Stream($"<< /Length {content.Length} >>", content),
            Ascii(
                "<< /Type /Font /Subtype /Type0 /BaseFont /CustomCID /Encoding 7 0 R " +
                "/DescendantFonts [6 0 R] /ToUnicode 8 0 R >>"),
            Ascii(
                "<< /Type /Font /Subtype /CIDFontType0 /BaseFont /CustomCID " +
                "/CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> " +
                "/DW 1000 /W [100 102 750] >>"),
            Stream(
                $"<< /Length {encoding.Length} /CMapName /Fixture-H >>",
                encoding),
            Stream($"<< /Length {toUnicode.Length} >>", toUnicode)
        };
        return BuildClassic(objects, infoObject: null);
    }

    public static byte[] CreateType3Fixture()
    {
        byte[] content = Ascii("BT /F3 10 Tf 50 600 Td <4142> Tj ET");
        byte[] glyph = Ascii("500 0 d0");
        var objects = new[]
        {
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Ascii(
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 500 800] " +
                "/Resources << /Font << /F3 5 0 R >> >> /Contents 4 0 R >>"),
            Stream($"<< /Length {content.Length} >>", content),
            Ascii(
                "<< /Type /Font /Subtype /Type3 /Name /F3 " +
                "/FontBBox [0 -200 1000 800] /FontMatrix [0.002 0 0 0.002 0 0] " +
                "/CharProcs << /A 6 0 R /B 7 0 R >> " +
                "/Encoding << /Differences [65 /A /B] >> " +
                "/FirstChar 65 /LastChar 66 /Widths [500 600] /Resources << >> >>"),
            Stream($"<< /Length {glyph.Length} >>", glyph),
            Stream($"<< /Length {glyph.Length} >>", glyph)
        };
        return BuildClassic(objects, infoObject: null);
    }

    public static byte[] CreateEmbeddedType1EncodingFixture()
    {
        byte[] content = Ascii("BT /F1 16 Tf 50 620 Td <41> Tj ET");
        byte[] fontProgram = Ascii(
            "%!PS-AdobeFont-1.0: FixtureType1 1.0\n" +
            "/Encoding 256 array\n" +
            "dup 65 /fi put\n" +
            "readonly def\n");
        var objects = new[]
        {
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Ascii(
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 500 800] " +
                "/Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>"),
            Stream($"<< /Length {content.Length} >>", content),
            Ascii(
                "<< /Type /Font /Subtype /Type1 /BaseFont /FixtureType1 " +
                "/FontDescriptor 6 0 R /FirstChar 65 /LastChar 65 /Widths [500] >>"),
            Ascii(
                "<< /Type /FontDescriptor /FontName /FixtureType1 /Flags 4 " +
                "/FontBBox [0 -200 1000 800] /Ascent 800 /Descent -200 " +
                "/CapHeight 700 /StemV 80 /FontFile 7 0 R >>"),
            Stream(
                $"<< /Length {fontProgram.Length} /Length1 {fontProgram.Length} " +
                "/Length2 0 /Length3 0 >>",
                fontProgram)
        };
        return BuildClassic(objects, infoObject: null);
    }

    public static byte[] CreateColumnLayoutFixture()
    {
        byte[] content = Ascii(
            "BT /F1 12 Tf " +
            "1 0 0 1 40 740 Tm (Left one) Tj " +
            "1 0 0 1 300 740 Tm (Right one) Tj " +
            "1 0 0 1 40 700 Tm (Left two) Tj " +
            "1 0 0 1 300 700 Tm (Right two) Tj ET");
        var objects = new[]
        {
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Ascii(
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 800] " +
                "/Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>"),
            Stream($"<< /Length {content.Length} >>", content),
            Ascii(
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica " +
                "/Encoding /WinAnsiEncoding >>")
        };
        return BuildClassic(objects, infoObject: null);
    }

    public static byte[] CreateRightToLeftFixture()
    {
        byte[] content = Ascii(
            "BT /F0 18 Tf " +
            "1 0 0 1 282 700 Tm <0002> Tj " +
            "1 0 0 1 300 700 Tm <0001> Tj ET");
        byte[] toUnicode = Ascii(
            "1 begincodespacerange <0000> <FFFF> endcodespacerange\n" +
            "2 beginbfchar <0001> <05D0> <0002> <05D1> endbfchar");
        var objects = new[]
        {
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Ascii(
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 500 800] " +
                "/Resources << /Font << /F0 5 0 R >> >> /Contents 4 0 R >>"),
            Stream($"<< /Length {content.Length} >>", content),
            Ascii(
                "<< /Type /Font /Subtype /Type0 /BaseFont /HebrewFixture " +
                "/Encoding /Identity-H /DescendantFonts [6 0 R] /ToUnicode 7 0 R >>"),
            Ascii(
                "<< /Type /Font /Subtype /CIDFontType2 /BaseFont /HebrewFixture " +
                "/CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> " +
                "/CIDToGIDMap /Identity /DW 1000 >>"),
            Stream($"<< /Length {toUnicode.Length} >>", toUnicode)
        };
        return BuildClassic(objects, infoObject: null);
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

    public static byte[] CreateWithLargeCompressedContent(
        int decodedBytes = 256 * 1024)
    {
        if (decodedBytes < 64)
            throw new ArgumentOutOfRangeException(nameof(decodedBytes));
        byte[] content = Enumerable.Repeat((byte)' ', decodedBytes).ToArray();
        byte[] operation = Ascii("0 0 10 10 re f");
        operation.CopyTo(content, content.Length - operation.Length);
        byte[] compressed = Compress(content);
        return BuildClassic(
            new[]
            {
                Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
                Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
                Ascii(
                    "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] " +
                    "/Resources << >> /Contents 4 0 R >>"),
                Stream(
                    $"<< /Length {compressed.Length} /Filter /FlateDecode >>",
                    compressed)
            },
            infoObject: null);
    }

    public static byte[] CreateWithoutEndOfFileMarker()
    {
        byte[] result = Create(compressContent: false);
        int marker = result.AsSpan().LastIndexOf("%%EOF"u8);
        return result.AsSpan(0, marker).ToArray();
    }

    public static byte[] CreateWithFilteredContent(
        string filter,
        byte[] encodedContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filter);
        ArgumentNullException.ThrowIfNull(encodedContent);
        return BuildClassic(
            new[]
            {
                Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
                Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
                Ascii(
                    "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] " +
                    "/Resources << >> /Contents 4 0 R >>"),
                Stream(
                    $"<< /Length {encodedContent.Length} /Filter /{filter} >>",
                    encodedContent)
            },
            infoObject: null);
    }

    public static byte[] CreateWithContent(
        string content,
        string mediaBox = "0 0 100 100")
    {
        byte[] bytes = Ascii(content);
        return BuildClassic(
            new[]
            {
                Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
                Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
                Ascii(
                    $"<< /Type /Page /Parent 2 0 R /MediaBox [{mediaBox}] " +
                    "/Resources << >> /Contents 4 0 R >>"),
                Stream($"<< /Length {bytes.Length} >>", bytes)
            },
            infoObject: null);
    }

    public static byte[] CreateWithContentParts(params string[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Length == 0)
            throw new ArgumentException("At least one content part is required.", nameof(parts));
        string references = string.Join(
            " ",
            Enumerable.Range(4, parts.Length).Select(index => $"{index} 0 R"));
        var objects = new List<byte[]>
        {
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Ascii(
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] " +
                $"/Resources << >> /Contents [{references}] >>")
        };
        foreach (string part in parts)
        {
            byte[] bytes = Ascii(part);
            objects.Add(Stream($"<< /Length {bytes.Length} >>", bytes));
        }

        return BuildClassic(objects, infoObject: null);
    }

    public static byte[] CreateWithRepeatedImages(bool distinct)
    {
        byte[] content = Ascii(distinct
            ? "q /I1 Do Q q /I2 Do Q"
            : "q /I1 Do Q q /I1 Do Q");
        byte[] first = { 255, 0, 0 };
        byte[] second = { 0, 0, 255 };
        var objects = new List<byte[]>
        {
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Ascii(
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 10 10] " +
                "/Resources << /XObject << /I1 5 0 R" +
                (distinct ? " /I2 6 0 R" : "") +
                " >> >> /Contents 4 0 R >>"),
            Stream($"<< /Length {content.Length} >>", content),
            Stream(
                "<< /Type /XObject /Subtype /Image /Width 1 /Height 1 " +
                "/ColorSpace /DeviceRGB /BitsPerComponent 8 /Length 3 >>",
                first)
        };
        if (distinct)
        {
            objects.Add(Stream(
                "<< /Type /XObject /Subtype /Image /Width 1 /Height 1 " +
                "/ColorSpace /DeviceRGB /BitsPerComponent 8 /Length 3 >>",
                second));
        }

        return BuildClassic(objects, infoObject: null);
    }

    private static byte[] BuildClassic(
        IReadOnlyList<byte[]> objects,
        int? infoObject = 6)
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
        string info = infoObject.HasValue ? $" /Info {infoObject.Value} 0 R" : "";
        Write(
            output,
            $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R{info} " +
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
