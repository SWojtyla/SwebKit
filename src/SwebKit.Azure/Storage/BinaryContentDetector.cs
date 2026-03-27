namespace SwebKit.Azure.Storage;

public static class BinaryContentDetector
{
    private static readonly byte[][] KnownBinaryHeaders =
    [
        "%PDF"u8.ToArray(),
        [0x50, 0x4B, 0x03, 0x04], // ZIP
        [0x89, 0x50, 0x4E, 0x47], // PNG
        [0xFF, 0xD8, 0xFF],       // JPEG
        "GIF8"u8.ToArray(),
        [0x1F, 0x8B],             // gzip
        [0x4D, 0x5A],             // PE exe
    ];

    public static bool IsBinary(ReadOnlySpan<byte> sample)
    {
        foreach (var header in KnownBinaryHeaders)
            if (sample.Length >= header.Length && sample[..header.Length].SequenceEqual(header))
                return true;

        int nonPrintable = 0;
        foreach (var b in sample)
            if (b < 32 && b != (byte)'\t' && b != (byte)'\r' && b != (byte)'\n')
                nonPrintable++;
        return sample.Length > 0 && nonPrintable > sample.Length * 0.05;
    }
}
