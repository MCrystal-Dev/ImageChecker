using ImageMagick;

namespace ImageChecker;

internal static class ImageResources
{
    internal const int MaxDimension = 20_000;
    private const ulong MiB = 1024 * 1024;

    internal static void Configure()
    {
        ResourceLimits.Width = Math.Min(ResourceLimits.Width, MaxDimension);
        ResourceLimits.Height = Math.Min(ResourceLimits.Height, MaxDimension);
        ResourceLimits.Memory = Math.Min(ResourceLimits.Memory, 512 * MiB);
        ResourceLimits.Disk = Math.Min(ResourceLimits.Disk, 2048 * MiB);
        ResourceLimits.MaxProfileSize = Math.Min(ResourceLimits.MaxProfileSize, 16 * MiB);
    }
}
