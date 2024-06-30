using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;

namespace Weesals.Landscape.Layered {
    public class LayeredLandscapeRenderer {

        public const int TileSize = LayeredLandscapeData.TileSize;
        public const int HeightScale = LandscapeData.HeightScale;

        public Material LandMaterial;
        public Material WaterMaterial;

        // The mesh that is instanced across the surface
        Mesh tileMesh;

        public LayeredLandscapeRenderer() {
            tileMesh = LandscapeUtility.GenerateSubdividedQuad(TileSize, TileSize);

            LandMaterial = new Material("./Assets/landscape3x3.hlsl");
            LandMaterial.SetMeshShader(Resources.LoadShader("./Assets/landscape3x3.hlsl", "MSMain"));
            LandMaterial.SetTexture("NoiseTex", Resources.LoadTexture("./Assets/Noise.jpg"));
            LandMaterial.SetBuffer("Vertices", tileMesh.VertexBuffer);
            LandMaterial.SetBuffer("Indices", tileMesh.IndexBuffer);
            WaterMaterial = new("./Assets/water.hlsl", LandMaterial);
        }




    }
}
