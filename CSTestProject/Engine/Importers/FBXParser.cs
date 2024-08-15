using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Numerics;
using System.IO.Compression;
using System.Diagnostics;
using Weesals.Utility;

namespace Weesals.Engine.Importers {
    public struct FBXParser {

        public static double UnpackTime(long time) {
            return (double)time / 46186158000L;
        }

        public struct Property {
            public enum Types : byte {
                LONG = (byte)'L',
                INTEGER = (byte)'I',
                STRING = (byte)'S',
                FLOAT = (byte)'F',
                DOUBLE = (byte)'D',
                ARRAY_DOUBLE = (byte)'d',
                ARRAY_INT = (byte)'i',
                ARRAY_LONG = (byte)'l',
                ARRAY_FLOAT = (byte)'f',
                BINARY = (byte)'R',
                VOID = (byte)' '
            };
            public Types Type;
            public RangeInt Value;
            public bool IsBinary;

            public ReadOnlySpan<byte> AsSpan(ReadOnlySpan<byte> data) { return data.Slice(Value.Start, Value.Length); }
            public uint AsU32(ReadOnlySpan<byte> data) { return MemoryMarshal.Read<uint>(AsSpan(data)); }
            public int AsI32(ReadOnlySpan<byte> data) { return MemoryMarshal.Read<int>(AsSpan(data)); }
            public ulong AsU64(ReadOnlySpan<byte> data) { return MemoryMarshal.Read<ulong>(AsSpan(data)); }
            public long AsI64(ReadOnlySpan<byte> data) { return MemoryMarshal.Read<long>(AsSpan(data)); }
            public float AsFloat(ReadOnlySpan<byte> data) { return MemoryMarshal.Read<float>(AsSpan(data)); }
            public double AsDouble(ReadOnlySpan<byte> data) { return MemoryMarshal.Read<double>(AsSpan(data)); }
            public double AsTime(ReadOnlySpan<byte> data) { return UnpackTime(AsI64(data)); }
            public string AsString(ReadOnlySpan<byte> data) { return Encoding.UTF8.GetString(AsSpan(data)); }
            public string AsStringNull(ReadOnlySpan<byte> data) {
                int e = 0;
                data = AsSpan(data);
                for (; e < data.Length && data[e] != '\0'; ++e) ;
                if (e < data.Length) data = data.Slice(0, e);
                return Encoding.UTF8.GetString(data);
            }

            public bool ValueEquals(ReadOnlySpan<byte> data, ReadOnlySpan<byte> value) { return AsSpan(data).SequenceEqual(value); }
            public override string? ToString() {
                return Value.Length == 0 ? $"Empty<{Type}>"
                    : Type == Types.LONG ? AsI64(gLastData).ToString()
                    : Type == Types.INTEGER ? AsI32(gLastData).ToString()
                    : Type == Types.STRING ? AsString(gLastData)
                    : Type == Types.FLOAT ? AsFloat(gLastData).ToString()
                    : Type == Types.DOUBLE ? AsDouble(gLastData).ToString()
                    : base.ToString();
            }
        }
        public struct Connection {
            public enum Types {
                OBJECT_OBJECT,
                OBJECT_PROPERTY,
                PROPERTY_OBJECT,
                PROPERTY_PROPERTY,
            };

            public Types Type = Types.OBJECT_OBJECT;
            public ulong From = 0;
            public ulong To = 0;
            public RangeInt FromProperty;
            public RangeInt ToProperty;
            public Connection() { }
            //private readonly string fromPropStr => FromProperty == null ? "null" : Encoding.UTF8.GetString(FromProperty);
            //private readonly string toPropStr => ToProperty == null ? "null" : Encoding.UTF8.GetString(ToProperty);
        }
        public struct TakeInfo {
            public string Name;
            public string Filename;
            public double local_time_from;
            public double local_time_to;
            public double reference_time_from;
            public double reference_time_to;
        }
        public class FBXNode {
            public string Id;
            public List<Property> Properties = new();
            public List<FBXNode> Children = new();
            public FBXNode(string id) { Id = id; }
            public FBXNode? FindChild(string id) {
                foreach (var child in Children) if (child.Id == id) return child;
                return default;
            }
            public override string ToString() { return Id; }
        }
        public class FBXObject {
            public string Name;
            public FBXObject(string name) {
                Name = name;
            }
        }
        public class FBXMesh : FBXObject {
            public int[] Indices;
            public Vector3[] Positions;
            public FBXMesh(string name) : base(name) { }
        }
        public class FBXMaterial : FBXObject {
            public Dictionary<string, Vector3> Properties = new();
            public FBXMaterial(string name) : base(name) { }
        }
        public class FBXTexture : FBXObject {
            public string Filename;
            public string RelativeFilename;
            public byte[] Media;
            public FBXTexture(string name) : base(name) { }
        }
        public struct ObjectPair {
            public FBXNode Node;
            public FBXObject? Object;
            public ObjectPair(FBXNode node, FBXObject? obj) {
                Node = node;
                Object = obj;
            }
        }
        public class FBXScene {
            public List<FBXNode> Nodes = new();
            public List<Connection> Connections = new();
            public Dictionary<string, ulong> NameToId = new();
            //public List<TakeInfo> Takes = new();
            //public Dictionary<ulong, ObjectPair> ObjectMap = new();
            //public List<FBXMesh> Meshes = new();

            public FBXNode? FindNode(string id) {
                foreach (var child in Nodes) if (child.Id == id) return child;
                return default;
            }

            public ulong RequireId(Span<byte> data, Property prop) {
                if (prop.ValueEquals(data, "Scene"u8)) return 0;
                if (prop.Type == Property.Types.LONG) return prop.AsU64(data);
                if (prop.Type == Property.Types.STRING) {
                    var key = prop.AsString(data);
                    if (!NameToId.TryGetValue(key, out var id)) {
                        NameToId.Add(key, id = (ulong)NameToId.Count + 10000000000);
                    }
                    return id;
                    /*var fbxObjects = FindNode("Objects");
                    foreach (var fbxObj in fbxObjects.Children) {
                        if (fbxObj.Properties[0].ValueEquals(data, prop.AsSpan(data))) {
                            return fbxObj.Properties[0].AsU64(data);
                        }
                    }*/
                }
                return 0;
            }
        }

        public readonly byte[] Data;
        private static byte[] gLastData;

        public FBXParser(string filename) {
            Data = File.ReadAllBytes(filename);
            gLastData = Data;
        }

        public FBXScene Parse() {
            ReadOnlySpan<byte> binaryFBXHeader = "Kaydara FBX Binary"u8;
            FBXScene scene;
            var range = new RangeInt(0, Data.Length);
            if (Data.AsSpan().StartsWith(binaryFBXHeader)) {
                scene = TokenizeBinary(ref range);
            } else {
                scene = TokenizeText(ref range);
            }
            ParseConnections(scene);
            return scene;
        }
        public void Print(FBXScene scene, StringBuilder output) {
            void WriteNode(FBXNode node, int depth, Span<byte> data) {
                output.Append(' ', depth);
                output.Append(node.Id);
                output.Append(": ");
                for (int i = 0; i < node.Properties.Count; i++) {
                    if (i > 0) output.Append(", ");
                    var prop = node.Properties[i];
                    switch (prop.Type) {
                        case Property.Types.INTEGER: output.Append(prop.AsI32(data)); break;
                        case Property.Types.STRING: output.Append(prop.AsString(data).Replace('\0', ':')); break;
                        case Property.Types.FLOAT: output.Append(prop.AsFloat(data)); break;
                        case Property.Types.DOUBLE: output.Append(prop.AsDouble(data)); break;
                    }
                }
                if (node.Properties.Count > 0) output.Append(" ");
                output.Append("{\n");
                foreach (var child in node.Children) {
                    WriteNode(child, depth + 1, data);
                }
                output.Append(' ', depth);
                output.Append("}\n");
            }
            foreach (var node in scene.Nodes) {
                WriteNode(node, 0, Data);
            }
        }

        struct Header {
            [InlineArray(21)] public struct MagicArray { public byte Value; }
            [InlineArray(2)] public struct ReservedArray { public byte Value; }
            public MagicArray Magic;
            public ReservedArray Reserved;
            public uint Version;
        }
        public ref struct ParseContext {
            public uint Version;
            public RangeInt Data;
        }

        #region Tokenizing

        unsafe private FBXScene TokenizeBinary(ref RangeInt range) {
            var header = new Header();
            for (int i = 0; i < 21; i++) header.Magic[i] = Read<byte>(ref range);
            for (int i = 0; i < 2; i++) header.Reserved[i] = Read<byte>(ref range);
            header.Version = Read<uint>(ref range);
            var context = new ParseContext() {
                Version = header.Version,
                Data = range,
            };
            var root = new FBXScene();
            while (true) {
                var child = ReadNodeBinary(ref context);
                if (child == null) break;
                root.Nodes.Add(child);
            }
            return root;
        }
        private Property ReadPropertyBinary(ref RangeInt data) {
            var property = new Property() { IsBinary = true, };
            property.Type = (Property.Types)Read<byte>(ref data);
            switch ((char)property.Type) {
                case 'S': {
                    var val = ReadLongString(ref data);
                    property.Value = val;
                }
                break;
                case 'Y': property.Value = Read(ref data, 2); break;
                case 'C': property.Value = Read(ref data, 1); break;
                case 'I': property.Value = Read(ref data, 4); break;
                case 'F': property.Value = Read(ref data, 4); break;
                case 'D': property.Value = Read(ref data, 8); break;
                case 'L': property.Value = Read(ref data, 8); break;
                case 'R': {
                    var begin = data;
                    var len = Read<uint>(ref data);
                    property.Value = new RangeInt(begin.Start, (int)len + 4);
                    Read(ref data, len);
                }
                break;
                case 'b':
                case 'c':
                case 'f':
                case 'd':
                case 'l':
                case 'i': {
                    var begin = data;
                    var length = Read<uint>(ref data);
                    var encoding = Read<uint>(ref data);
                    var comp_len = Read<uint>(ref data);
                    property.Value = new RangeInt(begin.Start, (int)comp_len + 4 * 3);
                    Read(ref data, comp_len);
                }
                break;
                default: throw new Exception($"Unknown property type {property.Type}");
            }
            return property;
        }

        private FBXNode? ReadNodeBinary(ref ParseContext context) {
            ref var data = ref context.Data;
            var version = context.Version;
            ulong end_offset = ReadOffset(ref data, version);
            if (end_offset == 0) return default;

            var prop_count = ReadOffset(ref data, version);
            var prop_length = ReadOffset(ref data, version);

            var id = ReadShortString(ref data);

            var node = new FBXNode(Encoding.ASCII.GetString(GetSpan(id)));
            node.Properties.EnsureCapacity((int)prop_count);
            for (int p = 0; p < (int)prop_count; ++p) {
                var prop = ReadPropertyBinary(ref data);
                node.Properties.Add(prop);
            }

            if (Data.Length - data.Length >= (int)end_offset) return node;

            int BLOCK_SENTINEL_LENGTH = version >= 7500 ? 25 : 13;

            while (Data.Length - data.Length < (int)end_offset - BLOCK_SENTINEL_LENGTH) {
                var child = ReadNodeBinary(ref context);
                if (child != null) node.Children.Add(child);
            }
            var sentinel = Read(ref data, (uint)BLOCK_SENTINEL_LENGTH);
            return node;
        }

        public FBXScene TokenizeText(ref RangeInt data) {
            var root = new FBXScene();
            while (data.Length > 0) {
                var head = Data[data.Start + 0];
                if (head == (byte)';' || head == (byte)'\r' || head == (byte)'\n') {
                    SkipLine(ref data);
                    SkipWhitespace(ref data);
                } else {
                    var node = ReadTextNode(ref data);
                    root.Nodes.Add(node);
                }
            }
            return root;
        }
        private FBXNode ReadTextNode(ref RangeInt data) {
            var id = ReadTextToken(ref data);
            Trace.Assert(Match(ref data, (byte)':'));
            SkipInsignificantWhitespace(ref data);
            var node = new FBXNode(Encoding.ASCII.GetString(GetSpan(id)));
            while (data.Length > 0 && !IsEndLine(Data[data.Start + 0]) && Data[data.Start + 0] != '{') {
                var prop = ReadTextProperty(ref data);
                node.Properties.Add(prop);
                if (data.Length > 0 && Data[data.Start + 0] == ',') {
                    Read(ref data, 1);
                    SkipWhitespace(ref data);
                }
                SkipInsignificantWhitespace(ref data);
            }
            if (Match(ref data, (byte)'{')) {
                SkipWhitespace(ref data);
                while (!Match(ref data, (byte)'}')) {
                    var child = ReadTextNode(ref data);
                    SkipWhitespace(ref data);
                    node.Children.Add(child);
                }
            }
            return node;
        }
        private Property ReadTextProperty(ref RangeInt data) {
            if (data.Length <= 0) return default;
            var prop = new Property() { IsBinary = false, };
            if (Match(ref data, (byte)'"')) {
                prop.Type = Property.Types.STRING;
                prop.Value = ReadUntil(ref data, (byte)'"');
                return prop;
            }
            var firstChar = (char)Data[data.Start + 0];
            if ((char.IsNumber(firstChar) || firstChar == (byte)'-')) {
                int len = 0;
                prop.Type = Property.Types.LONG;
                if (Data[data.Start + len] == '-') ++len;
                for (; char.IsNumber((char)Data[data.Start + len]); ++len) ;
                if (Data[data.Start + len] == '.') {
                    for (++len; char.IsNumber((char)Data[data.Start + len]); ++len) ;
                    if (Data[data.Start + len] == 'e' || Data[data.Start + len] == 'E') {
                        for (++len; char.IsNumber((char)Data[data.Start + len]); ++len) ;
                    }
                }
                prop.Value = Read(ref data, (uint)len);
                return prop;
            }
            if (firstChar == 'T' || firstChar == 'Y' || firstChar == 'W' || firstChar == 'C') {
                prop.Type = (Property.Types)firstChar;
                prop.Value = Read(ref data, 1);
                return prop;
            }
            if (firstChar == ',') {
                prop.Type = Property.Types.VOID;
                prop.Value = default;
                return prop;
            }
            if (Match(ref data, (byte)'*')) {
                prop.Type = Property.Types.ARRAY_LONG;
                ReadUntil(ref data, (byte)':');
                Match(ref data, (byte)':');
                SkipInsignificantWhitespace(ref data);
                prop.Value = ReadUntil(ref data, (byte)'}');
                Match(ref data, (byte)'}');
                return prop;
            }
            throw new Exception("Failed to parse");
        }
        private static bool IsEndLine(byte chr) { return chr == '\r' || chr == '\n'; }
        private RangeInt ReadTextToken(ref RangeInt data) {
            int len = 0;
            while (len < data.Length && char.IsLetterOrDigit((char)Data[data.Start + len])) ++len;
            return Read(ref data, (uint)len);
        }
        private RangeInt SkipLine(ref RangeInt data) {
            int len = 0;
            while (len < data.Length && (Data[data.Start + len] != '\r' && Data[data.Start + len] != '\n')) ++len;
            return Read(ref data, (uint)len);
        }
        private RangeInt SkipWhitespace(ref RangeInt data) {
            int len = 0;
            while (len < data.Length && char.IsWhiteSpace((char)Data[data.Start + len])) ++len;
            return Read(ref data, (uint)len);
        }
        private RangeInt SkipInsignificantWhitespace(ref RangeInt data) {
            int len = 0;
            while (len < data.Length && char.IsWhiteSpace((char)Data[data.Start + len]) && !IsEndLine(Data[data.Start + len])) ++len;
            return Read(ref data, (uint)len);
        }

        public ReadOnlySpan<byte> GetSpan(RangeInt data) {
            return Data.AsSpan(data.Start, data.Length);
        }
        unsafe bool Match(ref RangeInt data, ReadOnlySpan<byte> needle) {
            if (!GetSpan(data).StartsWith(needle)) return false;
            data.Start += needle.Length;
            data.Length -= needle.Length;
            return true;
        }
        unsafe bool Match(ref RangeInt data, byte needle) {
            if (data.Length < 1 || Data[data.Start] != needle) return false;
            data.Start++;
            data.Length--;
            return true;
        }
        unsafe T Read<T>(ref RangeInt data) where T : unmanaged {
            var item = MemoryMarshal.Read<T>(Data.AsSpan(data.Start));
            data.Start += sizeof(T);
            data.Length -= sizeof(T);
            return item;
        }
        unsafe RangeInt ReadUntil(ref RangeInt data, byte end) {
            int len = 0;
            while (len < data.Length && Data[data.Start + len] != end) ++len;
            return Read(ref data, (uint)len);
        }
        private RangeInt Read(ref RangeInt data, ulong size) {
            var outData = new RangeInt(data.Start, checked((int)size));
            data.Start += (int)size;
            data.Length -= (int)size;
            return outData;
        }
        private ulong ReadOffset(ref RangeInt data, uint version) {
            return version >= 7500 ? Read<ulong>(ref data) : Read<uint>(ref data);
        }
        private RangeInt ReadShortString(ref RangeInt data) {
            var len = Read<byte>(ref data);
            return Read(ref data, len);
        }
        private RangeInt ReadLongString(ref RangeInt data) {
            var len = Read<uint>(ref data);
            return Read(ref data, len);
        }

        #endregion

        #region Parsing

        /*private void Parse(FBXScene root) {
            ParseConnections(root);
            ParseTakes(root);
            //ParseObjects(root);
        }*/

        private static bool isString(Property prop) { return prop.Type == Property.Types.STRING; }
        private static bool isLong(Property prop) { return prop.Type == Property.Types.LONG; }

        private void ParseConnections(FBXScene scene) {
            var connections = scene.FindNode("Connections");
            if (connections == null) return;
            foreach (var child in connections.Children) {
                if (!isString(child.Properties[0]) || child.Properties.Count < 3) {
                    throw new Exception("Invalid connection");
                }
                Connection c = new();
                if (child.Properties[0].ValueEquals(Data, "OO"u8)) {
                    c.Type = Connection.Types.OBJECT_OBJECT;
                    c.From = ToObjectId(scene, child.Properties[1]);
                    c.To = ToObjectId(scene, child.Properties[2]);
                } else if (child.Properties[0].ValueEquals(Data, "OP"u8)) {
                    c.Type = Connection.Types.OBJECT_PROPERTY;
                    c.From = ToObjectId(scene, child.Properties[1]);
                    c.To = ToObjectId(scene, child.Properties[2]);
                    c.ToProperty = child.Properties[3].Value;
                } else if (child.Properties[0].ValueEquals(Data, "PO"u8)) {
                    c.Type = Connection.Types.PROPERTY_OBJECT;
                    c.From = ToObjectId(scene, child.Properties[1]);
                    c.FromProperty = child.Properties[2].Value;
                    c.To = ToObjectId(scene, child.Properties[3]);
                } else if (child.Properties[0].ValueEquals(Data, "PP"u8)) {
                    c.Type = Connection.Types.PROPERTY_PROPERTY;
                    c.From = ToObjectId(scene, child.Properties[1]);
                    c.FromProperty = child.Properties[2].Value;
                    c.To = ToObjectId(scene, child.Properties[3]);
                    c.ToProperty = child.Properties[4].Value;
                } else {
                    throw new Exception("Not supported");
                }
                scene.Connections.Add(c);
            }
        }
        private ulong ToObjectId(FBXScene scene, Property prop) {
            return scene.RequireId(Data, prop);
        }
        /*private void ParseTakes(FBXScene scene) {
            var takes = scene.FindNode("Takes");
            if (takes == null) return;
            foreach (var child in takes.Children) {
                if (!isString(child.Properties[0])) throw new Exception("Invalid name in take");

                TakeInfo take = new();
                take.Name = child.Properties[0].AsString(Data);
                var filename = child.FindChild("FileName");
                if (filename != null) {
                    if (!isString(filename.Properties[0])) throw new Exception("Invalid filename in take");
                    take.Filename = filename.Properties[0].AsString(Data);
                }
                var local_time = child.FindChild("LocalTime");
                if (local_time != null) {
                    if (!isLong(local_time.Properties[0]) || !isLong(local_time.Properties[1]))
                        throw new Exception("Invalid local time in take");
                    take.local_time_from = local_time.Properties[0].AsTime(Data);
                    take.local_time_to = local_time.Properties[1].AsTime(Data);
                }
                var reference_time = child.FindChild("ReferenceTime");
                if (reference_time != null) {
                    if (!isLong(reference_time.Properties[0]) || !isLong(reference_time.Properties[1]))
                        throw new Exception("Invalid reference time in take");
                    take.reference_time_from = reference_time.Properties[0].AsI64(Data);
                    take.reference_time_to = reference_time.Properties[1].AsI64(Data);
                }
                scene.Takes.Add(take);
            }
        }*/
        public string GetNodeName(FBXNode node) {
            int id = node.Properties.Count >= 1 && node.Properties[0].Type == Property.Types.STRING ? 0
                : node.Properties.Count >= 2 && node.Properties[1].Type == Property.Types.STRING ? 1 : -1;
            return id >= 0 ? node.Properties[id].AsStringNull(Data) : "";
        }
        /*private void ParseObjects(FBXScene scene) {
            var fbxObjs = scene.FindNode("Objects");
            if (fbxObjs == null) return;
            foreach (var fbxObj in fbxObjs.Children) {
                var id = fbxObj.Properties[0].AsU64(Data);
                FBXObject outObj = null;
                if (fbxObj.Id == "Geometry") {
                    var fbxLastProp = fbxObj.Properties[^1];
                    if (fbxLastProp.ValueEquals(Data, "Mesh"u8)) {
                        outObj = ParseGeometryMesh(fbxObj);
                    }
                } else if (fbxObj.Id == "Material") {
                    outObj = ParseMaterial(fbxObj);
                } else if (fbxObj.Id == "Model") {
                    var fbxClassProp = fbxObj.Properties[2];
                    if (fbxClassProp.ValueEquals(Data, "Mesh"u8)) {
                        var name = GetNodeName(fbxObj);
                        scene.Meshes.Add(new FBXMesh(name));
                    }
                    outObj = ParseMaterial(fbxObj);
                } else if (fbxObj.Id == "Texture") {
                    var tex = new FBXTexture(GetNodeName(fbxObj));
                    foreach (var fbxTexChild in fbxObj.Children) {
                        if (fbxTexChild.Id == "FileName") tex.Filename = fbxTexChild.Properties[0].AsString(Data);
                        if (fbxTexChild.Id == "RelativeFilename") tex.RelativeFilename = fbxTexChild.Properties[0].AsString(Data);
                        if (fbxTexChild.Id == "Media") tex.Media = fbxTexChild.Properties[0].AsSpan(Data).ToArray();
                    }
                    outObj = tex;
                }
                scene.ObjectMap[id] = new ObjectPair(fbxObj, outObj);
            }
        }*/

        public T[] ParseTypedBuffer<T>(FBXNode node, string name) where T : unmanaged {
            var fbxItems = node.FindChild(name);
            if (fbxItems == null || fbxItems.Properties.Count == 0) return null;
            if (!ParseTypedArray<T>(fbxItems.Properties[0], out var values)) throw new Exception("Failed to parse values");
            return values;
        }
        public Vector3[] ParseGeometryVertices(FBXNode node) {
            return ParseTypedBuffer<Vector3>(node, "Vertices");
        }
        public struct VertexData<T> {
            public enum Mappings { Uniform, Vertex, PolyVert, Polygon, };
            public Mappings Mapping;
            public T[] Data;
            public bool IsValid => Data != null;
            public bool HasPerPolyData => IsValid && Mapping > Mappings.Vertex;
            public override string ToString() { return $"{Data?.Length ?? 0} {Mapping}"; }
        }
        public VertexData<T> ParseVertexData<T>(FBXNode node, string layerName, string dataName, string indexName) where T : unmanaged {
            var fbxLayer = node.FindChild(layerName);
            if (fbxLayer == null) return default;
            var fbxData = fbxLayer.FindChild(dataName);
            var fbxMapping = fbxLayer.FindChild("MappingInformationType");
            var fbxReference = fbxLayer.FindChild("ReferenceInformationType");
            var data = new VertexData<T>();
            ParseTypedArray<T>(fbxData.Properties[0], out data.Data);
            if (fbxReference != null) {
                if (fbxReference.Properties[0].ValueEquals(Data, "IndexToDirect"u8)) {
                    if (indexName != null) {
                        var fbxIndices = fbxLayer.FindChild(indexName);
                        ParseTypedArray<int>(fbxIndices.Properties[0], out var indices);
                        var newData = new T[indices.Length];
                        for (int i = 0; i < indices.Length; i++) {
                            newData[i] = data.Data[indices[i]];
                        }
                        data.Data = newData;
                    } else {
                        // Mesh material ids use this, but do not have an indirection array
                    }
                } else if (!fbxReference.Properties[0].ValueEquals(Data, "Direct"u8)) {
                    return default;
                }
            }
            if (fbxMapping != null) {
                if (fbxMapping.Properties[0].ValueEquals(Data, "ByPolygonVertex"u8)) {
                    data.Mapping = VertexData<T>.Mappings.PolyVert;
                } else if (fbxMapping.Properties[0].ValueEquals(Data, "ByPolygon"u8)) {
                    data.Mapping = VertexData<T>.Mappings.Polygon;
                } else if (fbxMapping.Properties[0].ValueEquals(Data, "ByVertice"u8) || fbxMapping.Properties[0].ValueEquals(Data, "ByVertex"u8)) {
                    data.Mapping = VertexData<T>.Mappings.Vertex;
                } else if (fbxMapping.Properties[0].ValueEquals(Data, "AllSame"u8)) {
                    data.Mapping = VertexData<T>.Mappings.Uniform;
                } else throw new NotImplementedException();
            }
            return data;
        }
        public int[] ParseGeometryIndices(FBXNode node) {
            return ParseTypedBuffer<int>(node, "PolygonVertexIndex");
        }
        private FBXMesh ParseGeometryMesh(FBXNode node) {
            var name = GetNodeName(node);
            var vertices = ParseGeometryVertices(node);
            var indices = ParseGeometryIndices(node);

            return new FBXMesh(name) {
                Indices = indices,
                Positions = vertices,
            };
        }
        private FBXObject ParseMaterial(FBXNode node) {
            var name = GetNodeName(node);
            var material = new FBXMaterial(name);
            var properties = node.FindChild("Properties70");
            foreach (var prop in properties.Children) {
                Vector3 value = Vector3.Zero;
                /*for (int i = 4; i < prop.Properties.Count; i++) {
                    value[i - 4] = (float)prop.Properties[i].AsDouble();
                }*/
                material.Properties.Add(
                    prop.Properties[0].AsStringNull(Data),
                    value
                );
            }
            return material;
        }

        unsafe public bool ParseTypedArray<T>(Property property, out T[] out_data) where T : unmanaged {
            if (property.IsBinary) {
                var count = property.AsI32(Data);
                var converter = new DataConverter(typeof(T), property.Type);
                out_data = new T[count / converter.destElCount];

                if (count == 0) return true;
                return ParseBinaryArrayRaw(property, out_data.AsSpan());
            } else {
                out_data = Array.Empty<T>();
                ReadOnlySpan<byte> data = GetSpan(property.Value);
                int count = 0;
                while (data.Length > 0) {
                    count = (int)BitOperations.RoundUpToPowerOf2((uint)count + 4);
                    if (count > out_data.Length) Array.Resize(ref out_data, count);
                    count += ParseTextArrayRaw<T>(ref data, out_data.AsSpan(count));
                }
                Array.Resize(ref out_data, count);
                return true;
            }
        }

        unsafe public bool ParseBinaryArrayRaw<T>(Property property, Span<T> out_data) where T : unmanaged {
            Debug.Assert(property.IsBinary);
            Debug.Assert(out_data != null);

            var outData = MemoryMarshal.Cast<T, byte>(out_data);

            var count = MemoryMarshal.Read<uint>(GetSpan(property.Value));
            var enc = MemoryMarshal.Read<uint>(GetSpan(property.Value).Slice(4));
            var len = MemoryMarshal.Read<uint>(GetSpan(property.Value).Slice(8));

            var converter = new DataConverter(typeof(T), property.Type);

            Debug.Assert((int)count == out_data.Length * converter.destElCount);
            if (enc == 0) {
                Debug.Assert((int)len == (int)count * converter.srcElSize);
                if (converter.IsPassthrough) {
                    GetSpan(property.Value).Slice(12, sizeof(T) * out_data.Length).CopyTo(MemoryMarshal.Cast<T, byte>(out_data));
                } else {
                    fixed (void* dstDataPtr = out_data, srcDataPtr = GetSpan(property.Value).Slice(12)) {
                        converter.converter(dstDataPtr, srcDataPtr, out_data.Length * converter.destElCount);
                    }
                }
            } else if (enc == 1) {
                var inStream = new MemoryStream(GetSpan(property.Value).ToArray(), 12, (int)len);
                var stream = new ZLibStream(inStream, CompressionMode.Decompress);

                if (converter.IsPassthrough) {
                    int read = 0;
                    for (; stream.CanRead && read < outData.Length;)
                        read += stream.Read(outData.Slice(read));
                    Debug.Assert(read == count * converter.srcElSize);
                } else {
                    var outStream = new MemoryStream();
                    stream.CopyTo(outStream);
                    Debug.Assert(outStream.Length == count * converter.srcElSize);
                    var rawData = outStream.GetBuffer();
                    fixed (void* dstDataPtr = out_data, srcDataPtr = rawData) {
                        converter.converter(dstDataPtr, srcDataPtr, out_data.Length * converter.destElCount);
                    }
                }
            }
            return true;
        }
        unsafe struct DataConverter {
            public delegate*<void*, void*, int, void> converter = null;
            public int srcElSize = 1;
            public int destElCount = 1;
            public int destElSize = 1;
            public bool IsPassthrough => converter == null;

            public DataConverter(Type destType, Property.Types srcType) {
                if (destType == typeof(Vector2)) { destType = typeof(float); destElCount = 2; }
                if (destType == typeof(Vector3)) { destType = typeof(float); destElCount = 3; }
                if (destType == typeof(Vector4)) { destType = typeof(float); destElCount = 4; }
                if (destType == typeof(Matrix4x4)) { destType = typeof(float); destElCount = 16; }
                destElSize =
                    destType == typeof(double) || destType == typeof(long) || destType == typeof(ulong) ? 8
                    : destType == typeof(float) || destType == typeof(int) || destType == typeof(uint) ? 4
                    : 0;
                switch (srcType) {
                    case Property.Types.ARRAY_LONG: {
                        srcElSize = 8;
                        if (destType == typeof(long) || destType == typeof(ulong)) converter = null;
                        else if (destType == typeof(int) || destType == typeof(uint)) converter = &ConvertLongToInt;
                        else throw new NotImplementedException();
                    }
                    break;
                    case Property.Types.ARRAY_DOUBLE: {
                        srcElSize = 8;
                        if (destType == typeof(double)) converter = null;
                        else if (destType == typeof(float)) converter = &ConvertDblToFloat;
                        else throw new NotImplementedException();
                    }
                    break;
                    case Property.Types.ARRAY_FLOAT: {
                        srcElSize = 4;
                        if (destType == typeof(float)) converter = null;
                        else throw new NotImplementedException();
                    }
                    break;
                    case Property.Types.ARRAY_INT: {
                        srcElSize = 4;
                        if (destType == typeof(int)) converter = null;
                        else throw new NotImplementedException();
                    }
                    break;
                    default: throw new NotImplementedException();
                }
            }
        }
        unsafe static void ConvertLongToInt(void* dest, void* src, int count) {
            for (int i = 0; i < count; i++) ((int*)dest)[i] = (int)((long*)src)[i];
        }
        unsafe static void ConvertDblToFloat(void* dest, void* src, int count) {
            for (int i = 0; i < count; i++) ((float*)dest)[i] = (float)((double*)src)[i];
        }
        static int ParseTextArrayRaw<T>(ref ReadOnlySpan<byte> data, Span<T> out_data) where T : unmanaged {
            if (typeof(T) == typeof(float) || typeof(T) == typeof(Vector2)
                || typeof(T) == typeof(Vector3) || typeof(T) == typeof(Vector4)
                || typeof(T) == typeof(Matrix4x4)) {
                return parseTextArrayRaw(ref data, MemoryMarshal.Cast<T, float>(out_data));
            } else if (typeof(T) == typeof(int)) {
                return parseTextArrayRaw(ref data, MemoryMarshal.Cast<T, int>(out_data));
            } else if (typeof(T) == typeof(uint)) {
                return parseTextArrayRaw(ref data, MemoryMarshal.Cast<T, uint>(out_data));
            } else if (typeof(T) == typeof(long)) {
                return parseTextArrayRaw(ref data, MemoryMarshal.Cast<T, long>(out_data));
            } else if (typeof(T) == typeof(ulong)) {
                return parseTextArrayRaw(ref data, MemoryMarshal.Cast<T, ulong>(out_data));
            }
            return 0;
        }
        static int parseTextArrayRaw(ref ReadOnlySpan<byte> data, Span<float> out_raw) {
            for (int i = 0; ; i++) {
                if (data.Length == 0 || i >= out_raw.Length) return i;
                int l = 0; while (l < data.Length && data[l] != ',') ++l;
                out_raw[i] = (float)double.Parse(data.Slice(0, l));
                while (l < data.Length && data[l] == ',') ++l;
                data = data.Slice(l);
            }
        }
        static int parseTextArrayRaw(ref ReadOnlySpan<byte> data, Span<int> out_raw) {
            for (int i = 0; ; i++) {
                if (data.Length == 0 || i >= out_raw.Length) return i;
                int l = 0; while (l < data.Length && data[l] != ',') ++l;
                out_raw[i] = int.Parse(data.Slice(0, l));
                while (l < data.Length && data[l] == ',') ++l;
                data = data.Slice(l);
            }
        }
        static int parseTextArrayRaw(ref ReadOnlySpan<byte> data, Span<uint> out_raw) {
            for (int i = 0; ; i++) {
                if (data.Length == 0 || i >= out_raw.Length) return i;
                int l = 0; while (l < data.Length && data[l] != ',') ++l;
                out_raw[i] = uint.Parse(data.Slice(0, l));
                while (l < data.Length && data[l] == ',') ++l;
                data = data.Slice(l);
            }
        }
        static int parseTextArrayRaw(ref ReadOnlySpan<byte> data, Span<long> out_raw) {
            for (int i = 0; ; i++) {
                if (data.Length == 0 || i >= out_raw.Length) return i;
                int l = 0; while (l < data.Length && data[l] != ',') ++l;
                out_raw[i] = long.Parse(data.Slice(0, l));
                while (l < data.Length && data[l] == ',') ++l;
                data = data.Slice(l);
            }
        }
        static int parseTextArrayRaw(ref ReadOnlySpan<byte> data, Span<ulong> out_raw) {
            for (int i = 0; ; i++) {
                if (data.Length == 0 || i >= out_raw.Length) return i;
                int l = 0; while (l < data.Length && data[l] != ',') ++l;
                out_raw[i] = ulong.Parse(data.Slice(0, l));
                while (l < data.Length && data[l] == ',') ++l;
                data = data.Slice(l);
            }
        }

        #endregion

    }
}
