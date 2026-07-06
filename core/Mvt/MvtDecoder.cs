namespace MaplibreNative.Routing.Core.Mvt;

internal enum MvtGeomType : byte { Unknown = 0, Point = 1, LineString = 2, Polygon = 3 }

internal sealed class MvtFeature
{
    public ulong Id;
    public MvtGeomType Type;
    public uint[] Tags = [];
    public List<List<(int X, int Y)>> Geometry = [];
}

internal sealed class MvtLayer
{
    public string Name = "";
    public uint Extent = 4096;
    public List<string> Keys = [];
    public List<object> Values = [];
    public List<MvtFeature> Features = [];
}

internal sealed class MvtTile
{
    public List<MvtLayer> Layers = [];

    public MvtLayer? GetLayer(string name) =>
        Layers.Find(l => l.Name.Equals(name, StringComparison.Ordinal));
}

internal static class MvtDecoder
{
    public static MvtTile Decode(ReadOnlySpan<byte> data)
    {
        var tile = new MvtTile();
        var reader = new ProtobufReader(data);
        while (reader.HasMore)
        {
            int field = reader.ReadFieldTag(out int wt);
            if (field == 3 && wt == 2)
                tile.Layers.Add(ReadLayer(reader.ReadLengthDelimited()));
            else
                reader.Skip(wt);
        }
        return tile;
    }

    public static MvtTile Decode(byte[] data) => Decode(data.AsSpan());

    public static Dictionary<string, object> GetProperties(MvtFeature feature, MvtLayer layer)
    {
        var props = new Dictionary<string, object>(feature.Tags.Length / 2);
        for (int i = 0; i + 1 < feature.Tags.Length; i += 2)
        {
            int ki = (int)feature.Tags[i];
            int vi = (int)feature.Tags[i + 1];
            if (ki < layer.Keys.Count && vi < layer.Values.Count)
                props[layer.Keys[ki]] = layer.Values[vi];
        }
        return props;
    }

    private static MvtLayer ReadLayer(ReadOnlySpan<byte> data)
    {
        var layer = new MvtLayer();
        var reader = new ProtobufReader(data);
        while (reader.HasMore)
        {
            int field = reader.ReadFieldTag(out int wt);
            switch (field)
            {
                case 1 when wt == 2: layer.Name = reader.ReadString(); break;
                case 2 when wt == 2: layer.Features.Add(ReadFeature(reader.ReadLengthDelimited())); break;
                case 3 when wt == 2: layer.Keys.Add(reader.ReadString()); break;
                case 4 when wt == 2: layer.Values.Add(ReadValue(reader.ReadLengthDelimited())); break;
                case 5 when wt == 0: layer.Extent = reader.ReadVarint32(); break;
                default: reader.Skip(wt); break;
            }
        }
        return layer;
    }

    private static MvtFeature ReadFeature(ReadOnlySpan<byte> data)
    {
        var feature = new MvtFeature();
        var reader = new ProtobufReader(data);
        ReadOnlySpan<byte> geomBytes = default;

        while (reader.HasMore)
        {
            int field = reader.ReadFieldTag(out int wt);
            switch (field)
            {
                case 1 when wt == 0: feature.Id = reader.ReadVarint64(); break;
                case 2 when wt == 2: feature.Tags = ReadPackedVarints(reader.ReadLengthDelimited()); break;
                case 3 when wt == 0: feature.Type = (MvtGeomType)reader.ReadVarint32(); break;
                case 4 when wt == 2: geomBytes = reader.ReadLengthDelimited(); break;
                default: reader.Skip(wt); break;
            }
        }

        if (geomBytes.Length > 0)
            feature.Geometry = DecodeGeometry(geomBytes);

        return feature;
    }

    private static object ReadValue(ReadOnlySpan<byte> data)
    {
        var reader = new ProtobufReader(data);
        while (reader.HasMore)
        {
            int field = reader.ReadFieldTag(out int wt);
            switch (field)
            {
                case 1 when wt == 2: return reader.ReadString();
                case 2 when wt == 5: return reader.ReadFloat();
                case 3 when wt == 1: return reader.ReadDouble();
                case 4 when wt == 0: return (long)reader.ReadVarint64();
                case 5 when wt == 0: return reader.ReadVarint64();
                case 6 when wt == 0: return reader.ReadSInt32();
                case 7 when wt == 0: return reader.ReadVarint64() != 0;
                default: reader.Skip(wt); break;
            }
        }
        return "";
    }

    private static uint[] ReadPackedVarints(ReadOnlySpan<byte> data)
    {
        var result = new List<uint>();
        var reader = new ProtobufReader(data);
        while (reader.HasMore)
            result.Add(reader.ReadVarint32());
        return result.ToArray();
    }

    private static List<List<(int, int)>> DecodeGeometry(ReadOnlySpan<byte> data)
    {
        var rings = new List<List<(int, int)>>();
        var current = new List<(int, int)>();
        int cursorX = 0, cursorY = 0;

        var reader = new ProtobufReader(data);
        while (reader.HasMore)
        {
            uint cmdInt = reader.ReadVarint32();
            int cmdId = (int)(cmdInt & 0x7);
            int cmdCount = (int)(cmdInt >> 3);

            switch (cmdId)
            {
                case 1: // MoveTo
                    if (current.Count > 0)
                    {
                        rings.Add(current);
                        current = new List<(int, int)>();
                    }
                    for (int i = 0; i < cmdCount; i++)
                    {
                        cursorX += ZigZag(reader.ReadVarint32());
                        cursorY += ZigZag(reader.ReadVarint32());
                        current.Add((cursorX, cursorY));
                    }
                    break;

                case 2: // LineTo
                    for (int i = 0; i < cmdCount; i++)
                    {
                        cursorX += ZigZag(reader.ReadVarint32());
                        cursorY += ZigZag(reader.ReadVarint32());
                        current.Add((cursorX, cursorY));
                    }
                    break;

                case 7: // ClosePath
                    if (current.Count > 0)
                        current.Add(current[0]);
                    break;
            }
        }

        if (current.Count > 0)
            rings.Add(current);

        return rings;
    }

    private static int ZigZag(uint n) => (int)((n >> 1) ^ (~(n & 1) + 1));
}
