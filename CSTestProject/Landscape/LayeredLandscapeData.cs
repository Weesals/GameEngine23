using System;
using System.Collections.Generic;
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

    public class LayeredLandscapeData {

        public struct Chunk {
            public int DataOffset;
        }

        public SparseArray<Chunk> chunks = new();
        public RangeInt[] grid = Array.Empty<RangeInt>();

        HeightCell[] heightMap = Array.Empty<HeightCell>();
        ControlCell[] controlMap = Array.Empty<ControlCell>();
        WaterCell[]? waterMap;

        public void Initialise() {
            chunks.Clear();
            grid.AsSpan().Fill(new RangeInt(0, 0));
        }

        //public int RequireChunkAt(Int2 pos) {
        //}

    }
}
