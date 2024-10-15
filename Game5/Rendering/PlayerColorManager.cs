using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;

namespace Game5.Rendering {
    public class PlayerColorManager {

        public static readonly Color[] Colours = new Color[] {
            new Color(255, 211, 0, 255),  // Yellow
            new Color(0x38, 0x6A, 0xE9, 255),   // Blue
            new Color(255, 0, 0, 255),   // Red
            new Color(0, 88, 0, 255),  // Olive
            new Color(255, 26, 185, 255),  // Pink
            new Color(0, 0, 44, 255),  // Black
            new Color(132, 132, 255, 255),  // Purple?
            new Color(158, 79, 70, 255),   // Brown
            new Color(0, 255, 194, 255),  // Teal
            Color.Gray,
        };

        public Material PlayerColorMaterial;

        public PlayerColorManager() {
            var colours = new Vector4[Colours.Length];
            for (int i = 0; i < colours.Length; i++) {
                colours[i] = Colours[i];
            }
            PlayerColorMaterial = new();
            PlayerColorMaterial.SetArrayValue<Vector4>("_PlayerColors", colours);
        }

    }
}
