using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace Tedd.FastNoise.Gallery;

/// <summary>
/// A minimal PNG writer: 8-bit RGB, no interlacing, one filter mode.
/// </summary>
/// <remarks>
/// Hand-rolled rather than pulled from a package, because the alternatives all drag in either a
/// Windows-only dependency or a large imaging library, and this tool runs on the Linux CI runner
/// that builds the documentation site. PNG's container format is four chunks and a CRC; the
/// compression is <see cref="ZLibStream"/>, which emits exactly the zlib wrapper an IDAT needs.
/// </remarks>
internal static class Png
{
    /// <summary>Writes an RGB image.</summary>
    /// <param name="path">Destination file.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="rgb">Pixel data, three bytes per pixel, row-major from the top left.</param>
    public static void Write(string path, int width, int height, ReadOnlySpan<byte> rgb)
    {
        using FileStream file = File.Create(path);

        // Signature.
        file.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header[..4], width);
        BinaryPrimitives.WriteInt32BigEndian(header.Slice(4, 4), height);
        header[8] = 8;   // bit depth
        header[9] = 2;   // colour type: truecolour
        header[10] = 0;  // compression: deflate
        header[11] = 0;  // filter method: adaptive
        header[12] = 0;  // interlace: none
        WriteChunk(file, "IHDR", header);

        // Each scanline is prefixed with its filter type. Zero means "store the bytes as they are",
        // which costs some compression ratio and saves a great deal of code.
        byte[] raw = new byte[height * ((width * 3) + 1)];
        int source = 0;
        int destination = 0;

        for (int y = 0; y < height; y++)
        {
            raw[destination++] = 0;
            rgb.Slice(source, width * 3).CopyTo(raw.AsSpan(destination));
            source += width * 3;
            destination += width * 3;
        }

        using MemoryStream compressed = new();
        using (ZLibStream deflate = new(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            deflate.Write(raw);
        }

        WriteChunk(file, "IDAT", compressed.ToArray());
        WriteChunk(file, "IEND", []);
    }

    private static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);

        Span<byte> typeBytes = stackalloc byte[4];
        for (int i = 0; i < 4; i++)
        {
            typeBytes[i] = (byte)type[i];
        }

        stream.Write(typeBytes);
        stream.Write(data);

        uint crc = Crc32(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }

    /// <summary>The CRC-32 PNG requires over a chunk's type and data.</summary>
    private static uint Crc32(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        uint crc = 0xFFFFFFFFu;
        crc = Accumulate(crc, first);
        crc = Accumulate(crc, second);
        return crc ^ 0xFFFFFFFFu;

        static uint Accumulate(uint crc, ReadOnlySpan<byte> data)
        {
            foreach (byte value in data)
            {
                crc ^= value;

                for (int bit = 0; bit < 8; bit++)
                {
                    crc = (crc >> 1) ^ (0xEDB88320u & (uint)(-(int)(crc & 1)));
                }
            }

            return crc;
        }
    }
}
