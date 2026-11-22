using System.IO.Compression;

namespace MaplibreNative.Routing.Core.Tests;

/// <summary>Writes small Mapbox Vector Tiles for the tests: layers of line features with string properties.</summary>
internal static class TestTile
{
    public sealed record Line(IReadOnlyDictionary<string, string> Props, params (int X, int Y)[] Points);

    public static byte[] Build(params (string Name, Line[] Features)[] layers)
    {
        var tile = new List<byte>();
        foreach (var (name, features) in layers)
        {
            var keys = new List<string>();
            var values = new List<string>();
            int Index(List<string> list, string v)
            {
                int i = list.IndexOf(v);
                if (i < 0) { list.Add(v); i = list.Count - 1; }
                return i;
            }

            var layer = new List<byte>();
            layer.AddRange(Text(1, name));
            foreach (var f in features)
            {
                var tags = f.Props.SelectMany(p => new[] { (ulong)Index(keys, p.Key), (ulong)Index(values, p.Value) });
                var geom = new List<ulong> { 9, ZigZag(f.Points[0].X), ZigZag(f.Points[0].Y), (ulong)(((f.Points.Length - 1) << 3) | 2) };
                for (int i = 1; i < f.Points.Length; i++)
                {
                    geom.Add(ZigZag(f.Points[i].X - f.Points[i - 1].X));
                    geom.Add(ZigZag(f.Points[i].Y - f.Points[i - 1].Y));
                }
                var feature = new List<byte>();
                feature.AddRange(Packed(2, tags));
                feature.AddRange(Varint(3 << 3));
                feature.AddRange(Varint(2));
                feature.AddRange(Packed(4, geom));
                layer.AddRange(Bytes(2, feature));
            }
            foreach (var k in keys) layer.AddRange(Text(3, k));
            foreach (var v in values) layer.AddRange(Bytes(4, Text(1, v)));
            layer.AddRange(Varint(5 << 3));
            layer.AddRange(Varint(4096));
            tile.AddRange(Bytes(3, layer));
        }
        return tile.ToArray();
    }

    public static byte[] Gzip(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gz = new GZipStream(output, CompressionLevel.Fastest)) gz.Write(data);
        return output.ToArray();
    }

    public static Dictionary<string, string> Props(params (string Key, string Value)[] p) =>
        p.ToDictionary(x => x.Key, x => x.Value);

    private static ulong ZigZag(int n) => n >= 0 ? (ulong)n * 2 : (ulong)(-n) * 2 - 1;

    private static IEnumerable<byte> Varint(ulong n)
    {
        var bytes = new List<byte>();
        for (; n >= 0x80; n >>= 7) bytes.Add((byte)(n & 0x7f | 0x80));
        bytes.Add((byte)n);
        return bytes;
    }

    private static IEnumerable<byte> Bytes(int field, IEnumerable<byte> body)
    {
        var b = body.ToArray();
        return Varint((ulong)(field << 3 | 2)).Concat(Varint((ulong)b.Length)).Concat(b);
    }

    private static IEnumerable<byte> Text(int field, string s) => Bytes(field, System.Text.Encoding.UTF8.GetBytes(s));

    private static IEnumerable<byte> Packed(int field, IEnumerable<ulong> nums) => Bytes(field, nums.SelectMany(Varint));
}
