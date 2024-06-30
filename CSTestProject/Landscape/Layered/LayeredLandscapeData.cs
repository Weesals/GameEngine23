using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.Utility;

namespace Weesals.Landscape {

    using HeightCell = LandscapeData.HeightCell;
    using ControlCell = LandscapeData.ControlCell;
    using WaterCell = LandscapeData.WaterCell;

    /*
     * Each tile needs to know which tile it connects with along its +X, +Y, and +XY sides
     * (because it needs n+1 verts)
     * Could assign each tile an "island ID" and it connects with the same island ID
     * Cannot be implicit because tile0 could end in cliff (2 layers) and connect with
     *   tile1 (1 layer), ambiguous if tile1 extends cliff or land below
     * Could have each tile specify the ID it connects with
     *   => IDs can shift as groups are resized
     * 
     * Use flood fill distance for mask when painting
     * Calculate 3x3 weight and switch if paint threshold > 3x3 weight
     * => Iterate in checker pattern or similar
     */

    public class LayeredLandscapeData {

        public const int TileSize = 8;

        public struct TileData {
            public int IslandID;    // Tiles with same get island connected
            public int NextTile;    // Linked List of tiles(s) which are on the same XZ coords
        }
        public struct Group {
            public const int Size = 8;
            public const int Count = Size * Size;
            public ulong TileMask;
            // Offset of the first tileData, must be multiplied by 64 before indexing heightMap
            public int TileOffset;
            public int TileCount => BitOperations.PopCount(TileMask);
            public bool GetHasTile(int localTileIndex) { return (TileMask & (1ul << localTileIndex)) != 0; }
            public static readonly Group Invalid = new();
        }
        public struct TileIndices {
            public int LandStart, WaterStart;
        }

        private RectI groupRange;
        private Int2 groupSize => groupRange.Size;
        private Group[] groups = Array.Empty<Group>();
        // Is in 1/64 space (ie. single index for entire tile)
        private SparseIndices tileAllocation = new();

        TileData[] tileData = Array.Empty<TileData>();
        HeightCell[] heightMap = Array.Empty<HeightCell>();
        ControlCell[] controlMap = Array.Empty<ControlCell>();
        WaterCell[]? waterMap;

        public void Initialise() {
            groups.AsSpan().Fill(Group.Invalid);
        }

        public void Resize(RectI newRange) {
            var commonMin = Int2.Max(newRange.Min, groupRange.Min);
            var commonMax = Int2.Min(newRange.Max, groupRange.Max);

            var newCount = newRange.Width * newRange.Height;
            var oldGroups = groups;
            groups = new Group[newCount];
            groups.AsSpan().Fill(Group.Invalid);
            for (int y = commonMin.Y; y < commonMax.Y; y++) {
                for (int x = commonMin.X; x < commonMax.X; x++) {
                    var oldI = (x - groupRange.X) + (y - groupRange.Y) * groupRange.Width;
                    var newI = (x - newRange.X) + (y - newRange.Y) * newRange.Width;
                    groups[newI] = oldGroups[oldI];
                }
            }
            groupRange = newRange;
        }

        public int RequireTileIndex(Int2 tilePnt, int islandId = 0) {
            var groupPnt = tilePnt >> 3;
            if ((uint)(groupPnt.X - groupRange.X) >= groupRange.Width ||
                (uint)(groupPnt.Y - groupRange.Y) >= groupRange.Height) {
                var range = groupRange;
                if (range.Width <= 0 || range.Height <= 0)
                    range = new RectI(groupPnt.X, groupPnt.Y, 1, 1);
                else range = range.ExpandToInclude(groupPnt);
                Resize(range);
            }
            int localTileIndex = (tilePnt.X & 0x07) + ((tilePnt.Y & 0x07) << 3);
            ref var group = ref groups[GetGroupIndex(groupPnt)];
            if (!group.GetHasTile(localTileIndex)) {
                AllocateTile(ref group, localTileIndex);
            }
            return group.TileOffset + localTileIndex;
        }
        public int GetTileIndex(Int2 tilePnt) {
            var groupPnt = tilePnt >> 3;
            if ((uint)(groupPnt.X - groupRange.X) >= groupRange.Width ||
                (uint)(groupPnt.Y - groupRange.Y) >= groupRange.Height) return -1;
            int localTileIndex = (tilePnt.X & 0x07) + ((tilePnt.Y & 0x07) << 3);
            ref var group = ref groups[GetGroupIndex(groupPnt)];
            if (!group.GetHasTile(localTileIndex)) return -1;
            return group.TileOffset + localTileIndex;
        }
        public TileDataAccessor RequireTile(Int2 tilePnt) {
            return new(this, RequireTileIndex(tilePnt));
        }
        public TileDataAccessor GetTile(Int2 tilePnt) {
            return new(this, GetTileIndex(tilePnt));
        }

        private void AllocateTile(ref Group group, int localTileIndex) {
            Debug.Assert(!group.GetHasTile(localTileIndex));
            var mask = group.TileMask & ~(ulong.MaxValue << localTileIndex);
            int index = BitOperations.PopCount(mask);
            int oldDataOffset = group.TileOffset;
            var totalCount = group.TileCount;
            if (group.TileOffset >= 0 && tileAllocation.TryTakeAt(group.TileOffset + totalCount, 1)) {
                // All good!
            } else {
                if (oldDataOffset >= 0) tileAllocation.Add(oldDataOffset, totalCount);
                group.TileOffset = tileAllocation.Take(totalCount + 1);
                if (group.TileOffset == -1) {
                    ResizeMaps((int)BitOperations.RoundUpToPowerOf2((uint)tileData.Length + 256));
                    group.TileOffset = tileAllocation.Take(totalCount + 1);
                }
                Debug.Assert(group.TileOffset >= 0);
            }
            if (index > 0 && group.TileOffset < oldDataOffset) MoveTiles(oldDataOffset, group.TileOffset, index);
            if (index < totalCount) MoveTiles(oldDataOffset + index, group.TileOffset + index + 1, totalCount - index);
            if (index > 0 && group.TileOffset > oldDataOffset) MoveTiles(oldDataOffset, group.TileOffset, index);
            group.TileMask |= 1ul << localTileIndex;
        }

        private void ResizeMaps(int newSize) {
            int oldSize = tileData.Length;
            Array.Resize(ref tileData, newSize);
            tileAllocation.Add(oldSize, newSize - oldSize);
            newSize *= 64;
            Array.Resize(ref heightMap, newSize);
            Array.Resize(ref controlMap, newSize);
            if (waterMap != null) Array.Resize(ref waterMap, newSize);
        }

        private void MoveTiles(int from, int to, int count) {
            Array.Copy(tileData, from, tileData, to, count);
            from *= 64; to *= 64; count *= 64;
            Array.Copy(heightMap, from, heightMap, to, count);
            Array.Copy(controlMap, from, controlMap, to, count);
            if (waterMap != null) Array.Copy(waterMap, from, waterMap, to, count);
        }

        private int GetGroupIndex(Int2 groupPnt) {
            return groupPnt.X + groupPnt.Y * groupSize.X;
        }

        public struct TileDataAccessor {
            public const int Size = 8;
            public readonly LayeredLandscapeData Landscape;
            public readonly int DataOffset;
            public int TileIndex => DataOffset >> 6;
            public bool IsValid => DataOffset >= 0;
            public TileData TileData => Landscape.tileData[TileIndex];
            public TileDataAccessor(LayeredLandscapeData landscape, int tileIndex) {
                Landscape = landscape;
                DataOffset = tileIndex * 64;
            }
            public ref HeightCell GetHeightLocal(Int2 pnt) {
                return ref Landscape.heightMap[DataOffset + pnt.X + pnt.Y * 8];
            }
            public ref ControlCell GetControlLocal(Int2 pnt) {
                return ref Landscape.controlMap[DataOffset + pnt.X + pnt.Y * 8];
            }
            public ref WaterCell GetWaterLocal(Int2 pnt) {
                if (Landscape.waterMap == null) throw new Exception();
                return ref Landscape.waterMap[DataOffset + pnt.X + pnt.Y * 8];
            }
            public TileDataAccessor GetNextLayer() {
                return new(Landscape, Landscape.tileData[TileIndex].NextTile);
            }
        }

    }
}
