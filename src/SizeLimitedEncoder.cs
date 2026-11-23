using ImageMagick;

namespace ImageChecker;

internal static class SizeLimitedEncoder
{
    // Keep the original pixel dimensions when a reasonable lossy quality fits.
    private const int MinimumQuality = 60;

    internal static string Write(MagickImage original, string destination, MagickFormat format, long limit, CancellationToken cancellation)
    {
        bool png = format == MagickFormat.Png;
        bool webp = format == MagickFormat.WebP;
        string trial = destination + ".trial";
        string details = "";

        bool Fits(IMagickImage<byte> image, int quality)
        {
            cancellation.ThrowIfCancellationRequested();
            image.Quality = (uint)quality;
            image.Write(trial, format);
            long length = new FileInfo(trial).Length;
            if (length >= limit) return false;
            File.Move(trial, destination, overwrite: true);
            details = $"{image.Width} × {image.Height} px · {length:N0} bytes";
            return true;
        }

        void RefineQuality(IMagickImage<byte> image, int highest)
        {
            int low = MinimumQuality + 1, high = highest;
            while (low <= high)
            {
                int middle = low + (high - low) / 2;
                if (Fits(image, middle)) low = middle + 1;
                else high = middle - 1;
            }
        }

        try
        {
            // PNG's quality parameter controls compression and filtering, not pixel fidelity.
            int bestQuality = png ? 95 : 100;
            if (webp) original.Settings.SetDefine(format, "lossless", true);
            if (Fits(original, bestQuality)) return details;
            original.Strip();
            if (Fits(original, bestQuality)) return details;
            if (webp)
            {
                original.Settings.SetDefine(format, "lossless", false);
                if (Fits(original, 100)) return details;
            }
            if (!png && Fits(original, MinimumQuality))
            {
                RefineQuality(original, 99);
                return details;
            }

            // Every resize starts from the source. Keep the last successful encoding while probing larger dimensions.
            int longest = checked((int)Math.Max(original.Width, original.Height));
            int low = 1, high = longest - 1, bestEdge = 0;
            while (low <= high)
            {
                int edge = low + (high - low) / 2;
                using var resized = original.Clone();
                resized.Resize((uint)edge, (uint)edge);
                if (Fits(resized, png ? bestQuality : MinimumQuality))
                {
                    bestEdge = edge;
                    low = edge + 1;
                }
                else high = edge - 1;
            }
            if (bestEdge == 0) throw new IOException("Size limit is too small for this format.");
            if (!png)
            {
                using var resized = original.Clone();
                resized.Resize((uint)bestEdge, (uint)bestEdge);
                RefineQuality(resized, 100);
            }
            return details;
        }
        finally { File.Delete(trial); }
    }
}
