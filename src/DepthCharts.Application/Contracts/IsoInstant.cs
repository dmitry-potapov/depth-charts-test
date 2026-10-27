using System.Globalization;

namespace DepthCharts.Application.Contracts;

// ISO-8601 only; a lenient TryParse would read "10/11/2026" as a US date. Offset (K) is optional, defaulting to UTC.
public static class IsoInstant
{
    private static readonly string[] Formats =
    [
        "yyyy-MM-dd",
        "yyyy-MM-dd'T'HH:mmK",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
    ];

    public static bool TryParse(string s, out DateTimeOffset value) =>
        DateTimeOffset.TryParseExact(s, Formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value);
}
