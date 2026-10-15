using System.Buffers.Binary;
namespace MonoClip.Core;

// Moves the MP4 index (moov) in front of the media data, like ffmpeg's +faststart.
// Players can then start streaming over HTTP without first fetching the end of the file.
public static class Mp4FastStart
{
    record Box(string Type, long Offset, long Size);
    static readonly HashSet<string> Containers = ["moov", "trak", "mdia", "minf", "stbl"];

    // Returns false when the file was already fast-start (it is copied unchanged).
    public static bool Apply(string input, string output)
    {
        using var source = File.OpenRead(input);
        var boxes = TopLevel(source);
        int moov = boxes.FindIndex(b => b.Type == "moov"), mdat = boxes.FindIndex(b => b.Type == "mdat");
        if (moov < 0 || mdat < 0) throw new InvalidDataException("Not a valid MP4 file (moov/mdat missing).");
        using var target = File.Create(output);
        if (moov < mdat) { source.Position = 0; source.CopyTo(target); return false; }
        var index = new byte[boxes[moov].Size]; source.Position = boxes[moov].Offset; source.ReadExactly(index);
        // Everything from the first mdat on moves back by the size of the index.
        Patch(index, 8, index.Length, boxes[moov].Size);
        foreach (var box in boxes.Take(mdat)) Copy(source, target, box);
        target.Write(index);
        foreach (var box in boxes.Skip(mdat).Where(b => b.Type != "moov")) Copy(source, target, box);
        return true;
    }
    static List<Box> TopLevel(Stream s)
    {
        var boxes = new List<Box>(); var header = new byte[16]; long offset = 0;
        while (offset < s.Length)
        {
            if (s.Length - offset < 8) throw new InvalidDataException("Abgeschnittene MP4-Box.");
            s.Position = offset; s.ReadExactly(header, 0, 8);
            long size = BinaryPrimitives.ReadUInt32BigEndian(header); var type = System.Text.Encoding.ASCII.GetString(header, 4, 4);
            if (size == 1) { s.ReadExactly(header, 8, 8); size = (long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8)); }
            else if (size == 0) size = s.Length - offset;
            if (size < 8 || offset + size > s.Length) throw new InvalidDataException("Invalid MP4 box size.");
            boxes.Add(new(type, offset, size)); offset += size;
        }
        return boxes;
    }
    static void Patch(byte[] data, int start, int end, long delta)
    {
        for (int at = start; at + 8 <= end;)
        {
            long size = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at)); var type = System.Text.Encoding.ASCII.GetString(data, at + 4, 4); int header = 8;
            if (size == 1) { size = (long)BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(at + 8)); header = 16; }
            else if (size == 0) size = end - at;
            if (size < header || at + size > end) throw new InvalidDataException("Invalid MP4 index box.");
            int body = at + header, boxEnd = (int)(at + size);
            if (Containers.Contains(type)) Patch(data, body, boxEnd, delta);
            else if (type is "stco" or "co64")
            {
                // Full box: version/flags, entry count, then 32- or 64-bit absolute chunk offsets.
                int count = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(body + 4)), width = type == "stco" ? 4 : 8;
                if (body + 8 + (long)count * width > boxEnd) throw new InvalidDataException("Invalid chunk table.");
                for (int i = 0, p = body + 8; i < count; i++, p += width)
                    if (width == 4)
                    {
                        long moved = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(p)) + delta;
                        if (moved > uint.MaxValue) throw new NotSupportedException("Clip too large for 32-bit MP4 offsets.");
                        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(p), (uint)moved);
                    }
                    else BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(p), BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(p)) + (ulong)delta);
            }
            at = boxEnd;
        }
    }
    static void Copy(Stream source, Stream target, Box box)
    {
        source.Position = box.Offset; var buffer = new byte[1 << 20];
        for (long left = box.Size; left > 0;) { int n = source.Read(buffer, 0, (int)Math.Min(buffer.Length, left)); if (n == 0) throw new EndOfStreamException(); target.Write(buffer, 0, n); left -= n; }
    }
}
