﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.Utility;

namespace Weesals.Landscape {
    // A PBR texture set that can appear on the terrain, with some metadata
    // associated with game interaction and rendering
    public class LandscapeLayer {
        public enum AlignmentModes : byte { NoRotation, Clustered, WithNormal, Random90, Random, }
        public enum TerrainFlags : ushort {
            land = 0x00ff, Ground = 0x0001, Cliff = 0x7002,
            water = 0x0f00, River = 0x7100, Ocean = 0x7200,
            FlagImpassable = 0x7000,
        }
        public string Name;

        public float Scale = 0.2f;
        public float Rotation;
        public float Fringe = 0.5f;
        public float UniformMetal = 0f;
        public float UniformSmoothness = 0f;
        public float UvYScroll = 0f;
        public AlignmentModes Alignment = AlignmentModes.Clustered;
        public TerrainFlags Flags = TerrainFlags.Ground;
    }

    // A terrain
    public class LandscapeData {
        // How many units of Height is 1 unit in world space
        public const int HeightScale = 1024;

        // Hold data related to the position/size of the landscape in the world
        public struct SizingData {
            // Location of the terrain 0,0 coord (minimum corner)
            public Vector3 Location;
            // How many cells the terrain contains
            public Int2 Size;
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
            public Int2 WorldToLandscape(Vector3 worldPos) { return (Int2)((worldPos - Location).toxz() * (1024f / Scale1024) + new Vector2(0.5f)); }
            public Int2 WorldToLandscape(Vector2 worldPos) { return (Int2)((worldPos - Location.toxz()) * (1024f / Scale1024) + new Vector2(0.5f)); }
            public Vector3 LandscapeToWorld(Int2 landscapePos) { return (((Vector2)landscapePos) * (Scale1024 / 1024f)).AppendZ(0.0f) + Location; }

            public Int2 WorldToLandscape(Vector2 worldPos, out Vector2 outLerp) {
                worldPos -= Location.toxz();
                worldPos *= 1024f / Scale1024;
                var pnt = (Int2)(worldPos);
                outLerp = worldPos - (Vector2)pnt;
                return pnt;
            }
        }

        // Data used by the vertex shader to position a vertex (ie. its height)
        public struct HeightCell {
            public short Height;
            public float GetHeightF() { return (float)Height / (float)HeightScale; }
            public static HeightCell Default;
        }
        // Data used by the fragment shader to determine shading (ie. texture)
        public struct ControlCell {
            public byte TypeId;
            public static ControlCell Default;
        }
        // Data used to render water over the terrain
        public struct WaterCell {
            public byte Data;
            public short GetHeight() {
                return (short)((Data - 127) << 3);
            }
            public void SetHeight(short value) {
                Data = (byte)Math.Clamp((value >> 3) + 127, 0, 255);
            }
            public bool GetIsInvalid() { return Data == 0; }
            public static WaterCell Default;
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
            public bool GetHasChanges() {
                return HeightMapChanged | ControlMapChanged | WaterMapChanged;
            }

            // Expand this to include the passed in range/flags
            public void CombineWith(in LandscapeChangeEvent other) {
                // If this is the first change, just use it as is.
                if (!GetHasChanges()) { this = other; return; }
                // Otherwise inflate our range and integrate changed flags
                var min = Int2.Min(Range.Min, other.Range.Min);
                var max = Int2.Max(Range.Max, other.Range.Max);
                Range = new RectI(min.X, min.Y, max.X - min.X, max.Y - min.Y);
                HeightMapChanged |= other.HeightMapChanged;
                ControlMapChanged |= other.ControlMapChanged;
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

        public struct LandscapeHit {
            public Vector3 mHitPosition;
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
            public ref CellType this[Int2 p] {
                get => ref mCells[mSizing.ToIndex(p)];
            }
        }
        public struct HeightMapReadOnly {
            internal DataReader<HeightCell> Reader;
            internal HeightMapReadOnly(SizingData sizing, HeightCell[] cells) {
                Reader = new DataReader<HeightCell>(sizing, cells);
            }
            public ref HeightCell this[Int2 p] {
                get => ref Reader[p];
            }
            public float GetHeightAtF(Vector2 pos) {
                Vector2 l;
                var p00 = Reader.mSizing.WorldToLandscape(pos, out l);
                p00 = Int2.Clamp(p00, 0, Reader.Size - 2);
                var h00 = Reader[p00];
                var h10 = Reader[p00 + new Int2(1, 0)];
                var h01 = Reader[p00 + new Int2(0, 1)];
                var h11 = Reader[p00 + new Int2(1, 1)];
                return (
                    (float)h00.Height * (1.0f - l.X) * (1.0f - l.Y)
                    + (float)h10.Height * (l.X) * (1.0f - l.Y)
                    + (float)h01.Height * (1.0f - l.X) * (l.Y)
                    + (float)h11.Height * (l.X) * (l.Y)

                    ) / (float)HeightScale;
            }
        };
        public struct ControlMapReadOnly {
            internal DataReader<ControlCell> Reader;
            internal ControlMapReadOnly(SizingData sizing, ControlCell[] cells) {
                Reader = new DataReader<ControlCell>(sizing, cells);
            }
            public ref ControlCell this[Int2 p] {
                get => ref Reader[p];
            }
        }
        public struct WaterMapReadOnly {
            internal DataReader<WaterCell> Reader;
            internal WaterMapReadOnly(SizingData sizing, WaterCell[] cells) {
                Reader = new DataReader<WaterCell>(sizing, cells);
            }
            public ref WaterCell this[Int2 p] {
                get => ref Reader[p];
            }
        }

        SizingData mSizing = new SizingData(0);
        HeightCell[] mHeightMap = Array.Empty<HeightCell>();
        ControlCell[] mControlMap = Array.Empty<ControlCell>();
        WaterCell[]? mWaterMap;

        // Used to track if the landscape has changed since last
        int mRevision = 0;

        // Listen for changes to the landscape data
        //std::vector<ChangeCallback> mChangeListeners;
        public Action<LandscapeData, LandscapeChangeEvent> mChangeListeners;

        public LandscapeData() { }

        public SizingData GetSizing() { return mSizing; }
        public Int2 GetSize() { return mSizing.Size; }
        public float GetScale() { return (float)mSizing.Scale1024 / 1024.0f; }

        public int GetRevision() { return mRevision; }
        public bool GetIsWaterEnabled() { return mWaterMap != null; }

        public void SetLocation(Vector3 location) {
            mSizing.Location = location;
        }
        public void SetSize(Int2 size) {
            mSizing.Size = size;
            int cellCount = mSizing.Size.X * mSizing.Size.Y;
            mHeightMap = new HeightCell[cellCount];
            mControlMap = new ControlCell[cellCount];
            Array.Fill(mHeightMap, HeightCell.Default);
            Array.Fill(mControlMap, ControlCell.Default);
            if (GetIsWaterEnabled()) {
                mWaterMap = new WaterCell[cellCount];
                Array.Fill(mWaterMap, WaterCell.Default);
            }
        }
        public void SetScale(int scale1024) {
            mSizing.Scale1024 = scale1024;
        }
        public void SetWaterEnabled(bool enable) {
            // Resize water to either match heightmap or be erased
            mWaterMap = enable ? new WaterCell[mHeightMap.Length] : null;
        }

        public void NotifyLandscapeChanged() {
            NotifyLandscapeChanged(LandscapeChangeEvent.MakeAll(GetSize()));
        }
        public void NotifyLandscapeChanged(in LandscapeChangeEvent changeEvent) {
            ++mRevision;
            if (mChangeListeners != null)
                mChangeListeners(this, changeEvent);
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
                var terHeight00 = (float)mHeightMap[mSizing.ToIndex(fromC + new Int2(0, 0))].GetHeightF();
                var terHeight10 = (float)mHeightMap[mSizing.ToIndex(fromC + new Int2(1, 0))].GetHeightF();
                var terHeight01 = (float)mHeightMap[mSizing.ToIndex(fromC + new Int2(0, 1))].GetHeightF();
                var terHeight11 = (float)mHeightMap[mSizing.ToIndex(fromC + new Int2(1, 1))].GetHeightF();
                // Raycast against the triangles that make up this cell
                Vector3 bc; float t;
                if (Geometry.RayTriangleIntersection(localRay,
                    new Vector3((Vector2)(fromC + new Int2(0, 0)) * terScale, terHeight00).toxzy(),
                    new Vector3((Vector2)(fromC + new Int2(1, 1)) * terScale, terHeight11).toxzy(),
                    new Vector3((Vector2)(fromC + new Int2(1, 0)) * terScale, terHeight10).toxzy(),
                    out bc, out t
                ) || Geometry.RayTriangleIntersection(localRay,
                    new Vector3((Vector2)(fromC + new Int2(0, 0)) * terScale, terHeight00).toxzy(),
                    new Vector3((Vector2)(fromC + new Int2(0, 1)) * terScale, terHeight01).toxzy(),
                    new Vector3((Vector2)(fromC + new Int2(1, 1)) * terScale, terHeight11).toxzy(),
                    out bc, out t)
                ) {
                    // If a hit was found, return the data
                    hit = new LandscapeHit {
                        mHitPosition = ray.Origin + ray.Direction * t,
                    };
                    return true;
                }
                // If no hit was found, iterate to the next cell (in just 1 of the horizontal directions)
                float xNext = maxDst;
                float yNext = maxDst;
                var nextEdgeDelta = (Vector2)(fromC + dirEdge) * terScale - from;
                if (dir.X != 0.0f) xNext = Math.Max(xNext, nextEdgeDelta.X / dir.X);
                if (dir.Y != 0.0f) yNext = Math.Max(yNext, nextEdgeDelta.Y / dir.Y);
                fromC.X += xNext < yNext ? dirSign.X : 0;
                fromC.Y += xNext < yNext ? 0 : dirSign.Y;
                dst = Math.Min(xNext, yNext);
            }
            hit = default;
            return false;
        }

    }

}