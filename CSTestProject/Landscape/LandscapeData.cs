using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.Engine.Profiling;
using Weesals.Geometry;
using Weesals.Utility;

namespace Weesals.Landscape {
    public interface ILandscapeReader {
        public LandscapeData.SizingData Sizing { get; }
    }
    public interface IHeightmapReader : ILandscapeReader {
        public int GetHeightAt(Int2 pnt);
    }
    public interface IValidCellReader : ILandscapeReader {
        public bool GetIsCellValid(Int2 pnt);
    }
    public interface IControlMapReader : ILandscapeReader {
    }

    // Contains data to determine what was changed in the terrain
    public struct LandscapeChangeEvent {
        public RectI Range;
        public bool HeightMapChanged;
        public bool ControlMapChanged;
        public bool WaterMapChanged;
        public LandscapeChangeEvent(RectI range, bool heightMap = false, bool controlMap = false, bool waterMap = false) {
            Range = range;
            HeightMapChanged = heightMap;
            ControlMapChanged = controlMap;
            WaterMapChanged = waterMap;
        }
        public LandscapeChangeEvent(Int2 min, Int2 max, bool heightMap = false, bool controlMap = false, bool waterMap = false) {
            if (min.X <= max.X || min.Y <= max.Y) {
                Range = new RectI(min.X, min.Y, max.X - min.X + 1, max.Y - min.Y + 1);
                HeightMapChanged = heightMap;
                ControlMapChanged = controlMap;
                WaterMapChanged = waterMap;
            } else {
                this = MakeNone();
            }
        }
        public bool HasChanges => HeightMapChanged | ControlMapChanged | WaterMapChanged;

        // Expand this to include the passed in range/flags
        public void CombineWith(in LandscapeChangeEvent other) {
            // If this is the first change, just use it as is.
            if (!HasChanges) { this = other; return; }
            if (!other.HasChanges) return;
            // Otherwise inflate our range and integrate changed flags
            var min = Int2.Min(Range.Min, other.Range.Min);
            var max = Int2.Max(Range.Max, other.Range.Max);
            Range = new RectI(min.X, min.Y, max.X - min.X, max.Y - min.Y);
            HeightMapChanged |= other.HeightMapChanged;
            ControlMapChanged |= other.ControlMapChanged;
            WaterMapChanged |= other.WaterMapChanged;
        }
        // Create a changed event that covers everything for the entire terrain
        public static LandscapeChangeEvent MakeAll(Int2 size) {
            return new LandscapeChangeEvent(new RectI(0, 0, size.X, size.Y), true, true, true);
        }
        // Create a changed event that includes nothing
        public static LandscapeChangeEvent MakeNone() {
            return new LandscapeChangeEvent(default, false, false, false);
        }
    }

    // A raycast result
    public struct LandscapeHit {
        public Vector3 HitPosition;
    }

    // A terrain
    public class LandscapeData {

        public const int SerialVersion = 1;
        // How many units of Height is 1 unit in world space
        public const int HeightScale = 512;

        // Hold data related to the position/size of the landscape in the world
        public struct SizingData {
            // Location of the terrain 0,0 coord (minimum corner)
            public Vector3 Location;
            // How many cells the terrain contains
            public Int2 Size;
            public Int2 SimulationSize => Size * Scale1024;
            // The size of each cell in world-units/1024 (roughly mm)
            public int Scale1024;
            public SizingData(Int2 size, int scale1024 = 1024) {
                Size = size;
                Scale1024 = scale1024;
            }

            // Get the index of a specified cell coordinate
            public int ToIndex(Int2 pnt) { return pnt.X + pnt.Y * Size.X; }
            // Get the cell for the specified index
            public Int2 FromIndex(int index) { return new Int2(index % Size.X, index / Size.X); }

            // Check if a point exists inside of the terrain
            public bool IsInBounds(Int2 pnt) { return (uint)pnt.X < (uint)Size.X && (uint)pnt.Y < (uint)Size.Y; }

            // Convert from world to local (cell) space
            public Int2 WorldToLandscape(Vector3 worldPos) { return Int2.RoundToInt((worldPos - Location).toxz() * (1024f / Scale1024)); }
            public Int2 WorldToLandscape(Vector2 worldPos) { return Int2.RoundToInt((worldPos - Location.toxz()) * (1024f / Scale1024)); }
            public Vector3 LandscapeToWorld(Int2 landscapePos) { return (((Vector2)landscapePos) * (Scale1024 / 1024f)).AppendY(0.0f) + Location; }

            public Int2 LandscapeToSimulation(Int2 pnt) { return pnt * Scale1024; }
            public Int2 SimulationToLandscape(Int2 pnt) { return pnt / Scale1024; }

            public Int2 WorldToLandscape(Vector2 worldPos, out Vector2 outLerp) {
                worldPos -= Location.toxz();
                worldPos *= 1024f / Scale1024;
                var pnt = Int2.FloorToInt(worldPos);
                outLerp = worldPos - (Vector2)pnt;
                return pnt;
            }
        }

        // Data used by the vertex shader to position a vertex (ie. its height)
        public struct HeightCell {
            public short Height;
            public float HeightF => (float)Height / (float)HeightScale;
            public override string ToString() { return Height.ToString(); }
            public static HeightCell Default = new();
            public static HeightCell Invalid = new() { Height = short.MinValue };
        }
        // Data used by the fragment shader to determine shading (ie. texture)
        public struct ControlCell {
            public byte TypeId;
            public override string ToString() { return TypeId.ToString(); }
            public static ControlCell Default = new();
        }
        // Data used to render water over the terrain
        public struct WaterCell {
            public byte Data;
            public bool IsInvalid => Data == 0;
            public bool IsValid => Data != 0;
            public short Height {
                get => DataToHeight(Data);
                set => Data = HeightToData(value);
            }
            public override string ToString() { return Data.ToString(); }
            public static short DataToHeight(int data) { return (short)((data - 127) << 6); }
            public static byte HeightToData(int height) { return (byte)Math.Clamp((height >> 6) + 127, 0, 255); }
            public static WaterCell Default = new();
        }

        // Simplified data access API
        public struct DataReader<CellType> where CellType : unmanaged {
            public SizingData mSizing;
            public CellType[] mCells;
            public Int2 Size => mSizing.Size;
            public DataReader(in SizingData sizing, CellType[] cells) {
                mSizing = sizing;
                mCells = cells;
            }
            public ref CellType this[int p] => ref mCells[p];
            public ref CellType this[Int2 p] => ref mCells[mSizing.ToIndex(p)];
        }
        public struct HeightMapReadOnly : IHeightmapReader {
            internal DataReader<HeightCell> Reader;
            public Int2 Size => Reader.Size;
            public SizingData Sizing => Reader.mSizing;
            internal HeightMapReadOnly(SizingData sizing, HeightCell[] cells) {
                Reader = new DataReader<HeightCell>(sizing, cells);
            }
            public ref HeightCell this[int p] => ref Reader[p];
            public ref HeightCell this[Int2 p] => ref Reader[p];
            public int GetHeightAt(Int2 pnt) {
                if ((uint)pnt.X >= Reader.Size.X || (uint)pnt.Y >= Reader.Size.Y) return 0;
                return Reader[pnt].Height;
            }
        }
        public struct ControlMapReadOnly : IControlMapReader {
            internal DataReader<ControlCell> Reader;
            public Int2 Size => Reader.Size;
            public SizingData Sizing => Reader.mSizing;
            internal ControlMapReadOnly(SizingData sizing, ControlCell[] cells) {
                Reader = new DataReader<ControlCell>(sizing, cells);
            }
            public ref ControlCell this[int p] => ref Reader[p];
            public ref ControlCell this[Int2 p] => ref Reader[p];
            public byte GetTypeAt(Int2 p) => this[p].TypeId;
        }
        public struct WaterMapReadOnly : IHeightmapReader, IValidCellReader {
            internal DataReader<WaterCell> Reader;
            public bool IsValid => Reader.mCells != null;
            public Int2 Size => Reader.Size;
            public SizingData Sizing => Reader.mSizing;
            internal WaterMapReadOnly(SizingData sizing, WaterCell[] cells) {
                Reader = new DataReader<WaterCell>(sizing, cells);
            }
            public ref WaterCell this[int p] => ref Reader[p];
            public ref WaterCell this[Int2 p] => ref Reader[p];
            public int GetHeightAt(Int2 pnt) {
                if ((uint)pnt.X >= Reader.Size.X || (uint)pnt.Y >= Reader.Size.Y) return 0;
                return Reader[pnt].Height;
            }
            public bool GetIsCellValid(Int2 pnt) {
                return Reader[pnt].IsValid;
            }
        }

        SizingData mSizing = new SizingData(0);
        public LandscapeLayerCollection Layers { get; private set; }
        HeightCell[] mHeightMap = Array.Empty<HeightCell>();
        ControlCell[] mControlMap = Array.Empty<ControlCell>();
        WaterCell[]? mWaterMap;

        // Used to track if the landscape has changed since last
        int mRevision = 0;

        // Listen for changes to the landscape data
        //std::vector<ChangeCallback> mChangeListeners;
        public event Action<LandscapeData, LandscapeChangeEvent> OnLandscapeChanged;

        public LandscapeData() { }

        public SizingData GetSizing() { return mSizing; }
        public Int2 GetSize() { return mSizing.Size; }
        public float GetScale() { return (float)mSizing.Scale1024 / 1024.0f; }
        public Int2 Size => GetSize();
        public SizingData Sizing => GetSizing();

        public int Revision => mRevision;
        public bool WaterEnabled => mWaterMap != null;

        public void Initialise(LandscapeLayerCollection layers) {
            Layers = layers;
        }
        public void Initialise(Int2 size, LandscapeLayerCollection layers) {
            Layers = layers;
            SetSize(size);
        }

        public void SetLocation(Vector3 location) {
            mSizing.Location = location;
        }
        public void SetSize(Int2 size, bool initValues = true) {
            mSizing.Size = size;
            int cellCount = mSizing.Size.X * mSizing.Size.Y;
            if (mHeightMap.Length != cellCount)
                mHeightMap = new HeightCell[cellCount];
            if (mControlMap.Length != cellCount)
                mControlMap = new ControlCell[cellCount];
            if (mWaterMap != null && mWaterMap.Length != cellCount)
                mWaterMap = new WaterCell[cellCount];
            if (initValues) {
                Array.Fill(mHeightMap, HeightCell.Default);
                Array.Fill(mControlMap, ControlCell.Default);
                if (mWaterMap != null) Array.Fill(mWaterMap, WaterCell.Default);
            }
        }
        public void SetScale(int scale1024) {
            mSizing.Scale1024 = scale1024;
        }
        public void SetWaterEnabled(bool enable) {
            if (WaterEnabled == enable) return;
            // Resize water to either match heightmap or be erased
            mWaterMap = enable ? new WaterCell[mHeightMap.Length] : null;
        }

        public void Resize(Int2 size) {
            if (mSizing.Size == size) return;
            var sizing = mSizing;
            var heightMap = mHeightMap;
            var controlMap = mControlMap;
            var waterMap = mWaterMap;
            SetSize(size, true);
            var minSize = Int2.Min(mSizing.Size, sizing.Size);
            for (int y = 0; y < minSize.Y; y++) {
                var i0 = y * sizing.Size.X;
                var i1 = y * mSizing.Size.X;
                var iL = minSize.X;
                heightMap.AsSpan(i0, iL).CopyTo(mHeightMap.AsSpan(i1));
                controlMap.AsSpan(i0, iL).CopyTo(mControlMap.AsSpan(i1));
                if (waterMap != null)
                    waterMap.AsSpan(i0, iL).CopyTo(mWaterMap.AsSpan(i1));
            }
            NotifyLandscapeChanged(LandscapeChangeEvent.MakeAll(mSizing.Size));
        }

        public void NotifyLandscapeChanged() {
            NotifyLandscapeChanged(LandscapeChangeEvent.MakeAll(GetSize()));
        }
        public void NotifyLandscapeChanged(in LandscapeChangeEvent changeEvent) {
            ++mRevision;
            if (OnLandscapeChanged != null)
                OnLandscapeChanged(this, changeEvent);
        }

        // Helper accessors
        public HeightMapReadOnly GetHeightMap() {
            return new HeightMapReadOnly(mSizing, mHeightMap);
        }
        public ControlMapReadOnly GetControlMap() {
            return new ControlMapReadOnly(mSizing, mControlMap);
        }
        public WaterMapReadOnly GetWaterMap() {
            return new WaterMapReadOnly(mSizing, mWaterMap);
        }

        // Get the raw data of the landscape; MUST manually call
        // NotifyLandscapeChanged with changed range afterwards
        public HeightCell[] GetRawHeightMap() { return mHeightMap; }
        public ControlCell[] GetRawControlMap() { return mControlMap; }
        public WaterCell[]? GetRawWaterMap() { return mWaterMap; }

        public bool Raycast(in Ray ray, out LandscapeHit hit, float maxDst = float.MaxValue) {
            // Easier to work in "local" space (only translation, not scale)
            // TODO: Also apply scaling (including HeightScale) to work with native units
            var localRay = new Ray(ray.Origin - mSizing.Location, ray.Direction);
            var from = localRay.Origin.toxz();
            var dir = localRay.Direction.toxz();
            var terScale = (float)mSizing.Scale1024 / 1024.0f;
            var maxExtents = ((Vector2)mSizing.Size) * terScale;
            var dirSign = new Int2(dir.X < 0.0f ? -1 : 1, dir.Y < 0.0f ? -1 : 1);
            var dirEdge = new Int2(dir.X < 0.0f ? 0 : 1, dir.Y < 0.0f ? 0 : 1);
            // Move the ray forward until it is within the landscape range
            float dst = 0.0f;
            if (dir.X != 0.0f) dst = Math.Max(dst, (maxExtents.X * (1 - dirEdge.X) - from.X) / dir.X);
            if (dir.Y != 0.0f) dst = Math.Max(dst, (maxExtents.Y * (1 - dirEdge.Y) - from.Y) / dir.Y);
            var fromC = Int2.Clamp((Int2)((from + dir * dst) / terScale), 0, mSizing.Size - 2);
            while (dst < maxDst) {
                // Out of range, cancel iteration
                if ((uint)fromC.X >= (uint)mSizing.Size.X - 1) break;
                if ((uint)fromC.Y >= (uint)mSizing.Size.Y - 1) break;

                // TODO: Compare against the min/max extents of each terrain chunk
                // (once the hierarchical cache is generated)

                // Get the heights of the 4 corners
                var terHeight00 = (float)mHeightMap[mSizing.ToIndex(fromC + new Int2(0, 0))].HeightF;
                var terHeight10 = (float)mHeightMap[mSizing.ToIndex(fromC + new Int2(1, 0))].HeightF;
                var terHeight01 = (float)mHeightMap[mSizing.ToIndex(fromC + new Int2(0, 1))].HeightF;
                var terHeight11 = (float)mHeightMap[mSizing.ToIndex(fromC + new Int2(1, 1))].HeightF;
                // Raycast against the triangles that make up this cell
                Vector3 bc; float t;
                if (Triangulation.RayTriangleIntersection(localRay,
                    new Vector3((Vector2)(fromC + new Int2(0, 0)) * terScale, terHeight00).toxzy(),
                    new Vector3((Vector2)(fromC + new Int2(1, 1)) * terScale, terHeight11).toxzy(),
                    new Vector3((Vector2)(fromC + new Int2(1, 0)) * terScale, terHeight10).toxzy(),
                    out bc, out t
                ) || Triangulation.RayTriangleIntersection(localRay,
                    new Vector3((Vector2)(fromC + new Int2(0, 0)) * terScale, terHeight00).toxzy(),
                    new Vector3((Vector2)(fromC + new Int2(0, 1)) * terScale, terHeight01).toxzy(),
                    new Vector3((Vector2)(fromC + new Int2(1, 1)) * terScale, terHeight11).toxzy(),
                    out bc, out t)
                ) {
                    // If a hit was found, return the data
                    hit = new LandscapeHit {
                        HitPosition = ray.Origin + ray.Direction * t,
                    };
                    return true;
                }
                // If no hit was found, iterate to the next cell (in just 1 of the horizontal directions)
                float xNext = maxDst;
                float yNext = maxDst;
                var nextEdgeDelta = (Vector2)(fromC + dirEdge) * terScale - from;
                if (dir.X != 0.0f) xNext = Math.Min(xNext, nextEdgeDelta.X / dir.X);
                if (dir.Y != 0.0f) yNext = Math.Min(yNext, nextEdgeDelta.Y / dir.Y);
                fromC.X += xNext < yNext ? dirSign.X : 0;
                fromC.Y += xNext < yNext ? 0 : dirSign.Y;
                dst = Math.Min(xNext, yNext);
            }
            hit = default;
            return false;
        }

        public void PaintRectangle(int typeId, RectI range) {
            for (int y = range.Min.Y; y < range.Max.Y; y++) {
                var yI = Sizing.ToIndex(new Int2(0, y));
                for (int x = range.Min.X; x < range.Max.X; x++) {
                    int i = yI + x;
                    var cell = mControlMap[i];
                    cell.TypeId = (byte)typeId;
                    mControlMap[i] = cell;
                }
            }
            NotifyLandscapeChanged(new LandscapeChangeEvent(range, controlMap: true));
        }

        string dataPath = "./Assets/Terrain.tdat";
        public void Load() {
            if (!File.Exists(dataPath)) return;
            using (var stream = File.OpenRead(dataPath))
            using (var compressed = new ZLibStream(stream, CompressionMode.Decompress)) {
                Deserialize(new BinaryReader(compressed));
            }
        }
        public void Save() {
            using (var stream = new MemoryStream())
            using (var compressed = new ZLibStream(stream, CompressionMode.Compress)) {
                Serialize(new BinaryWriter(compressed));
                compressed.Flush();
                File.WriteAllBytes(dataPath, stream.ToArray());
            }
        }
        public void Reset() {
            mHeightMap.AsSpan().Fill(HeightCell.Default);
            mControlMap.AsSpan().Fill(ControlCell.Default);
            if (WaterEnabled) mWaterMap.AsSpan().Fill(WaterCell.Default);
            NotifyLandscapeChanged();
        }

        // Read/write data to disk (untested)
        public void Serialize(BinaryWriter writer) {
            writer.Write(SerialVersion);
            writer.Write(Size.X);
            writer.Write(Size.Y);
            var typeMapping = new PooledArray<byte>(64);
            for (int i = 0; i < typeMapping.Count; i++) typeMapping[i] = (byte)i;
            // Write type id mapping
            var allTypes = new PooledList<byte>();
            byte prevTypeId = 255;
            for (int i = 0; i < mControlMap.Length; i++) {
                var typeId = mControlMap[i].TypeId;
                if (typeId == prevTypeId) continue;
                prevTypeId = typeId;
                if (!allTypes.Contains(typeId)) allTypes.Add(typeId);
            }
            writer.Write((byte)allTypes.Count);
            for (int i = 0; i < allTypes.Count; i++) {
                writer.Write(Layers[allTypes[i]].Name);
                typeMapping[allTypes[i]] = (byte)i;
            }
            allTypes.Dispose();
            // Write all maps
            for (int i = 0; i < mHeightMap.Length; i++) writer.Write(mHeightMap[i].Height);
            for (int i = 0; i < mControlMap.Length; i++) writer.Write(typeMapping[mControlMap[i].TypeId]);
            writer.Write(WaterEnabled);
            if (WaterEnabled) for (int i = 0; i < mWaterMap.Length; i++) writer.Write(mWaterMap[i].Data);
            typeMapping.Dispose();
        }
        public void Deserialize(BinaryReader reader) {
            var version = reader.ReadInt32();
            Debug.Assert(version <= SerialVersion, "Unsupported version");
            SetSize(new Int2(reader.ReadInt32(), reader.ReadInt32()), false);

            // Read type id mapping
            var typeC = reader.ReadByte();
            using var typeMapping = new PooledArray<byte>(typeC);
            using (new ProfilerMarker("Layers").Auto()) {
                for (int i = 0; i < typeC; i++) {
                    typeMapping[i] = (byte)Layers.FindLayerId(reader.ReadString());
                }
            }
            using (new ProfilerMarker("Maps").Auto()) {
                // Read maps
                reader.Read(MemoryMarshal.Cast<HeightCell, byte>(mHeightMap.AsSpan()));
                //for (int i = 0; i < mHeightMap.Length; i++) mHeightMap[i] = new HeightCell() { Height = reader.ReadInt16() };

                reader.Read(MemoryMarshal.Cast<ControlCell, byte>(mControlMap.AsSpan()));
                for (int i = 0; i < mControlMap.Length; i++) mControlMap[i].TypeId = typeMapping[mControlMap[i].TypeId];
                //for (int i = 0; i < mControlMap.Length; i++) mControlMap[i] = new ControlCell() { TypeId = typeMapping[reader.ReadByte()], };

                SetWaterEnabled(reader.ReadBoolean());
                if (WaterEnabled)
                    reader.Read(MemoryMarshal.Cast<WaterCell, byte>(mWaterMap.AsSpan()));
                    //for (int i = 0; i < mWaterMap.Length; i++) mWaterMap[i] = new WaterCell() { Data = reader.ReadByte() };
            }
            // Update all change listeners
            NotifyLandscapeChanged(LandscapeChangeEvent.MakeAll(Size));
        }
    }

    public static class HeightMapExt {
        public static float GetHeightAtF<T>(this T heightmap, Vector2 pos) where T : IHeightmapReader{
            Vector2 l;
            var p00 = heightmap.Sizing.WorldToLandscape(pos, out l);
            //p00 = Int2.Clamp(p00, 0, heightmap.Sizing.Size - 2);
            var h00 = heightmap.GetHeightAt(p00);
            var h10 = heightmap.GetHeightAt(p00 + new Int2(1, 0));
            var h01 = heightmap.GetHeightAt(p00 + new Int2(0, 1));
            var h11 = heightmap.GetHeightAt(p00 + new Int2(1, 1));
            return (
                (float)h00 * (1.0f - l.X) * (1.0f - l.Y)
                + (float)h10 * (l.X) * (1.0f - l.Y)
                + (float)h01 * (1.0f - l.X) * (l.Y)
                + (float)h11 * (l.X) * (l.Y)
                ) / (float)LandscapeData.HeightScale;
        }
        public static float GetHeightAtF<T>(this T heightmap, Int2 pnt) where T : IHeightmapReader {
            return heightmap.GetHeightAt(pnt) / LandscapeData.HeightScale;
        }
        public static Int2 GetDerivative<T>(this T heightmap, Int2 pnt) where T : IHeightmapReader {
            var p0 = Int2.Max(pnt - 1, 0);
            var p1 = Int2.Min(pnt + 1, heightmap.Sizing.Size - 1);
            var dx = heightmap.GetHeightAt(new Int2(p1.X, pnt.Y)) - heightmap.GetHeightAt(new Int2(p0.X, pnt.Y));
            var dy = heightmap.GetHeightAt(new Int2(pnt.X, p1.Y)) - heightmap.GetHeightAt(new Int2(pnt.X, p0.Y));
            return new Int2(dx, dy);
        }
        public static Vector3 GetNormalAt<T>(this T heightmap, Int2 pnt) where T : IHeightmapReader {
            var dd = heightmap.GetDerivative(pnt);
            return Vector3.Normalize(new Vector3(-dd.X, LandscapeData.HeightScale * 2f, -dd.Y));
        }
        public static int GetInterpolatedHeightAt<T>(this T heightmap, Int2 pos) where T : IHeightmapReader {
            if (heightmap.Sizing.Scale1024 != 1024) pos = pos * 1024 / heightmap.Sizing.Scale1024;
            return heightmap.GetInterpolatedHeightAt1024(pos);
        }
        public static int GetInterpolatedHeightAt1024<T>(this T heightMap, Int2 pos) where T : IHeightmapReader {
            Int2 pnt0 = pos >> 10;
            var sizing = heightMap.Sizing;
            if (!sizing.IsInBounds(pos)) return 0;
            pnt0 = Int2.Min(pnt0, sizing.Size - 2);
            var h00 = heightMap.GetHeightAt(sizing.ToIndex(pnt0));
            var h01 = heightMap.GetHeightAt(sizing.ToIndex(pnt0 + new Int2(1, 0)));
            var h10 = heightMap.GetHeightAt(sizing.ToIndex(pnt0 + new Int2(0, 1)));
            var h11 = heightMap.GetHeightAt(sizing.ToIndex(pnt0 + new Int2(1, 1)));
            Int2 pntF = pos - (pnt0 << 10);
            var hL = (h00 * (1024 - pntF.Y) + h10 * (pntF.Y));
            var hR = (h01 * (1024 - pntF.Y) + h11 * (pntF.Y));
            hL /= LandscapeData.HeightScale;    // hL and hR are in 1024 space (because above is 1024 range)
            hR /= LandscapeData.HeightScale;
            return (hL * (1024 - pntF.X) + hR * (pntF.X)) / 1024;
        }
    }

}
