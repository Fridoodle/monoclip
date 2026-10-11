using System.Buffers.Binary;
namespace MonoClip.Core;

// Video size and duration of an MP4, read from its index (moov/mvhd/tkhd). Used for the share page tags.
public sealed record Mp4Info(int Width, int Height, TimeSpan Duration)
{
    public static Mp4Info? Read(string path)
    {
        using var s = File.OpenRead(path); var header = new byte[16]; long offset = 0;
        while (offset + 8 <= s.Length)
        {
            s.Position = offset; s.ReadExactly(header, 0, 8);
            long size = BinaryPrimitives.ReadUInt32BigEndian(header); bool moov = header.AsSpan(4, 4).SequenceEqual("moov"u8); int head = 8;
            if (size == 1) { s.ReadExactly(header, 8, 8); size = (long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8)); head = 16; }
            else if (size == 0) size = s.Length - offset;
            if (size < head || offset + size > s.Length) return null;
            if (moov && size < 64 * 1024 * 1024)
            {
                var data = new byte[size - head]; s.ReadExactly(data);
                int width = 0, height = 0; TimeSpan duration = TimeSpan.Zero; Walk(data, ref width, ref height, ref duration);
                return width > 0 && height > 0 ? new(width, height, duration) : null;
            }
            offset += size;
        }
        return null;
    }
    static void Walk(ReadOnlySpan<byte> data, ref int width, ref int height, ref TimeSpan duration)
    {
        for (int at = 0; at + 8 <= data.Length;)
        {
            long size = BinaryPrimitives.ReadUInt32BigEndian(data[at..]); var type = data.Slice(at + 4, 4); int head = 8;
            if (size == 1 && at + 16 <= data.Length) { size = (long)BinaryPrimitives.ReadUInt64BigEndian(data[(at + 8)..]); head = 16; }
            if (size < head || at + size > data.Length) return;
            var body = data.Slice(at + head, (int)size - head);
            if (type.SequenceEqual("trak"u8)) Walk(body, ref width, ref height, ref duration);
            else if (type.SequenceEqual("mvhd"u8) && body.Length >= 32)
            {
                // Version 0: 32-bit times; version 1: 64-bit times. Timescale follows creation/modification time.
                bool wide = body[0] == 1; uint scale = BinaryPrimitives.ReadUInt32BigEndian(body[(wide ? 20 : 12)..]);
                ulong length = wide ? BinaryPrimitives.ReadUInt64BigEndian(body[24..]) : BinaryPrimitives.ReadUInt32BigEndian(body[16..]);
                if (scale > 0) duration = TimeSpan.FromSeconds((double)length / scale);
            }
            else if (type.SequenceEqual("tkhd"u8) && width == 0)
            {
                // Width/height are 16.16 fixed point at the end of the box; audio tracks have 0 x 0.
                int at16 = body[0] == 1 ? 88 : 76;
                if (body.Length >= at16 + 8) { width = (int)(BinaryPrimitives.ReadUInt32BigEndian(body[at16..]) >> 16); height = (int)(BinaryPrimitives.ReadUInt32BigEndian(body[(at16 + 4)..]) >> 16); if (width == 0) height = 0; }
            }
            at += (int)size;
        }
    }
}
