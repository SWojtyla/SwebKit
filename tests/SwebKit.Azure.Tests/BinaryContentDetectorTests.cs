using System.Text;
using SwebKit.Azure.Storage;

namespace SwebKit.Azure.Tests;

/// <summary>
/// Sniffing decides whether a downloaded blob is shown as text. False negatives dump
/// control bytes into the UI; false positives hide readable content.
/// </summary>
public sealed class BinaryContentDetectorTests
{
    // ── Magic headers ──

    [Fact]
    public void IsBinary_PdfHeader_IsBinary()
        => Assert.True(BinaryContentDetector.IsBinary("%PDF-1.7 trailing ascii"u8));

    [Fact]
    public void IsBinary_ZipHeader_IsBinary()
        => Assert.True(BinaryContentDetector.IsBinary([0x50, 0x4B, 0x03, 0x04, .. "hello"u8]));

    [Fact]
    public void IsBinary_PngHeader_IsBinary()
        => Assert.True(BinaryContentDetector.IsBinary([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A]));

    [Fact]
    public void IsBinary_JpegHeader_IsBinary()
        => Assert.True(BinaryContentDetector.IsBinary([0xFF, 0xD8, 0xFF, 0xE0]));

    [Fact]
    public void IsBinary_GifHeader_IsBinary()
        => Assert.True(BinaryContentDetector.IsBinary("GIF89a"u8));

    [Fact]
    public void IsBinary_GzipHeader_IsBinary()
        => Assert.True(BinaryContentDetector.IsBinary([0x1F, 0x8B, 0x08]));

    [Fact]
    public void IsBinary_PortableExecutableHeader_IsBinary()
        => Assert.True(BinaryContentDetector.IsBinary("MZ"u8));

    [Fact]
    public void IsBinary_HeaderShorterThanTheSignature_DoesNotMatch()
    {
        // A one-byte sample must not be mistaken for the two-byte gzip/PE signatures.
        Assert.False(BinaryContentDetector.IsBinary([0x4D]));
    }

    [Fact]
    public void IsBinary_SignatureNotAtOffsetZero_DoesNotMatch()
    {
        // The detector only looks at the start of the sample; "%PDF" further in is just text.
        Assert.False(BinaryContentDetector.IsBinary("see %PDF-1.7 mentioned here"u8));
    }

    // ── Printable-ratio heuristic ──

    [Fact]
    public void IsBinary_PlainAsciiText_IsNotBinary()
        => Assert.False(BinaryContentDetector.IsBinary("the quick brown fox jumps over the lazy dog"u8));

    [Fact]
    public void IsBinary_WhitespaceControlCharacters_AreTreatedAsPrintable()
        => Assert.False(BinaryContentDetector.IsBinary("line one\r\n\tindented\n"u8));

    [Fact]
    public void IsBinary_EmptySample_IsNotBinary()
        => Assert.False(BinaryContentDetector.IsBinary(ReadOnlySpan<byte>.Empty));

    [Fact]
    public void IsBinary_AtOrBelowTheFivePercentThreshold_IsNotBinary()
    {
        // 20 bytes, 1 control byte -> 1 is not greater than 20 * 0.05.
        var sample = Encoding.ASCII.GetBytes(new string('a', 19));
        Assert.False(BinaryContentDetector.IsBinary([.. sample, 0x00]));
    }

    [Fact]
    public void IsBinary_AboveTheFivePercentThreshold_IsBinary()
    {
        // 20 bytes, 2 control bytes -> 2 exceeds 20 * 0.05.
        var sample = Encoding.ASCII.GetBytes(new string('a', 18));
        Assert.True(BinaryContentDetector.IsBinary([.. sample, 0x00, 0x01]));
    }

    [Fact]
    public void IsBinary_HighBytesAloneAreNotCountedAsNonPrintable()
    {
        // UTF-8 continuation bytes (>= 0x80) must not make valid UTF-8 text look binary.
        Assert.False(BinaryContentDetector.IsBinary(Encoding.UTF8.GetBytes("héllo wörld, ça va très bien")));
    }
}
