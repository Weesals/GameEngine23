using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        public string Name = "Unknown";
        public string BaseColor = "";
        public string NormalMap = "";

        public float Scale = 0.1f;
        [Range(0, 360)] public float Rotation;
        [Range(0, 1)] public float Fringe = 0.5f;
        [Range(0, 1)] public float UniformMetal = 0f;
        [Range(-1, 1)] public float UniformRoughness = 1.0f;
        [Range(-2, 2)] public float UvYScroll = 0f;
        public AlignmentModes Alignment = AlignmentModes.Clustered;
        public TerrainFlags Flags = TerrainFlags.Ground;

        //public FoliageType[] Foliage;

        public LandscapeLayer(string name) { Name = name; }

        public override int GetHashCode() {
            return HashCode.Combine(
                Scale, Rotation,
                Fringe, UniformMetal, UniformRoughness,
                UvYScroll, Alignment, Flags
            );
        }
        public override string ToString() { return Name; }
    }

    public class LandscapeLayerCollection {

        public LandscapeLayer[] TerrainLayers;

        public int LayerCount => TerrainLayers.Length;
        public int Revision { get; private set; }

        public LandscapeLayer this[int index] => TerrainLayers[index];

        public LandscapeLayerCollection(params LandscapeLayer[] layers) {
            TerrainLayers = layers;
        }

        public int FindLayerId(string name) {
            for (int i = 0; i < TerrainLayers.Length; i++) {
                if (TerrainLayers[i].Name == name) return i;
            }
            return -1;
        }
        public int FindLayerId(LandscapeLayer layer) {
            for (int i = 0; i < TerrainLayers.Length; i++) {
                if (TerrainLayers[i] == layer) return i;
            }
            return -1;
        }

    }
}
