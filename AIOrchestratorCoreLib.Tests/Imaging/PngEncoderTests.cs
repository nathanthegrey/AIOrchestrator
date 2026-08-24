using System.IO.Compression;
using System.Text;
using AIOrchestratorCoreLib.Imaging;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Imaging;

/// <summary>
/// Pins the ENCODER AGAINST THE FORMAT, not against itself. A hand-written container is the kind of
/// code that produces a file no test complains about and no viewer will open, so nothing here settles
/// for "it returned bytes and did not throw":
///
/// * the chunk walk below is an independent parser — it re-derives every length and offset from the
///   file, so a wrong chunk length is a parse failure rather than a silent pass;
/// * the CRC used for verification is a SECOND implementation, bit-at-a-time, sharing no table with
///   the production one, and it is itself pinned to the published IEND checksum so the two cannot
///   simply agree on being wrong together;
/// * the pixels are decompressed and compared byte for byte against what was fed in, on an image
///   whose every channel of every pixel is a different value — so a B/R swap, a row flip, or a
///   dropped filter byte each fail loudly instead of averaging out on a uniform test image.
/// </summary>
public class PngEncoderTests
{
    const int TEST_WIDTH = 3;
    const int TEST_HEIGHT = 2;

    const int SIGNATURE_LENGTH = 8;
    const int CHUNK_LENGTH_FIELD_LENGTH = 4;
    const int CHUNK_TYPE_FIELD_LENGTH = 4;
    const int CHUNK_CRC_FIELD_LENGTH = 4;
    const int CHUNK_OVERHEAD = CHUNK_LENGTH_FIELD_LENGTH + CHUNK_TYPE_FIELD_LENGTH + CHUNK_CRC_FIELD_LENGTH;

    const int IMAGE_HEADER_DATA_LENGTH = 13;

    /// <summary>
    /// The expected image, top-down, one array per scanline, R,G,B per pixel. Every one of the
    /// eighteen bytes is distinct: any transposition at all shows up as a mismatch.
    /// </summary>
    static readonly byte[][] EXPECTED_SCANLINES_RGB =
    [
        [10, 20, 30, 40, 50, 60, 70, 80, 90],
        [100, 110, 120, 130, 140, 150, 160, 170, 180],
    ];

    /// <summary>
    /// Junk alpha, on purpose, in the shape a real screen capture delivers it — mostly zero, which is
    /// the value that turns an honest-looking encoder's output invisible in Telegram.
    /// </summary>
    static readonly byte[] SOURCE_ALPHA_PER_PIXEL = [0, 255, 1, 200, 0, 42];

    [Fact]
    public void Encode_Bgra32_BeginsWithThePngSignatureBytes()
    {
        var png = Png_Encoder.Encode_Bgra32(TEST_WIDTH, TEST_HEIGHT, Build_TestImage_Bgra32());

        Assert.Equal(
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A },
            png[..SIGNATURE_LENGTH]);
    }

    [Fact]
    public void Encode_Bgra32_EmitsImageHeaderFirstThenImageDataThenImageEndLast()
    {
        var png = Png_Encoder.Encode_Bgra32(TEST_WIDTH, TEST_HEIGHT, Build_TestImage_Bgra32());

        var chunkTypes = Read_Chunks(png).Select(chunk => chunk.Type).ToArray();

        Assert.Equal("IHDR", chunkTypes.First());
        Assert.Equal("IEND", chunkTypes.Last());
        Assert.Contains("IDAT", chunkTypes);

        // Order, not merely presence: an IDAT before its IHDR is an unreadable file.
        Assert.True(
            Array.IndexOf(chunkTypes, "IDAT") > Array.IndexOf(chunkTypes, "IHDR"),
            $"IDAT must follow IHDR, got [{string.Join(", ", chunkTypes)}]");

        Assert.True(
            Array.LastIndexOf(chunkTypes, "IDAT") < Array.IndexOf(chunkTypes, "IEND"),
            $"IDAT must precede IEND, got [{string.Join(", ", chunkTypes)}]");
    }

    /// <summary>
    /// The walk lands exactly on the end of the file. A chunk length that over- or under-states its
    /// payload leaves a remainder here, which is the failure a viewer would report as "corrupt".
    /// </summary>
    [Fact]
    public void Encode_Bgra32_ChunksAccountForEveryByteOfTheFile()
    {
        var png = Png_Encoder.Encode_Bgra32(TEST_WIDTH, TEST_HEIGHT, Build_TestImage_Bgra32());

        var accountedFor = SIGNATURE_LENGTH + Read_Chunks(png).Sum(chunk => CHUNK_OVERHEAD + chunk.Data.Length);

        Assert.Equal(png.Length, accountedFor);
    }

    [Fact]
    public void Encode_Bgra32_EveryChunkCarriesACrcThatRecomputes()
    {
        var png = Png_Encoder.Encode_Bgra32(TEST_WIDTH, TEST_HEIGHT, Build_TestImage_Bgra32());

        foreach (var chunk in Read_Chunks(png))
        {
            var recomputed = Compute_Crc32_Independently(Encoding.ASCII.GetBytes(chunk.Type), chunk.Data);

            Assert.True(
                recomputed == chunk.DeclaredCrc,
                $"chunk '{chunk.Type}' declares CRC 0x{chunk.DeclaredCrc:X8} but recomputes to 0x{recomputed:X8}");
        }
    }

    /// <summary>
    /// Guards the guard. Two implementations of the same wrong polynomial would agree with each other
    /// happily, so the verification CRC is anchored to a value published in the PNG specification: the
    /// checksum of an empty IEND chunk is always 0xAE426082, in every conforming file ever written.
    /// </summary>
    [Fact]
    public void ComputeCrc32Independently_MatchesThePublishedImageEndChecksum()
    {
        Assert.Equal(0xAE426082u, Compute_Crc32_Independently(Encoding.ASCII.GetBytes("IEND"), []));
    }

    [Fact]
    public void Encode_Bgra32_ImageHeaderDeclaresTheDimensionsBitDepthAndOpaqueTruecolour()
    {
        const int WIDTH = 7;
        const int HEIGHT = 5;

        var png = Png_Encoder.Encode_Bgra32(WIDTH, HEIGHT, new byte[WIDTH * HEIGHT * 4]);

        var header = Read_Chunks(png).Single(chunk => chunk.Type == "IHDR").Data;

        Assert.Equal(IMAGE_HEADER_DATA_LENGTH, header.Length);

        Assert.Equal(WIDTH, Read_BigEndianInt32(header, 0));
        Assert.Equal(HEIGHT, Read_BigEndianInt32(header, 4));

        Assert.Equal(8, header[8]);     // bit depth
        Assert.Equal(2, header[9]);     // colour type 2 = truecolour, NO alpha
        Assert.Equal(0, header[10]);    // compression method: deflate
        Assert.Equal(0, header[11]);    // filter method: adaptive
        Assert.Equal(0, header[12]);    // interlace method: none
    }

    /// <summary>
    /// THE TEST THAT MATTERS. Inflates the IDAT payload and compares it, byte for byte, with the
    /// scanlines that were fed in — filter byte included. A channel-order swap, a row-order flip, an
    /// off-by-one stride or a missing filter byte all land here.
    /// </summary>
    [Fact]
    public void Encode_Bgra32_RoundTripsThePixelsAsTopDownRgbScanlines()
    {
        var png = Png_Encoder.Encode_Bgra32(TEST_WIDTH, TEST_HEIGHT, Build_TestImage_Bgra32());

        var expected = new List<byte>();

        foreach (var scanline in EXPECTED_SCANLINES_RGB)
        {
            expected.Add(0);
            expected.AddRange(scanline);
        }

        Assert.Equal(expected.ToArray(), Inflate_ImageData(png));
    }

    [Fact]
    public void Encode_Bgra32_StartsEveryScanlineWithTheNoneFilterByte()
    {
        var png = Png_Encoder.Encode_Bgra32(TEST_WIDTH, TEST_HEIGHT, Build_TestImage_Bgra32());

        var inflated = Inflate_ImageData(png);
        var filteredStride = 1 + (TEST_WIDTH * 3);

        Assert.Equal(filteredStride * TEST_HEIGHT, inflated.Length);

        for (var row = 0; row < TEST_HEIGHT; row++)
            Assert.Equal(0, inflated[row * filteredStride]);
    }

    /// <summary>
    /// Alpha is not merely "not written into an alpha channel" — it must not influence the output at
    /// all. Two buffers differing ONLY in alpha have to compress to identical image data; anything
    /// else means a stray byte is leaking into the pixels.
    /// </summary>
    [Fact]
    public void Encode_Bgra32_IgnoresTheAlphaChannelEntirely()
    {
        var transparent = Build_TestImage_Bgra32(alphaOverride: 0);
        var opaque = Build_TestImage_Bgra32(alphaOverride: 255);

        var fromTransparent = Png_Encoder.Encode_Bgra32(TEST_WIDTH, TEST_HEIGHT, transparent);
        var fromOpaque = Png_Encoder.Encode_Bgra32(TEST_WIDTH, TEST_HEIGHT, opaque);

        Assert.NotEqual(transparent, opaque);
        Assert.Equal(Inflate_ImageData(fromTransparent), Inflate_ImageData(fromOpaque));
    }

    /// <summary>
    /// A capture API may return a pooled or padded array, so a buffer LONGER than the image is legal
    /// input and the surplus must simply be ignored rather than shifting the rows.
    /// </summary>
    [Fact]
    public void Encode_Bgra32_AcceptsABufferLongerThanTheImageAndIgnoresTheSurplus()
    {
        var padded = new byte[(TEST_WIDTH * TEST_HEIGHT * 4) + 64];

        Build_TestImage_Bgra32().CopyTo(padded, 0);

        var fromPadded = Png_Encoder.Encode_Bgra32(TEST_WIDTH, TEST_HEIGHT, padded);
        var fromExact = Png_Encoder.Encode_Bgra32(TEST_WIDTH, TEST_HEIGHT, Build_TestImage_Bgra32());

        Assert.Equal(Inflate_ImageData(fromExact), Inflate_ImageData(fromPadded));
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(-1, 4)]
    [InlineData(4, 0)]
    [InlineData(4, -1)]
    public void Encode_Bgra32_ThrowsForANonPositiveDimension(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Png_Encoder.Encode_Bgra32(width, height, new byte[1024]));
    }

    /// <summary>
    /// One byte short is still short. The alternative to throwing is reading a torn final scanline out
    /// of whatever follows the buffer and presenting it as an image.
    /// </summary>
    [Fact]
    public void Encode_Bgra32_ThrowsWhenTheBufferIsShorterThanTheImage()
    {
        var oneByteShort = new byte[(TEST_WIDTH * TEST_HEIGHT * 4) - 1];

        Assert.Throws<ArgumentException>(
            () => Png_Encoder.Encode_Bgra32(TEST_WIDTH, TEST_HEIGHT, oneByteShort));
    }

    /// <summary>Builds the test image in the layout a Windows DIB section hands over: B,G,R,A per pixel.</summary>
    /// <param name="alphaOverride">
    /// When given, every pixel gets this alpha instead of the varied junk values — used to prove that
    /// alpha changes nothing about the encoded pixels.
    /// </param>
    static byte[] Build_TestImage_Bgra32(byte? alphaOverride = null)
    {
        var bgra = new byte[TEST_WIDTH * TEST_HEIGHT * 4];

        var targetIndex = 0;
        var pixelIndex = 0;

        foreach (var scanline in EXPECTED_SCANLINES_RGB)
            for (var column = 0; column < TEST_WIDTH; column++)
            {
                var red = scanline[column * 3];
                var green = scanline[(column * 3) + 1];
                var blue = scanline[(column * 3) + 2];

                bgra[targetIndex++] = blue;
                bgra[targetIndex++] = green;
                bgra[targetIndex++] = red;
                bgra[targetIndex++] = alphaOverride ?? SOURCE_ALPHA_PER_PIXEL[pixelIndex];

                pixelIndex++;
            }

        return bgra;
    }

    /// <summary>Concatenates every IDAT payload (the specification permits several) and inflates it.</summary>
    static byte[] Inflate_ImageData(byte[] png)
    {
        var imageData = Read_Chunks(png)
            .Where(chunk => chunk.Type == "IDAT")
            .SelectMany(chunk => chunk.Data)
            .ToArray();

        using var compressed = new MemoryStream(imageData);
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var inflated = new MemoryStream();

        zlib.CopyTo(inflated);

        return inflated.ToArray();
    }

    /// <summary>
    /// An independent chunk walk. It trusts nothing the encoder believes: each length comes out of the
    /// file itself and each offset is derived from the previous chunk, so a length field that does not
    /// match its payload falls off the end of the array here rather than passing quietly.
    /// </summary>
    static IReadOnlyList<PngChunk> Read_Chunks(byte[] png)
    {
        var chunks = new List<PngChunk>();
        var offset = SIGNATURE_LENGTH;

        while (offset < png.Length)
        {
            var dataLength = Read_BigEndianInt32(png, offset);

            Assert.InRange(dataLength, 0, png.Length - offset - CHUNK_OVERHEAD);

            var typeStart = offset + CHUNK_LENGTH_FIELD_LENGTH;
            var dataStart = typeStart + CHUNK_TYPE_FIELD_LENGTH;
            var crcStart = dataStart + dataLength;

            chunks.Add(new PngChunk(
                Encoding.ASCII.GetString(png, typeStart, CHUNK_TYPE_FIELD_LENGTH),
                png[dataStart..crcStart],
                unchecked((uint)Read_BigEndianInt32(png, crcStart))));

            offset = crcStart + CHUNK_CRC_FIELD_LENGTH;
        }

        return chunks;
    }

    static int Read_BigEndianInt32(byte[] bytes, int offset)
    {
        return (bytes[offset] << 24)
             | (bytes[offset + 1] << 16)
             | (bytes[offset + 2] << 8)
             | bytes[offset + 3];
    }

    /// <summary>
    /// A SECOND CRC-32, deliberately written the slow bit-at-a-time way and sharing no table with the
    /// production one. If both were table-driven, a single copied-and-wrong table would satisfy both.
    /// </summary>
    static uint Compute_Crc32_Independently(byte[] chunkTypeBytes, byte[] chunkData)
    {
        var remainder = 0xFFFFFFFFu;

        foreach (var value in chunkTypeBytes.Concat(chunkData))
        {
            remainder ^= value;

            for (var bit = 0; bit < 8; bit++)
                remainder = (remainder & 1) != 0
                    ? (remainder >> 1) ^ 0xEDB88320u
                    : remainder >> 1;
        }

        return remainder ^ 0xFFFFFFFFu;
    }

    sealed record PngChunk(string Type, byte[] Data, uint DeclaredCrc);
}
