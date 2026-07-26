using CoreJ2K;
using CoreJ2K.Util;
using JBig2Decoder.NETStandard;
using Poppler.Color;
using Poppler.Core;
using Poppler.Core.Filters;
using StbImageSharp;

namespace Poppler.Images;

internal static class PdfImageDecoder
{
    public static PdfImage Decode(
        string resourceName,
        PdfStream stream,
        PdfDictionary? resources,
        PdfDocumentCore document,
        PdfColor maskColor,
        int depth = 0)
    {
        if (depth > document.Options.MaximumXObjectDepth)
            throw new PdfLimitException("Image-mask nesting exceeds the configured limit.");

        PdfDictionary dictionary = stream.Dictionary;
        int declaredWidth = dictionary.GetValueOrNull("Width").AsInteger(document) ?? 0;
        int declaredHeight = dictionary.GetValueOrNull("Height").AsInteger(document) ?? 0;
        ValidateDimensions(declaredWidth, declaredHeight, document.Options);
        bool imageMask = dictionary.GetValueOrNull("ImageMask")?.Resolve(document)
            is PdfBoolean { Value: true };
        int bits = imageMask
            ? 1
            : dictionary.GetValueOrNull("BitsPerComponent").AsInteger(document) ?? 0;
        if (bits is < 1 or > 16)
            throw new PdfUnsupportedFeatureException($"image bit depth {bits}");

        PdfColorSpaceDefinition? colorSpace = imageMask
            ? PdfColorSpaceDefinition.DeviceGray
            : PdfColorSpaceDefinition.Parse(
                dictionary.GetValueOrNull("ColorSpace"),
                resources,
                document);
        PdfFilterPipeline.ImageSource source = PdfFilterPipeline.DecodeImageSource(
            stream,
            document,
            document.Options);
        DecodedSamples samples = source.TerminalFilter switch
        {
            "DCTDecode" or "DCT" => DecodeJpeg(source.Bytes),
            "JPXDecode" => DecodeJpeg2000(source.Bytes),
            "JBIG2Decode" => DecodeJbig2(
                source.Bytes,
                source.Parameters,
                document),
            "CCITTFaxDecode" or "CCF" => DecodeCcitt(
                source.Bytes,
                source.Parameters,
                declaredWidth,
                declaredHeight,
                document),
            _ => DecodePacked(
                source.Bytes,
                declaredWidth,
                declaredHeight,
                colorSpace?.Components ?? (imageMask ? 1 : 0),
                bits)
        };

        if (samples.Width != declaredWidth || samples.Height != declaredHeight)
        {
            throw new PdfFormatException(
                $"Image header is {samples.Width}x{samples.Height}, but the PDF dictionary " +
                $"declares {declaredWidth}x{declaredHeight}.");
        }
        ValidateDimensions(samples.Width, samples.Height, document.Options);

        bool interpolate = dictionary.GetValueOrNull("Interpolate")?.Resolve(document)
            is PdfBoolean { Value: true };
        byte[] pixels;
        PdfPixelFormat format;
        string colorSpaceName;
        if (samples.DirectRgb)
        {
            (pixels, format) = ConvertDirect(samples);
            colorSpaceName = colorSpace?.Name ??
                             (samples.Components == 1 ? "DeviceGray" : "Embedded");
        }
        else if (imageMask)
        {
            pixels = ConvertImageMask(samples, dictionary, document, maskColor);
            format = PdfPixelFormat.Rgba32;
            colorSpaceName = "ImageMask";
        }
        else
        {
            colorSpace ??= InferColorSpace(samples.Components);
            if (colorSpace is null || colorSpace.Components != samples.Components)
            {
                throw new PdfFormatException(
                    "Image sample count does not match its PDF color space.");
            }

            pixels = ConvertColorSamples(
                samples,
                dictionary,
                colorSpace,
                document,
                out format);
            colorSpaceName = colorSpace.Name;
        }

        byte[]? alpha = ReadAlpha(
            dictionary,
            resources,
            document,
            samples,
            maskColor,
            depth);
        if (alpha is not null)
        {
            pixels = AddAlpha(pixels, format, alpha, samples.Width, samples.Height);
            format = PdfPixelFormat.Rgba32;
        }

        return new PdfImage(
            resourceName,
            samples.Width,
            samples.Height,
            format,
            colorSpaceName,
            bits,
            source.Compression,
            interpolate,
            pixels);
    }

    private static DecodedSamples DecodeJpeg(byte[] data)
    {
        try
        {
            ImageResult result = ImageResult.FromMemory(
                data,
                ColorComponents.RedGreenBlue);
            return DecodedSamples.Direct(
                result.Width,
                result.Height,
                components: 3,
                result.Data);
        }
        catch (InvalidOperationException exception)
        {
            throw new PdfFormatException("Invalid DCT/JPEG image.", exception);
        }
    }

    private static DecodedSamples DecodeJpeg2000(byte[] data)
    {
        try
        {
            using InterleavedImage image = J2kImage.FromBytes(data);
            int components = image.NumberOfComponents;
            if (components is < 1 or > 4)
            {
                throw new PdfUnsupportedFeatureException(
                    $"JPEG 2000 image with {components} components");
            }

            int pixels = checked(image.Width * image.Height);
            var result = new byte[checked(pixels * components)];
            for (int component = 0; component < components; component++)
            {
                int[] values = image.GetComponent(component);
                int bitDepth = image.GetBitDepth(component);
                double maximum = bitDepth >= 31
                    ? int.MaxValue
                    : (1L << bitDepth) - 1;
                for (int pixel = 0; pixel < pixels; pixel++)
                {
                    result[pixel * components + component] = (byte)Math.Clamp(
                        (int)Math.Round(values[pixel] * 255d / maximum),
                        0,
                        255);
                }
            }

            return DecodedSamples.Direct(
                image.Width,
                image.Height,
                components,
                result);
        }
        catch (PdfException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or ArgumentException)
        {
            throw new PdfFormatException("Invalid JPX/JPEG 2000 image.", exception);
        }
    }

    private static DecodedSamples DecodeJbig2(
        byte[] data,
        PdfDictionary? parameters,
        PdfDocumentCore document)
    {
        try
        {
            var decoder = new JBIG2StreamDecoder();
            if (parameters?.GetValueOrNull("JBIG2Globals").AsStream(document) is { } globals)
                decoder.SetGlobalData(document.Decode(globals));
            byte[] rgb = decoder.DecodeJBIG2(data, out int width, out int height);
            return DecodedSamples.Direct(width, height, 3, rgb);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IndexOutOfRangeException or ArgumentException)
        {
            throw new PdfFormatException("Invalid JBIG2 image.", exception);
        }
    }

    private static DecodedSamples DecodeCcitt(
        byte[] data,
        PdfDictionary? parameters,
        int width,
        int height,
        PdfDocumentCore document)
    {
        int columns = parameters?.GetValueOrNull("Columns").AsInteger(document) ?? width;
        int rows = parameters?.GetValueOrNull("Rows").AsInteger(document) ?? height;
        int k = parameters?.GetValueOrNull("K").AsInteger(document) ?? 0;
        bool endOfLine = ReadBoolean(parameters, "EndOfLine", document);
        bool byteAlign = ReadBoolean(parameters, "EncodedByteAlign", document);
        bool blackIs1 = ReadBoolean(parameters, "BlackIs1", document);
        if (columns != width || (rows != 0 && rows != height))
        {
            throw new PdfUnsupportedFeatureException(
                "CCITT dimensions differing from the Image XObject dimensions");
        }

        byte[] packed = CcittFaxDecoder.Decode(
            data,
            width,
            height,
            k,
            endOfLine,
            byteAlign,
            blackIs1);
        return DecodePacked(packed, width, height, 1, 1);
    }

    private static DecodedSamples DecodePacked(
        byte[] data,
        int width,
        int height,
        int components,
        int bits)
    {
        if (components < 1)
            throw new PdfFormatException("Image has no color components.");
        long samplesPerRow = checked((long)width * components);
        long bitsPerRow = checked(samplesPerRow * bits);
        int rowBytes = checked((int)((bitsPerRow + 7) / 8));
        int required = checked(rowBytes * height);
        if (data.Length < required)
            throw new PdfFormatException("Image sample stream is truncated.");

        var samples = new ushort[checked(width * height * components)];
        uint maximum = (1u << bits) - 1;
        int destination = 0;
        for (int row = 0; row < height; row++)
        {
            int bit = checked(row * rowBytes * 8);
            for (int index = 0; index < samplesPerRow; index++)
            {
                uint value = 0;
                for (int sampleBit = 0; sampleBit < bits; sampleBit++)
                {
                    value = (value << 1) |
                            (uint)((data[bit >> 3] >> (7 - (bit & 7))) & 1);
                    bit++;
                }

                samples[destination++] = (ushort)value;
            }
        }

        return DecodedSamples.Raw(width, height, components, bits, maximum, samples);
    }

    private static (byte[] Pixels, PdfPixelFormat Format) ConvertDirect(
        DecodedSamples samples)
    {
        if (samples.Components == 1)
            return (samples.Bytes!, PdfPixelFormat.Gray8);
        if (samples.Components == 3)
            return (samples.Bytes!, PdfPixelFormat.Rgb24);

        int count = checked(samples.Width * samples.Height);
        var rgba = new byte[checked(count * 4)];
        if (samples.Components == 2)
        {
            for (int pixel = 0; pixel < count; pixel++)
            {
                byte gray = samples.Bytes![pixel * 2];
                int target = pixel * 4;
                rgba[target] = gray;
                rgba[target + 1] = gray;
                rgba[target + 2] = gray;
                rgba[target + 3] = samples.Bytes[pixel * 2 + 1];
            }
        }
        else
        {
            samples.Bytes!.CopyTo(rgba, 0);
        }

        return (rgba, PdfPixelFormat.Rgba32);
    }

    private static byte[] ConvertImageMask(
        DecodedSamples samples,
        PdfDictionary dictionary,
        PdfDocumentCore document,
        PdfColor maskColor)
    {
        double[] decode = ReadDecode(
            dictionary.GetValueOrNull("Decode"),
            document,
            new[] { 0d, 1d },
            expectedComponents: 1);
        (double red, double green, double blue) = maskColor.ToRgb();
        byte r = UnitByte(red);
        byte g = UnitByte(green);
        byte b = UnitByte(blue);
        int count = checked(samples.Width * samples.Height);
        var rgba = new byte[checked(count * 4)];
        for (int pixel = 0; pixel < count; pixel++)
        {
            double decoded = Interpolate(
                samples.Values![pixel],
                0,
                samples.Maximum,
                decode[0],
                decode[1]);
            int target = pixel * 4;
            rgba[target] = r;
            rgba[target + 1] = g;
            rgba[target + 2] = b;
            rgba[target + 3] = decoded < 0.5 ? (byte)255 : (byte)0;
        }

        return rgba;
    }

    private static byte[] ConvertColorSamples(
        DecodedSamples samples,
        PdfDictionary dictionary,
        PdfColorSpaceDefinition colorSpace,
        PdfDocumentCore document,
        out PdfPixelFormat format)
    {
        double[] decode = ReadDecode(
            dictionary.GetValueOrNull("Decode"),
            document,
            colorSpace.DefaultDecode(),
            colorSpace.Components);
        int count = checked(samples.Width * samples.Height);
        bool grayscale = colorSpace.Kind == PdfColorSpace.DeviceGray;
        format = grayscale ? PdfPixelFormat.Gray8 : PdfPixelFormat.Rgb24;
        var result = new byte[checked(count * (grayscale ? 1 : 3))];
        Span<double> components = colorSpace.Components <= 16
            ? stackalloc double[colorSpace.Components]
            : new double[colorSpace.Components];
        for (int pixel = 0; pixel < count; pixel++)
        {
            int source = pixel * colorSpace.Components;
            for (int component = 0; component < colorSpace.Components; component++)
            {
                components[component] = Interpolate(
                    samples.Values![source + component],
                    0,
                    samples.Maximum,
                    decode[component * 2],
                    decode[component * 2 + 1]);
            }

            PdfColor color = colorSpace.Convert(components);
            (double red, double green, double blue) = color.ToRgb();
            if (grayscale)
            {
                result[pixel] = UnitByte(red);
            }
            else
            {
                int target = pixel * 3;
                result[target] = UnitByte(red);
                result[target + 1] = UnitByte(green);
                result[target + 2] = UnitByte(blue);
            }
        }

        return result;
    }

    private static byte[]? ReadAlpha(
        PdfDictionary dictionary,
        PdfDictionary? resources,
        PdfDocumentCore document,
        DecodedSamples sourceSamples,
        PdfColor maskColor,
        int depth)
    {
        PdfObject? softMaskObject = dictionary.GetValueOrNull("SMask");
        if (softMaskObject?.Resolve(document) is PdfStream softMask)
        {
            PdfImage image = Decode(
                "SMask",
                softMask,
                resources,
                document,
                maskColor,
                depth + 1);
            return ReadMaskPixels(image, sourceSamples.Width, sourceSamples.Height);
        }

        PdfObject? maskObject = dictionary.GetValueOrNull("Mask");
        if (maskObject?.Resolve(document) is PdfStream explicitMask)
        {
            PdfImage image = Decode(
                "Mask",
                explicitMask,
                resources,
                document,
                maskColor,
                depth + 1);
            return ReadMaskPixels(image, sourceSamples.Width, sourceSamples.Height);
        }

        PdfArray? colorKey = maskObject.AsArray(document);
        if (colorKey is null ||
            sourceSamples.Values is null ||
            colorKey.Count < sourceSamples.Components * 2)
        {
            return null;
        }

        var ranges = new int[sourceSamples.Components * 2];
        for (int index = 0; index < ranges.Length; index++)
        {
            ranges[index] = colorKey[index].AsInteger(document) ??
                            throw new PdfFormatException("Invalid color-key mask.");
        }

        int count = checked(sourceSamples.Width * sourceSamples.Height);
        var alpha = Enumerable.Repeat((byte)255, count).ToArray();
        for (int pixel = 0; pixel < count; pixel++)
        {
            bool transparent = true;
            for (int component = 0; component < sourceSamples.Components; component++)
            {
                int sample = sourceSamples.Values[pixel * sourceSamples.Components + component];
                if (sample < ranges[component * 2] ||
                    sample > ranges[component * 2 + 1])
                {
                    transparent = false;
                    break;
                }
            }

            if (transparent)
                alpha[pixel] = 0;
        }

        return alpha;
    }

    private static byte[] ReadMaskPixels(PdfImage image, int width, int height)
    {
        int count = checked(width * height);
        var alpha = new byte[count];
        ReadOnlySpan<byte> data = image.Data.Span;
        int components = image.Format switch
        {
            PdfPixelFormat.Gray8 => 1,
            PdfPixelFormat.Rgb24 => 3,
            PdfPixelFormat.Rgba32 => 4,
            _ => 1
        };
        for (int y = 0; y < height; y++)
        {
            int sourceY = Math.Min(image.Height - 1, y * image.Height / height);
            for (int x = 0; x < width; x++)
            {
                int sourceX = Math.Min(image.Width - 1, x * image.Width / width);
                int source = (sourceY * image.Width + sourceX) * components;
                alpha[y * width + x] = image.Format == PdfPixelFormat.Rgba32
                    ? data[source + 3]
                    : data[source];
            }
        }

        return alpha;
    }

    private static byte[] AddAlpha(
        byte[] pixels,
        PdfPixelFormat format,
        ReadOnlySpan<byte> alpha,
        int width,
        int height)
    {
        int count = checked(width * height);
        if (alpha.Length != count)
            throw new PdfFormatException("Mask dimensions are invalid.");
        var result = new byte[checked(count * 4)];
        int sourceComponents = format switch
        {
            PdfPixelFormat.Gray8 => 1,
            PdfPixelFormat.Rgb24 => 3,
            PdfPixelFormat.Rgba32 => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
        for (int pixel = 0; pixel < count; pixel++)
        {
            int source = pixel * sourceComponents;
            int target = pixel * 4;
            if (format == PdfPixelFormat.Gray8)
            {
                result[target] = pixels[source];
                result[target + 1] = pixels[source];
                result[target + 2] = pixels[source];
            }
            else
            {
                result[target] = pixels[source];
                result[target + 1] = pixels[source + 1];
                result[target + 2] = pixels[source + 2];
            }

            byte existingAlpha = format == PdfPixelFormat.Rgba32
                ? pixels[source + 3]
                : (byte)255;
            result[target + 3] = (byte)((existingAlpha * alpha[pixel] + 127) / 255);
        }

        return result;
    }

    private static double[] ReadDecode(
        PdfObject? value,
        PdfDocumentCore document,
        double[] fallback,
        int expectedComponents)
    {
        PdfArray? array = value.AsArray(document);
        if (array is null)
            return fallback;
        if (array.Count < expectedComponents * 2)
            throw new PdfFormatException("Image /Decode array has too few entries.");
        var result = new double[expectedComponents * 2];
        for (int index = 0; index < result.Length; index++)
        {
            double? number = array[index].AsNumber(document);
            if (!number.HasValue || !double.IsFinite(number.Value))
                throw new PdfFormatException("Image /Decode array is invalid.");
            result[index] = number.Value;
        }

        return result;
    }

    private static PdfColorSpaceDefinition? InferColorSpace(int components) => components switch
    {
        1 => PdfColorSpaceDefinition.DeviceGray,
        3 => PdfColorSpaceDefinition.DeviceRgb,
        4 => PdfColorSpaceDefinition.DeviceCmyk,
        _ => null
    };

    private static bool ReadBoolean(
        PdfDictionary? dictionary,
        string name,
        PdfDocumentCore document) =>
        dictionary?.GetValueOrNull(name)?.Resolve(document)
            is PdfBoolean { Value: true };

    private static double Interpolate(
        double value,
        double sourceMinimum,
        double sourceMaximum,
        double targetMinimum,
        double targetMaximum) =>
        sourceMaximum == sourceMinimum
            ? targetMinimum
            : targetMinimum +
              (value - sourceMinimum) /
              (sourceMaximum - sourceMinimum) *
              (targetMaximum - targetMinimum);

    private static byte UnitByte(double value) =>
        (byte)Math.Clamp(
            (int)Math.Round((double.IsFinite(value) ? value : 0) * 255),
            0,
            255);

    private static void ValidateDimensions(
        int width,
        int height,
        PdfReadOptions options)
    {
        if (width < 1 || height < 1)
            throw new PdfFormatException("Image dimensions must be positive.");
        long pixels = checked((long)width * height);
        if (pixels > options.MaximumImagePixels)
            throw new PdfLimitException("Image pixel count exceeds the configured limit.");
    }

    private sealed class DecodedSamples
    {
        private DecodedSamples(
            int width,
            int height,
            int components,
            int bits,
            uint maximum,
            ushort[]? values,
            byte[]? bytes,
            bool directRgb)
        {
            Width = width;
            Height = height;
            Components = components;
            Bits = bits;
            Maximum = maximum;
            Values = values;
            Bytes = bytes;
            DirectRgb = directRgb;
        }

        public int Width { get; }
        public int Height { get; }
        public int Components { get; }
        public int Bits { get; }
        public uint Maximum { get; }
        public ushort[]? Values { get; }
        public byte[]? Bytes { get; }
        public bool DirectRgb { get; }

        public static DecodedSamples Raw(
            int width,
            int height,
            int components,
            int bits,
            uint maximum,
            ushort[] values) =>
            new(width, height, components, bits, maximum, values, null, false);

        public static DecodedSamples Direct(
            int width,
            int height,
            int components,
            byte[] bytes)
        {
            if (width < 1 ||
                height < 1 ||
                components < 1 ||
                bytes.Length != checked(width * height * components))
            {
                throw new PdfFormatException("Decoded image buffer has invalid dimensions.");
            }

            return new(width, height, components, 8, 255, null, bytes, true);
        }
    }
}
