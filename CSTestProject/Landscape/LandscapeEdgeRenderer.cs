using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.Engine.Profiling;
using Weesals.Geometry;

namespace Weesals.Landscape {
    public class LandscapeEdgeRenderer {

        private static ProfilerMarker ProfileMarker_Render = new("Render Landscape Edge");
        private static ProfilerMarker ProfileMarker_UpdateEdges = new("Update Edges");
        public const int TileSize = LandscapeRenderer.TileSize;

        public LandscapeRenderer LandscapeRenderer;
        public LandscapeData LandscapeData => LandscapeRenderer.LandscapeData;

        Mesh edgeMesh;
        Material EdgeMaterial;
        Material WaterEdgeMaterial;
        MeshDrawInstanced edgeDraw;
        MeshDrawInstanced waterEdgeDraw;

        public LandscapeEdgeRenderer(LandscapeRenderer landscape) {
            LandscapeRenderer = landscape;
            edgeMesh = LandscapeUtility.GenerateSubdividedQuad(TileSize, 1, false, true, true);

            EdgeMaterial = new("./Assets/landscapeEdge.hlsl", landscape.LandMaterial);
            EdgeMaterial.SetMacro("EDGE", "1");
            EdgeMaterial.SetTexture("EdgeTex", Resources.LoadTexture("./Assets/T_WorldsEdge.jpg"));
            //EdgeMaterial.SetRasterMode(RasterMode.MakeNoCull());

            WaterEdgeMaterial = new Material(landscape.WaterMaterial);
            WaterEdgeMaterial.SetMacro("EDGE", "1");

            edgeDraw = new MeshDrawInstanced(edgeMesh, EdgeMaterial);
            edgeDraw.AddInstanceElement("INSTANCE", BufferFormat.FORMAT_R16G16_SINT);
            waterEdgeDraw = new MeshDrawInstanced(edgeMesh, WaterEdgeMaterial);
            waterEdgeDraw.AddInstanceElement("INSTANCE", BufferFormat.FORMAT_R16G16_SINT);
        }
        private ConvexHull hull = new();
        private void IntlIntersectLocalFrustum(Frustum localFrustum, out Vector2 visMin, out Vector2 visMax) {
            hull.FromFrustum(localFrustum);
            hull.Slice(new Plane(Vector3.UnitY, 5f));
            hull.Slice(new Plane(-Vector3.UnitY, 0f));
            var bounds = hull.GetAABB();
            visMin = bounds.Min.toxz();
            visMax = bounds.Max.toxz();
        }

        unsafe private void UpdateEdgeInstances(MeshDrawInstanced draw, Frustum localFrustum, LandscapeRenderer.HeightMetaData[] metadata, Int2 imin, Int2 imax) {
            using var marker = ProfileMarker_UpdateEdges.Auto();
            int maxCount = Int2.CSum(imax - imin + 1) * 4;
            var itemsRaw = stackalloc Short2[maxCount];
            var items = new Span<Short2>(itemsRaw, maxCount);
            int count = 0;
            int drawHash = 0;
            var lmax = LandscapeRenderer.ChunkCount;
            for (int a = 0; a < 2; a++) {
                var amin = a == 0 ? imin : imin.YX;
                var amax = a == 0 ? imax : imax.YX;
                var lsize = a == 0 ? lmax : lmax.YX;
                for (int d = 0; d < 2; d++) {
                    var apnt = d == 0 ? amin.Y : amax.Y - 1;
                    if (apnt != (d == 0 ? 0 : lsize.Y - 1)) continue;
                    for (int i = amin.X; i < amax.X; ++i) {
                        var pnt = new Int2(i, apnt);
                        if (a == 1) pnt = pnt.YX;
                        var chunk = metadata[pnt.X + pnt.Y * LandscapeRenderer.ChunkCount.X];
                        if (chunk.IsValid) {
                            pnt.X = (i + (a ^ d)) * TileSize * ((a ^ d) == 0 ? 1 : -1);
                            pnt.Y = a + d * 2;
                            var value = new Short2((short)pnt.X, (short)pnt.Y);
                            items[count++] = value;
                            drawHash = HashCode.Combine(drawHash, value);
                        }
                    }
                }
            }
            draw.SetInstanceData(itemsRaw, count, 0, drawHash);
        }

        public void Render(CSGraphics graphics, ref MaterialStack materials, ScenePass pass, Frustum localFrustum) {
            using var marker = ProfileMarker_Render.Auto();

            // Calculate the min/max AABB of the projected frustum 
            Int2 visMinI, visMaxI;
            {
                IntlIntersectLocalFrustum(localFrustum, out var visMin, out var visMax);
                visMinI = Int2.Max(Int2.FloorToInt(visMin / TileSize), 0);
                visMaxI = Int2.Min(Int2.CeilToInt(visMax / TileSize), LandscapeRenderer.ChunkCount);
            }
            if (visMaxI.X <= visMinI.X || visMaxI.Y <= visMinI.Y) return;

            if (pass.TagsToInclude.HasAny(RenderTag.Default)) {
                UpdateEdgeInstances(edgeDraw, localFrustum, LandscapeRenderer.GetLandscapeChunkMeta(), visMinI, visMaxI);
                edgeDraw.Draw(graphics, ref materials, pass, CSDrawConfig.MakeDefault());
            }
            if (pass.TagsToInclude.Has(RenderTag.Transparent) && LandscapeData.WaterEnabled) {
                UpdateEdgeInstances(waterEdgeDraw, localFrustum, LandscapeRenderer.GetLandscapeChunkMeta(), visMinI, visMaxI);
                waterEdgeDraw.Draw(graphics, ref materials, pass, CSDrawConfig.MakeDefault());
            }
        }
    }
}
