using System.IO.Compression;
using System.Text;

namespace AIOrchestratorCoreLib.Imaging;

/// <summary>
/// A PNG encoder with NO dependency on anything outside the base class library — no NuGet package,
/// and no System.Drawing / WPF imaging, which would drag a `net10.0-windows` target into a library
/// whose only reader is a plain `net10.0` test project. Everything below is the PNG container written
/// by hand around the one piece the framework already does correctly: DEFLATE.
///
/// IT EXISTS FOR SCREENSHOTS ON THEIR WAY TO TELEGRAM, and that destination settles two questions a
/// general-purpose encoder would leave open:
///
/// 1. **The alpha channel is dropped.** A Windows DIB section hands us four bytes per pixel, but the
///    alpha byte of a screen capture is meaningless — the compositor leaves whatever it likes there,
///    frequently zero. Encoding it faithfully produces a PNG that Telegram renders fully transparent,
///    i.e. invisible or black. So we emit colour type 2, truecolour with no alpha, and the owner sees
///    the screen they asked for.
/// 2. **Correctness over ratio.** Every scanline is written with filter type None. Filter selection is
///    where a PNG encoder wins its last 20-30% of size, and it is also where it grows its bugs; the
///    DEFLATE pass already carries the bulk of the saving, and a screenshot bound for a phone is not
///    competing on bytes.
/// </summary>
public static class Png_Encoder
{
    /// <summary>
    /// The fixed 8-byte file magic. The high bit in the first byte and the CR-LF / LF pair in the tail
    /// are a deliberate trap for transfers that "helpfully" translate line endings or strip to 7-bit:
    /// a mangled file then fails at byte one instead of halfway through the pixels.
    /// </summary>
    public static readonly byte[] PNG_SIGNATURE = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public const string IMAGE_HEADER_CHUNK_TYPE = "IHDR";
    public const string IMAGE_DATA_CHUNK_TYPE = "IDAT";
    public const string IMAGE_END_CHUNK_TYPE = "IEND";

    /// <summary>Eight bits per channel — exactly what the source DIB carries, so nothing is rescaled.</summary>
    public const byte BIT_DEPTH = 8;

    /// <summary>PNG colour type 2: three channels, R,G,B, no alpha. See the class remarks for why.</summary>
    public const byte COLOUR_TYPE_TRUECOLOUR = 2;

    /// <summary>The only compression method the PNG specification defines: DEFLATE inside ZLIB.</summary>
    public const byte COMPRESSION_METHOD_DEFLATE = 0;

    /// <summary>The only filter method the specification defines: per-scanline adaptive filtering.</summary>
    public const byte FILTER_METHOD_ADAPTIVE = 0;

    /// <summary>Not interlaced. A progressive screenshot would be pure cost with no reader for it.</summary>
    public const byte INTERLACE_METHOD_NONE = 0;

    /// <summary>Filter type 0, "None": the scanline is stored as-is. Chosen for every row.</summary>
    public const byte FILTER_TYPE_NONE = 0;

    /// <summary>What the caller hands us: B, G, R, A, in that order, one byte each.</summary>
    public const int SOURCE_BYTES_PER_PIXEL = 4;

    /// <summary>What we write: R, G, B. The alpha byte is stepped over, never encoded.</summary>
    public const int ENCODED_BYTES_PER_PIXEL = 3;

    /// <summary>
    /// Encodes a top-down BGRA32 buffer as an opaque 8-bit truecolour PNG file.
    /// </summary>
    /// <param name="width">Pixels across. Must be positive.</param>
    /// <param name="height">Pixels down. Must be positive.</param>
    /// <param name="bgra">
    /// Top-down pixels, stride exactly <paramref name="width"/> * 4, channel order B,G,R,A — the
    /// layout a Windows DIB section produces. A buffer LONGER than needed is accepted, because a
    /// capture API may hand back a pooled or padded array; a shorter one is refused, since the
    /// alternative is reading a torn final scanline out of whatever follows and calling it an image.
    /// </param>
    /// <returns>The complete PNG file, ready to be written to disk or uploaded.</returns>
    public static byte[] Encode_Bgra32(int width, int height, byte[] bgra)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), width, "image width must be positive");

        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), height, "image height must be positive");

        // Widened DELIBERATELY. A wrong width and height can multiply past int.MaxValue, and an
        // overflowed requirement wraps NEGATIVE — which every buffer trivially satisfies, so the
        // guard would wave through precisely the case it exists to stop.
        var requiredLength = (long)width * height * SOURCE_BYTES_PER_PIXEL;

        if (bgra.Length < requiredLength)
            throw new ArgumentException(
                $"a {width}x{height} BGRA32 image needs {requiredLength} bytes, got {bgra.Length}",
                nameof(bgra));

        var filteredScanlines = Build_FilteredScanlines(width, height, bgra);

        using var file = new MemoryStream();

        file.Write(PNG_SIGNATURE, 0, PNG_SIGNATURE.Length);

        Write_Chunk(file, IMAGE_HEADER_CHUNK_TYPE, Build_ImageHeaderData(width, height));
        Write_Chunk(file, IMAGE_DATA_CHUNK_TYPE, Compress_AsZLibStream(filteredScanlines));
        Write_Chunk(file, IMAGE_END_CHUNK_TYPE, []);

        return file.ToArray();
    }

    /// <summary>
    /// The 13-byte IHDR payload. Every field is fixed by this encoder's contract except the two
    /// dimensions, so the only thing left that can be wrong here is byte order — which is why the
    /// big-endian write is a named helper and never an inlined shift.
    /// </summary>
    static byte[] Build_ImageHeaderData(int width, int height)
    {
        using var header = new MemoryStream();

        Write_BigEndianInt32(header, width);
        Write_BigEndianInt32(header, height);

        header.WriteByte(BIT_DEPTH);
        header.WriteByte(COLOUR_TYPE_TRUECOLOUR);
        header.WriteByte(COMPRESSION_METHOD_DEFLATE);
        header.WriteByte(FILTER_METHOD_ADAPTIVE);
        header.WriteByte(INTERLACE_METHOD_NONE);

        return header.ToArray();
    }

    /// <summary>
    /// Turns the BGRA buffer into the byte sequence that goes INSIDE the compressed stream: per row,
    /// one filter-type byte followed by that row's R,G,B triples.
    ///
    /// The channel reorder and the alpha drop happen together in one loop because they are the same
    /// read; splitting them into two passes would allocate a screenshot-sized buffer twice for an
    /// identical result.
    /// </summary>
    static byte[] Build_FilteredScanlines(int width, int height, byte[] bgra)
    {
        var sourceStride = width * SOURCE_BYTES_PER_PIXEL;
        var filteredStride = 1 + (width * ENCODED_BYTES_PER_PIXEL);

        var filtered = new byte[filteredStride * height];

        for (var row = 0; row < height; row++)
        {
            var sourceIndex = row * sourceStride;
            var filteredIndex = row * filteredStride;

            filtered[filteredIndex++] = FILTER_TYPE_NONE;

            for (var column = 0; column < width; column++)
            {
                var blue = bgra[sourceIndex];
                var green = bgra[sourceIndex + 1];
                var red = bgra[sourceIndex + 2];

                // The fourth source byte is alpha. Stepped over, never written.
                sourceIndex += SOURCE_BYTES_PER_PIXEL;

                filtered[filteredIndex++] = red;
                filtered[filteredIndex++] = green;
                filtered[filteredIndex++] = blue;
            }
        }

        return filtered;
    }

    /// <summary>
    /// Wraps the filtered scanlines in a ZLIB stream — the two-byte ZLIB header and the trailing
    /// Adler-32, NOT bare DEFLATE. Handing `DeflateStream` output to a PNG writer produces a file that
    /// some decoders open and others reject outright, which is the worst available failure mode, so
    /// the distinction is stated in this method's name rather than left to a comment.
    ///
    /// The whole image goes into ONE IDAT chunk. The specification allows any number and every decoder
    /// concatenates them before inflating; splitting buys streaming, which nothing here does.
    /// </summary>
    static byte[] Compress_AsZLibStream(byte[] filteredScanlines)
    {
        using var compressed = new MemoryStream();

        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(filteredScanlines, 0, filteredScanlines.Length);

        return compressed.ToArray();
    }

    /// <summary>
    /// One chunk: length, four-ASCII type, payload, then the CRC taken over TYPE AND PAYLOAD TOGETHER.
    /// The length field covers the payload ALONE — it excludes both the type and the CRC — and getting
    /// that wrong yields a file whose first chunk reads perfectly and whose second lands mid-stream.
    /// </summary>
    static void Write_Chunk(Stream file, string chunkType, byte[] chunkData)
    {
        var chunkTypeBytes = Encoding.ASCII.GetBytes(chunkType);

        Write_BigEndianInt32(file, chunkData.Length);

        file.Write(chunkTypeBytes, 0, chunkTypeBytes.Length);
        file.Write(chunkData, 0, chunkData.Length);

        Write_BigEndianUnsignedInt32(file, Compute_Crc32(chunkTypeBytes, chunkData));
    }

    /// <summary>PNG is big-endian throughout; this machine is not. Never inline this.</summary>
    static void Write_BigEndianInt32(Stream file, int value)
    {
        Write_BigEndianUnsignedInt32(file, unchecked((uint)value));
    }

    static void Write_BigEndianUnsignedInt32(Stream file, uint value)
    {
        file.WriteByte((byte)(value >> 24));
        file.WriteByte((byte)(value >> 16));
        file.WriteByte((byte)(value >> 8));
        file.WriteByte((byte)value);
    }

    /// <summary>
    /// The reversed CRC-32 polynomial the PNG specification mandates. It is the same one ZIP and gzip
    /// use, which is why a subtly wrong implementation still looks plausible against a table copied
    /// from anywhere — the fixed IEND checksum (0xAE426082) is the cheap way to prove this one right.
    /// </summary>
    public const uint CRC32_POLYNOMIAL = 0xEDB88320u;

    /// <summary>Every CRC starts with all bits set and is inverted again at the end.</summary>
    const uint CRC32_ALL_BITS_SET = 0xFFFFFFFFu;

    const int BITS_PER_BYTE = 8;
    const int CRC32_TABLE_LENGTH = 256;
    const uint LOW_BYTE_MASK = 0xFF;

    /// <summary>
    /// One byte-at-a-time table, built once. Screenshots push several megabytes of IDAT through here,
    /// and the bit-at-a-time form would do eight times the work for an identical answer.
    /// </summary>
    static readonly uint[] CRC32_TABLE = Build_Crc32Table();

    static uint[] Build_Crc32Table()
    {
        var table = new uint[CRC32_TABLE_LENGTH];

        for (var index = 0; index < CRC32_TABLE_LENGTH; index++)
        {
            var remainder = (uint)index;

            for (var bit = 0; bit < BITS_PER_BYTE; bit++)
                remainder = (remainder & 1) != 0
                    ? (remainder >> 1) ^ CRC32_POLYNOMIAL
                    : remainder >> 1;

            table[index] = remainder;
        }

        return table;
    }

    /// <summary>
    /// The chunk CRC, taken across the type bytes and the payload as ONE continuous message. They
    /// arrive as two arrays only because the caller holds them that way; concatenating into a scratch
    /// buffer first would double the allocation of every IDAT for no gain.
    /// </summary>
    static uint Compute_Crc32(byte[] chunkTypeBytes, byte[] chunkData)
    {
        var remainder = CRC32_ALL_BITS_SET;

        remainder = Accumulate_Crc32(remainder, chunkTypeBytes);
        remainder = Accumulate_Crc32(remainder, chunkData);

        return remainder ^ CRC32_ALL_BITS_SET;
    }

    static uint Accumulate_Crc32(uint remainder, byte[] bytes)
    {
        foreach (var value in bytes)
            remainder = CRC32_TABLE[(remainder ^ value) & LOW_BYTE_MASK] ^ (remainder >> BITS_PER_BYTE);

        return remainder;
    }
}
