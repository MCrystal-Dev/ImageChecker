using ImageMagick;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace ImageChecker;

internal sealed record ImageDate(DateOnly Day, string Source)
{
    internal string Prefix => Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

internal sealed record ImageMetadata(ImageDate Date, string Text)
{
    private static readonly XNamespace Exif = "http://ns.adobe.com/exif/1.0/";
    private static readonly XNamespace Xmp = "http://ns.adobe.com/xap/1.0/";
    private static readonly XNamespace Photoshop = "http://ns.adobe.com/photoshop/1.0/";
    private static readonly XNamespace MicrosoftPhoto = "http://ns.microsoft.com/photo/1.0/";

    internal static ImageMetadata Read(string path, bool includeFields = true)
    {
        var file = new FileInfo(path);
        var fields = new List<string>();
        ImageDate? detected = null;
        if (includeFields)
        {
            fields.Add(path);
            fields.Add($"{file.Length:N0} bytes");
            fields.Add($"File created locally: {file.CreationTime:G}");
            fields.Add($"File last modified: {file.LastWriteTime:G}");
        }
        void Consider(string? value, string source)
        {
            if (detected == null && ParseDay(value) is {} day) detected = new(day, source);
        }
        using var image = new MagickImage();
        try
        {
            var settings = new MagickReadSettings { Format = Operations.FormatOf(path), FrameCount = 1 };
            using var input = File.OpenRead(path);
            // PNG text and WebP metadata chunks can follow the pixels, beyond a header-only read.
            if (settings.Format == MagickFormat.Jpeg) image.Ping(input, settings);
            else image.Read(input, settings);
            var exif = image.GetExifProfile();
            XDocument? packet = null;
            if (image.GetXmpProfile() is {} profile)
            {
                try
                {
                    using var stream = new MemoryStream(profile.ToByteArray());
                    using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = 8_000_000 });
                    packet = XDocument.Load(reader);
                }
                catch (XmlException) { fields.Add("XMP packet could not be read."); }
            }
            string? XmpValue(XName tag) => packet?.Descendants().Attributes(tag).FirstOrDefault()?.Value ?? packet?.Descendants(tag).FirstOrDefault()?.Value;
            Consider(exif?.GetValue(ExifTag.DateTimeOriginal)?.Value, "Date taken (EXIF)");
            Consider(XmpValue(Exif + "DateTimeOriginal"), "Date taken (XMP)");
            Consider(exif?.GetValue(ExifTag.DateTimeDigitized)?.Value, "Date digitized (EXIF)");
            Consider(XmpValue(Xmp + "CreateDate"), "Image creation date (XMP)");
            Consider(XmpValue(Photoshop + "DateCreated"), "Content creation date (XMP)");
            Consider(image.GetAttribute("Creation Time"), "Image creation time (embedded)");
            Consider(image.GetAttribute("create-date"), "Image creation date (embedded)");
            Consider(XmpValue(MicrosoftPhoto + "DateAcquired"), "Date acquired (XMP)");
            if (includeFields)
            {
                fields.Add($"{image.Format} · {image.Width} × {image.Height} px · {image.Depth} bit · {image.ColorSpace}");
                foreach (var tag in exif?.Values ?? [])
                {
                    var value = tag.GetValue();
                    string? display = value is Array a ? (a.Length > 16 ? $"[{a.Length} values]" : string.Join(", ", a.Cast<object>())) : value?.ToString();
                    fields.Add($"EXIF {tag.Tag}: {display}");
                }
                if (packet?.Root is {} root)
                {
                    foreach (var attr in root.DescendantsAndSelf().Attributes().Where(a => !a.IsNamespaceDeclaration))
                        fields.Add($"XMP {attr.Name.LocalName}: {attr.Value}");
                    foreach (var element in root.Descendants().Where(e => !e.HasElements && e.Value.Length > 0))
                        fields.Add($"XMP {element.Name.LocalName}: {element.Value}");
                }
                foreach (var name in image.AttributeNames.Where(n => !n.StartsWith("date:", StringComparison.OrdinalIgnoreCase)))
                    fields.Add($"{name}: {image.GetAttribute(name)}");
            }
        }
        catch (MagickException ex) { fields.Add($"Embedded metadata could not be read. {ex.Message}"); }
        if (includeFields || detected == null)
        {
            if (AcquisitionDate.Read(path) is {} acquired)
            {
                fields.Add($"Windows date acquired: {acquired:G}");
                detected ??= new(DateOnly.FromDateTime(acquired), "Windows date acquired");
            }
        }
        detected ??= new(DateOnly.FromDateTime(file.CreationTime), "File created locally (estimate)");
        if (includeFields) fields.Insert(0, $"Detected date: {detected.Prefix} · {detected.Source}");
        return new(detected, string.Join(Environment.NewLine, fields.Select(line => line.Length <= 8192 ? line : line[..8192] + "…")));
    }

    private static DateOnly? ParseDay(string? raw)
    {
        string? value = raw?.Trim().TrimEnd('\0');
        if (value == null || value.Length < 10) return null;
        if ((value.Length == 10 || value[10] is 'T' or ' ') && DateOnly.TryParseExact(value[..10],
            ["yyyy:MM:dd", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) return day;
        return DateTimeOffset.TryParseExact(value, "r", CultureInfo.InvariantCulture, DateTimeStyles.None, out var rfc)
            ? DateOnly.FromDateTime(rfc.DateTime) : null;
    }
}
