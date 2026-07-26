using System.Globalization;

namespace Poppler.DocumentModel;

internal static class PdfDateParser
{
    public static DateTimeOffset? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        ReadOnlySpan<char> text = value.AsSpan().Trim();
        if (text.StartsWith("D:", StringComparison.Ordinal))
            text = text[2..];
        if (text.Length < 4 || !TryPart(text, 0, 4, out int year))
            return null;

        int month = PartOrDefault(text, 4, 2, 1);
        int day = PartOrDefault(text, 6, 2, 1);
        int hour = PartOrDefault(text, 8, 2, 0);
        int minute = PartOrDefault(text, 10, 2, 0);
        int second = PartOrDefault(text, 12, 2, 0);
        try
        {
            var date = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
            if (text.Length <= 14 || text[14] == 'Z')
                return new DateTimeOffset(date, TimeSpan.Zero);

            char sign = text[14];
            if (sign is not ('+' or '-'))
                return new DateTimeOffset(date, TimeSpan.Zero);
            int offsetHours = PartOrDefault(text, 15, 2, 0);
            int minuteStart = text.Length > 17 && text[17] == '\'' ? 18 : 17;
            int offsetMinutes = PartOrDefault(text, minuteStart, 2, 0);
            var offset = new TimeSpan(offsetHours, offsetMinutes, 0);
            if (sign == '-')
                offset = -offset;
            return new DateTimeOffset(date, offset);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static int PartOrDefault(ReadOnlySpan<char> text, int start, int length, int defaultValue) =>
        TryPart(text, start, length, out int value) ? value : defaultValue;

    private static bool TryPart(ReadOnlySpan<char> text, int start, int length, out int value)
    {
        value = 0;
        return start >= 0 &&
               length >= 0 &&
               start <= text.Length - length &&
               int.TryParse(
                   text.Slice(start, length),
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out value);
    }
}
