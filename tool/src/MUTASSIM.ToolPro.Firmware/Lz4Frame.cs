using System.Buffers.Binary;

namespace MUTASSIM.ToolPro.Firmware;

public sealed class Lz4FormatException(string message) : Exception(message);

/// <summary>
/// Streaming LZ4 decoder for the image files inside Samsung firmware (*.img.lz4, *.bin.lz4).
/// Supports the standard LZ4 frame format (dependent or independent blocks, block/content checksums,
/// concatenated and skippable frames) and the legacy LZ4 format. All checksums present in the file are
/// verified; a mismatch throws instead of producing a corrupted image.
/// </summary>
public static class Lz4Frame
{
    private const uint FrameMagic = 0x184D2204;
    private const uint LegacyMagic = 0x184C2102;
    private const uint SkippableMask = 0xFFFFFFF0;
    private const uint SkippableMagic = 0x184D2A50;
    private const int Window = 64 * 1024;
    private const int LegacyBlock = 8 * 1024 * 1024;

    /// <summary>Decodes all frames from <paramref name="input"/> into <paramref name="output"/>. Returns bytes written.</summary>
    public static long Decode(Stream input, Stream output, CancellationToken ct = default)
    {
        long total = 0;
        var header = new byte[4];
        var frames = 0;

        while (true)
        {
            var got = ReadUpTo(input, header, 4);
            if (got == 0 && frames > 0) return total;
            if (got < 4) throw new Lz4FormatException(frames == 0 ? "Not an LZ4 file." : "Truncated LZ4 data.");

            var magic = BinaryPrimitives.ReadUInt32LittleEndian(header);
            if (magic == FrameMagic) total += DecodeFrame(input, output, ct);
            else if (magic == LegacyMagic) { total += DecodeLegacy(input, output, ct, out var ended); frames++; if (ended) return total; continue; }
            else if ((magic & SkippableMask) == SkippableMagic) Skip(input, ReadU32(input));
            else throw new Lz4FormatException($"Unknown LZ4 magic 0x{magic:X8}.");
            frames++;
        }
    }

    public static long DecodeFile(string source, string destination, CancellationToken ct = default)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
        using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        return Decode(input, output, ct);
    }

    private static long DecodeFrame(Stream input, Stream output, CancellationToken ct)
    {
        var flg = ReadByte(input);
        var bd = ReadByte(input);
        if ((flg >> 6) != 1) throw new Lz4FormatException("Unsupported LZ4 frame version.");

        var blockChecksum = (flg & 0x10) != 0;
        var hasContentSize = (flg & 0x08) != 0;
        var contentChecksum = (flg & 0x04) != 0;
        var hasDictId = (flg & 0x01) != 0;
        var maxBlock = ((bd >> 4) & 7) switch
        {
            4 => 64 * 1024,
            5 => 256 * 1024,
            6 => 1024 * 1024,
            7 => 4 * 1024 * 1024,
            _ => throw new Lz4FormatException("Invalid LZ4 block size.")
        };

        var descriptor = new List<byte> { (byte)flg, (byte)bd };
        long? contentSize = null;
        if (hasContentSize)
        {
            var cs = ReadExact(input, 8);
            descriptor.AddRange(cs);
            contentSize = BinaryPrimitives.ReadInt64LittleEndian(cs);
        }
        if (hasDictId)
        {
            descriptor.AddRange(ReadExact(input, 4));
            throw new Lz4FormatException("LZ4 frames with an external dictionary are not supported.");
        }

        var hc = ReadByte(input);
        var expectedHc = (XxHash32.Hash(descriptor.ToArray(), 0) >> 8) & 0xFF;
        if (hc != expectedHc) throw new Lz4FormatException("LZ4 frame header checksum mismatch.");

        // Buffer = [up to 64 KB of previous output][current block], so dependent blocks can reference history.
        var buffer = new byte[Window + maxBlock];
        var history = 0;
        var compressed = new byte[maxBlock];
        var hash = contentChecksum ? new XxHash32(0) : null;
        long written = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var raw = ReadU32(input);
            if (raw == 0) break; // EndMark

            var uncompressed = (raw & 0x80000000) != 0;
            var size = (int)(raw & 0x7FFFFFFF);
            if (size > maxBlock) throw new Lz4FormatException("LZ4 block larger than the declared maximum.");

            ReadExactInto(input, compressed, size);
            if (blockChecksum && ReadU32(input) != XxHash32.Hash(compressed.AsSpan(0, size), 0))
                throw new Lz4FormatException("LZ4 block checksum mismatch.");

            int produced;
            if (uncompressed)
            {
                Buffer.BlockCopy(compressed, 0, buffer, history, size);
                produced = size;
            }
            else
            {
                produced = DecodeBlock(compressed.AsSpan(0, size), buffer, history, maxBlock);
            }

            output.Write(buffer, history, produced);
            hash?.Append(buffer.AsSpan(history, produced));
            written += produced;

            // Keep the last 64 KB as history for the next block.
            var end = history + produced;
            var keep = Math.Min(Window, end);
            Buffer.BlockCopy(buffer, end - keep, buffer, 0, keep);
            history = keep;
        }

        if (hash is not null && ReadU32(input) != hash.Digest())
            throw new Lz4FormatException("LZ4 content checksum mismatch: the file is corrupted.");
        if (contentSize is { } expected && expected != written)
            throw new Lz4FormatException($"LZ4 content size mismatch ({written} of {expected} bytes).");

        return written;
    }

    private static long DecodeLegacy(Stream input, Stream output, CancellationToken ct, out bool endedAtEof)
    {
        var compressed = new byte[LZ4CompressBound(LegacyBlock)];
        var buffer = new byte[LegacyBlock];
        var sizeBytes = new byte[4];
        long written = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var got = ReadUpTo(input, sizeBytes, 4);
            if (got == 0) { endedAtEof = true; return written; }
            if (got < 4) throw new Lz4FormatException("Truncated legacy LZ4 data.");

            var size = BinaryPrimitives.ReadUInt32LittleEndian(sizeBytes);
            if (size == LegacyMagic) continue; // next legacy frame
            if (size == FrameMagic || (size & SkippableMask) == SkippableMagic)
            {
                // A new modern frame follows: push the magic back by decoding it here.
                if (size == FrameMagic) written += DecodeFrame(input, output, ct);
                else Skip(input, ReadU32(input));
                endedAtEof = false;
                return written;
            }
            if (size > compressed.Length) throw new Lz4FormatException("Invalid legacy LZ4 block size.");

            ReadExactInto(input, compressed, (int)size);
            var produced = DecodeBlock(compressed.AsSpan(0, (int)size), buffer, 0, LegacyBlock);
            output.Write(buffer, 0, produced);
            written += produced;
        }
    }

    /// <summary>Decodes one LZ4 block into dst starting at dstStart. Returns the number of bytes produced.</summary>
    internal static int DecodeBlock(ReadOnlySpan<byte> src, byte[] dst, int dstStart, int maxOutput)
    {
        var ip = 0;
        var op = dstStart;
        var opLimit = dstStart + maxOutput;

        while (ip < src.Length)
        {
            var token = src[ip++];

            var literal = token >> 4;
            if (literal == 15) literal += ReadLength(src, ref ip);
            if (ip + literal > src.Length || op + literal > opLimit)
                throw new Lz4FormatException("Corrupted LZ4 block (literals).");
            src.Slice(ip, literal).CopyTo(dst.AsSpan(op));
            ip += literal;
            op += literal;

            if (ip == src.Length) break; // last sequence has literals only

            if (ip + 2 > src.Length) throw new Lz4FormatException("Corrupted LZ4 block (offset).");
            var offset = src[ip] | (src[ip + 1] << 8);
            ip += 2;
            if (offset == 0 || offset > op) throw new Lz4FormatException("Corrupted LZ4 block (match offset).");

            var match = (token & 0x0F) + 4;
            if ((token & 0x0F) == 15) match += ReadLength(src, ref ip);
            if (op + match > opLimit) throw new Lz4FormatException("Corrupted LZ4 block (match length).");

            var from = op - offset;
            if (offset >= match)
            {
                Buffer.BlockCopy(dst, from, dst, op, match);
                op += match;
            }
            else
            {
                for (var i = 0; i < match; i++) dst[op++] = dst[from + i]; // overlapping copy
            }
        }

        return op - dstStart;
    }

    private static int ReadLength(ReadOnlySpan<byte> src, ref int ip)
    {
        var len = 0;
        byte b;
        do
        {
            if (ip >= src.Length) throw new Lz4FormatException("Corrupted LZ4 block (length).");
            b = src[ip++];
            len += b;
        } while (b == 255);
        return len;
    }

    private static int LZ4CompressBound(int size) => size + size / 255 + 16;

    private static int ReadUpTo(Stream s, byte[] buffer, int count)
    {
        var total = 0;
        while (total < count)
        {
            var n = s.Read(buffer, total, count - total);
            if (n == 0) break;
            total += n;
        }
        return total;
    }

    private static byte[] ReadExact(Stream s, int count)
    {
        var b = new byte[count];
        ReadExactInto(s, b, count);
        return b;
    }

    private static void ReadExactInto(Stream s, byte[] buffer, int count)
    {
        if (ReadUpTo(s, buffer, count) != count) throw new Lz4FormatException("Truncated LZ4 data.");
    }

    private static int ReadByte(Stream s)
    {
        var b = s.ReadByte();
        if (b < 0) throw new Lz4FormatException("Truncated LZ4 data.");
        return b;
    }

    private static uint ReadU32(Stream s) => BinaryPrimitives.ReadUInt32LittleEndian(ReadExact(s, 4));

    private static void Skip(Stream s, uint count)
    {
        var tmp = new byte[Math.Min(count, 65536u)];
        var left = (long)count;
        while (left > 0)
        {
            var n = s.Read(tmp, 0, (int)Math.Min(tmp.Length, left));
            if (n == 0) throw new Lz4FormatException("Truncated skippable frame.");
            left -= n;
        }
    }
}

/// <summary>Streaming xxHash32 (used by the LZ4 frame format for its checksums).</summary>
public sealed class XxHash32
{
    private const uint P1 = 2654435761U, P2 = 2246822519U, P3 = 3266489917U, P4 = 668265263U, P5 = 374761393U;
    private readonly uint _seed;
    private uint _v1, _v2, _v3, _v4;
    private readonly byte[] _mem = new byte[16];
    private int _memSize;
    private ulong _length;

    public XxHash32(uint seed)
    {
        _seed = seed;
        _v1 = seed + P1 + P2;
        _v2 = seed + P2;
        _v3 = seed;
        _v4 = seed - P1;
    }

    public static uint Hash(ReadOnlySpan<byte> data, uint seed)
    {
        var h = new XxHash32(seed);
        h.Append(data);
        return h.Digest();
    }

    public void Append(ReadOnlySpan<byte> data)
    {
        _length += (ulong)data.Length;
        if (_memSize + data.Length < 16)
        {
            data.CopyTo(_mem.AsSpan(_memSize));
            _memSize += data.Length;
            return;
        }

        if (_memSize > 0)
        {
            var fill = 16 - _memSize;
            data[..fill].CopyTo(_mem.AsSpan(_memSize));
            Round16(_mem);
            data = data[fill..];
            _memSize = 0;
        }

        while (data.Length >= 16)
        {
            Round16(data);
            data = data[16..];
        }

        data.CopyTo(_mem);
        _memSize = data.Length;
    }

    public uint Digest()
    {
        uint h = _length >= 16
            ? Rotl(_v1, 1) + Rotl(_v2, 7) + Rotl(_v3, 12) + Rotl(_v4, 18)
            : _seed + P5;
        h += (uint)_length;

        var p = _mem.AsSpan(0, _memSize);
        while (p.Length >= 4)
        {
            h += BinaryPrimitives.ReadUInt32LittleEndian(p) * P3;
            h = Rotl(h, 17) * P4;
            p = p[4..];
        }
        foreach (var b in p)
        {
            h += b * P5;
            h = Rotl(h, 11) * P1;
        }

        h ^= h >> 15; h *= P2;
        h ^= h >> 13; h *= P3;
        h ^= h >> 16;
        return h;
    }

    private void Round16(ReadOnlySpan<byte> p)
    {
        _v1 = Round(_v1, BinaryPrimitives.ReadUInt32LittleEndian(p));
        _v2 = Round(_v2, BinaryPrimitives.ReadUInt32LittleEndian(p[4..]));
        _v3 = Round(_v3, BinaryPrimitives.ReadUInt32LittleEndian(p[8..]));
        _v4 = Round(_v4, BinaryPrimitives.ReadUInt32LittleEndian(p[12..]));
    }

    private static uint Round(uint acc, uint input) => Rotl(acc + input * P2, 13) * P1;
    private static uint Rotl(uint x, int r) => (x << r) | (x >> (32 - r));
}
