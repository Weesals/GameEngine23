using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.Utility;

namespace Weesals.Landscape {
    public class LandscapeRenderer {

        public const int TileResolution = 8;
        public const int HeightScale = LandscapeData.HeightScale;

        public interface ILandscapeDataListener {
            void NotifyDataChanged(LandscapeData odata, LandscapeData ndata);
        }

        public LandscapeData LandscapeData { get; private set; }
        public LandscapeLayerCollection Layers => LandscapeData.Layers;

        Material LandMaterial;
        Material WaterMaterial;
        Material EdgeMaterial;
        Material WaterEdgeMaterial;

        // The mesh that is instanced across the surface
        Mesh tileMesh;
        Mesh edgeMesh;

        public struct HeightMetaData {
            public int MinHeight;
            public int HeightRange;
            public int HeightMedian => MinHeight + HeightRange / 2;
            public int MaxHeight => MinHeight + HeightRange;
            public bool IsValid => HeightRange >= 0;
            public static readonly HeightMetaData Invalid = new HeightMetaData() { MinHeight = int.MaxValue, HeightRange = -1, };

            public bool HeightRangeChanged(HeightMetaData value) {
                return (value.MinHeight != MinHeight || value.MaxHeight != MaxHeight);
            }
        }
        public struct PropertiesData {
            public Vector4 Sizing;
            public Vector4 Scaling;
            public int Hash;
            public PropertiesData(Vector4 sizing, Vector4 scaling) {
                Sizing = sizing;
                Scaling = scaling;
                Hash = sizing.GetHashCode() * 51 + scaling.GetHashCode();
            }
        }
        public struct RuntimeData : IDisposable {
            public CSTexture HeightMap;
            public CSTexture ControlMap;
            public Int2 ChunkCount;
            public HeightMetaData[] LandscapeChunkMeta;
            public HeightMetaData[] WaterChunkMeta;
            public HeightMetaData LandscapeMeta;
            public HeightMetaData WaterMeta;
            public LandscapeChangeEvent Changed;
            public PropertiesData Properties;
            public BoundingBox Bounds;

            public CSTexture BaseTextures;
            public CSTexture BumpTextures;

            public void Dispose() {
                HeightMap.Dispose();
                ControlMap.Dispose();
            }
        }
        private RuntimeData runtimeData;
        public PropertiesData Properties => runtimeData.Properties;
        public BoundingBox Bounds => runtimeData.Bounds;
        public CSTexture? HeightMap => runtimeData.HeightMap;
        public CSTexture? ControlMap => runtimeData.ControlMap;

        private List<ILandscapeDataListener> listeners = new();

        MeshDrawInstanced landscapeDraw;
        MeshDrawInstanced waterDraw;
        MeshDrawInstanced edgeDraw;
        MeshDrawInstanced waterEdgeDraw;
        int materialPropHash;

        int mRevision;

		public LandscapeRenderer() { }

        unsafe public void Initialise(LandscapeData landscapeData, Material rootMaterial) {
			LandscapeData = landscapeData;
            runtimeData.Dispose();
            runtimeData = new() {
                Changed = LandscapeChangeEvent.MakeAll(landscapeData.Size),
                Bounds = BoundingBox.FromMinMax(Vector3.Zero, landscapeData.Sizing.LandscapeToWorld(landscapeData.Size)),
            };
            if (!runtimeData.BaseTextures.IsValid()) {
                runtimeData.BaseTextures = CSTexture.Create()
                    .SetSize(512)
                    .SetArrayCount(Layers.LayerCount);
                for (int i = 0; i < Layers.LayerCount; ++i) {
                    CSResources.LoadTexture(Layers[i].BaseColor)
                        .GetTextureData()
                        .CopyTo(runtimeData.BaseTextures.GetTextureData(0, i));
                }
                runtimeData.BaseTextures.MarkChanged();
                runtimeData.BaseTextures.GenerateMips();
            }
            if (!runtimeData.BumpTextures.IsValid()) {
                runtimeData.BumpTextures = CSTexture.Create()
                    .SetSize(256)
                    .SetArrayCount(Layers.LayerCount);
                for (int i = 0; i < Layers.LayerCount; ++i) {
                    CSResources.LoadTexture(Layers[i].NormalMap)
                        .GetTextureData()
                        .CopyTo(runtimeData.BumpTextures.GetTextureData(0, i));
                }
                runtimeData.BumpTextures.MarkChanged();
                runtimeData.BumpTextures.GenerateMips();
            }
            if (tileMesh == null) tileMesh = LandscapeUtility.GenerateSubdividedQuad(TileResolution, TileResolution);
            if (edgeMesh == null) edgeMesh = LandscapeUtility.GenerateSubdividedQuad(TileResolution, 1, false, true, true);

            LandMaterial = new Material("./assets/landscape.hlsl");
            if (rootMaterial != null) LandMaterial.InheritProperties(rootMaterial);
            LandMaterial.SetTexture("BaseMaps", runtimeData.BaseTextures);
            LandMaterial.SetTexture("BumpMaps", runtimeData.BumpTextures);
            landscapeDraw = new MeshDrawInstanced(tileMesh, LandMaterial);
            landscapeDraw.AddInstanceElement("INSTANCE", BufferFormat.FORMAT_R16G16_UINT);

            EdgeMaterial = new Material("./assets/landscape.hlsl");
            EdgeMaterial.InheritProperties(LandMaterial);
            EdgeMaterial.SetMacro("EDGE", "1");
            EdgeMaterial.SetTexture("EdgeTex", CSResources.LoadTexture("./assets/T_WorldsEdge.jpg"));
            edgeDraw = new MeshDrawInstanced(edgeMesh, EdgeMaterial);
            edgeDraw.AddInstanceElement("INSTANCE", BufferFormat.FORMAT_R16G16_UINT);

            WaterMaterial = new("./assets/water.hlsl");
            WaterMaterial.InheritProperties(LandMaterial);
            WaterMaterial.SetTexture("NoiseTex", CSResources.LoadTexture("./assets/Noise.jpg"));
            WaterMaterial.SetTexture("FoamTex", CSResources.LoadTexture("./assets/FoamMask.jpg"));
            WaterMaterial.SetBlendMode(BlendMode.MakeAlphaBlend());
            WaterMaterial.SetDepthMode(DepthMode.MakeReadOnly());
            waterDraw = new MeshDrawInstanced(tileMesh, WaterMaterial);
            waterDraw.AddInstanceElement("INSTANCE", BufferFormat.FORMAT_R16G16_UINT);

            WaterEdgeMaterial = new Material("./assets/water.hlsl");
            WaterEdgeMaterial.InheritProperties(WaterMaterial);
            WaterEdgeMaterial.SetMacro("EDGE", "1");
            WaterEdgeMaterial.SetDepthMode(DepthMode.MakeReadOnly());
            waterEdgeDraw = new MeshDrawInstanced(edgeMesh, WaterEdgeMaterial);
            waterEdgeDraw.AddInstanceElement("INSTANCE", BufferFormat.FORMAT_R16G16_UINT);

            LandscapeData.OnLandscapeChanged += LandscapeChanged;
        }

        private void LandscapeChanged(LandscapeData data, LandscapeChangeEvent changed) {
            runtimeData.Changed.CombineWith(changed);
        }

        // Check if anything has changed, and apply changes
        private void ApplyDataChanges() {
            bool requireMaterialUpdates = false;
            if (Layers != null) {
                var layerHash = 0;
                for (int i = 0; i < Layers.LayerCount; i++) {
                    layerHash += Layers[i].GetHashCode();
                }
                if (layerHash != materialPropHash) {
                    materialPropHash = layerHash;
                    requireMaterialUpdates = true;
                }
            }

            // Update heightmap and controlmap
            if (runtimeData.Changed.HasChanges) {
                UpdateRange(runtimeData.Changed);
                runtimeData.Changed = LandscapeChangeEvent.MakeNone();
                var metadata = runtimeData.LandscapeMeta;
                runtimeData.Properties = new PropertiesData(
                    new Vector4(LandscapeData.Size.X, LandscapeData.Size.Y, 1f / LandscapeData.Size.X, 1f / LandscapeData.Size.Y),
                    new Vector4(
                        1024f / LandscapeData.Sizing.Scale1024, LandscapeData.Sizing.Scale1024 / 1024f,
                        (float)metadata.MinHeight / HeightScale, (float)metadata.HeightRange / HeightScale
                    )
                );
                requireMaterialUpdates = true;
            }
            if (requireMaterialUpdates) SetMaterialProperties();
        }
        private void UpdateRange(LandscapeChangeEvent changed) {
            var range = changed.Range;
            var size = LandscapeData.GetSize();
            if (!runtimeData.HeightMap.IsValid())
                runtimeData.HeightMap = CSTexture.Create(size.X, size.Y);
            if (!runtimeData.ControlMap.IsValid())
                runtimeData.ControlMap = CSTexture.Create(size.X, size.Y);

            // Avoid reading water data if not allocated
            if (!LandscapeData.WaterEnabled) changed.WaterMapChanged = false;

            var oldMetadata = runtimeData.LandscapeMeta;
            UpdateMetadata(changed);

            if (changed.HeightMapChanged) {
                var allRange = new RectI(0, 0, size.X, size.Y);
                // Expand dirty range by 1 because normals
                // If metadata changed, height needs to be renormalized
                var heightDirtyRange =
                    runtimeData.LandscapeMeta.HeightRangeChanged(oldMetadata) ? allRange :
                    range.Expand(1).ClampToBounds(allRange);
                UpdateHeightmap(heightDirtyRange);
            }
            if (changed.ControlMapChanged) {
                UpdateControlMap(range);
            }
            if (changed.WaterMapChanged && LandscapeData.WaterEnabled) {
                UpdateWaterMap(range);
            }

            // Mark the data as current
            mRevision = LandscapeData.Revision;
            SetMaterialProperties();
        }

        private void SetMaterialProperties() {
            // Calculate material parameters
            var transform = GetWorldMatrix();

            LandMaterial.SetValue("Model", transform);
            LandMaterial.SetTexture("HeightMap", runtimeData.HeightMap);
            LandMaterial.SetTexture("ControlMap", runtimeData.ControlMap);
            LandMaterial.SetValue("_LandscapeSizing", Properties.Sizing);
            LandMaterial.SetValue("_LandscapeScaling", Properties.Scaling);
            if (Layers != null) {
                Span<Vector4> layerData1 = stackalloc Vector4[Layers.LayerCount];
                Span<Vector4> layerData2 = stackalloc Vector4[Layers.LayerCount];
                for (int l = 0; l < Layers.LayerCount; ++l) {
                    var layer = Layers.TerrainLayers[l];
                    layerData1[l] = new Vector4(layer.Scale, layer.UvYScroll, layer.UniformMetal, layer.UniformSmoothness);
                    layerData2[l] = new Vector4(Math.Max(0.01f, layer.Fringe), 0f, 0f, 0f);
                }
                LandMaterial.SetValue("_LandscapeLayerData1", layerData1);
                LandMaterial.SetValue("_LandscapeLayerData2", layerData2);
            }
        }
        public Matrix4x4 GetWorldMatrix() {
            var scale = LandscapeData.GetScale();
            return Matrix4x4.CreateScale(scale, 1.0f, scale) *
                Matrix4x4.CreateTranslation(LandscapeData.GetSizing().Location);
        }

        unsafe private void UpdateMetadata(LandscapeChangeEvent changed) {
            // Get the heightmap data
            var chunkCount = (LandscapeData.Size - 1) / TileResolution + 1;
            if (chunkCount != runtimeData.ChunkCount) {
                runtimeData.ChunkCount = chunkCount;
                runtimeData.LandscapeChunkMeta = new HeightMetaData[chunkCount.X * chunkCount.Y];
                runtimeData.WaterChunkMeta = new HeightMetaData[chunkCount.X * chunkCount.Y];
                Array.Fill(runtimeData.LandscapeChunkMeta, HeightMetaData.Invalid);
                Array.Fill(runtimeData.WaterChunkMeta, HeightMetaData.Invalid);
            }

            var chunkMin = changed.Range.Min / TileResolution;
            var chunkMax = (changed.Range.Max - 1) / TileResolution;
            var heightMap = LandscapeData.GetHeightMap();
            var waterMap = LandscapeData.GetWaterMap();
            for (int cy = chunkMin.Y; cy <= chunkMax.Y; ++cy) {
                for (int cx = chunkMin.X; cx <= chunkMax.X; ++cx) {
                    if (changed.HeightMapChanged) {
                        runtimeData.LandscapeChunkMeta[cx + cy * chunkCount.X] =
                            ComputeMetadata(heightMap, new RectI(cx * TileResolution, cy * TileResolution, TileResolution, TileResolution));
                    }
                    if (changed.WaterMapChanged) {
                        runtimeData.WaterChunkMeta[cx + cy * chunkCount.X] =
                            ComputeMetadata(waterMap, new RectI(cx * TileResolution, cy * TileResolution, TileResolution, TileResolution));
                    }
                }
            }
            if (changed.HeightMapChanged) {
                runtimeData.LandscapeMeta = SummarizeMeta(runtimeData.LandscapeChunkMeta);
            }
            if (changed.WaterMapChanged) {
                runtimeData.WaterMeta = SummarizeMeta(runtimeData.WaterChunkMeta);
            }
        }
        private HeightMetaData SummarizeMeta(HeightMetaData[] metas) {
            int heightMin = int.MaxValue, heightMax = int.MinValue;
            foreach (var chunk in metas) {
                if (!chunk.IsValid) continue;
                heightMin = Math.Min(heightMin, chunk.MinHeight);
                heightMax = Math.Max(heightMax, chunk.MaxHeight);
            }
            return new HeightMetaData() { MinHeight = heightMin, HeightRange = heightMax - heightMin, };
        }
        unsafe private HeightMetaData ComputeMetadata<T>(T heightMap, RectI range) where T : IHeightmapReader {
            // Calculate min/max height range
            int heightMin = int.MaxValue, heightMax = int.MinValue;
            for (int y = range.Min.Y; y < range.Max.Y; ++y) {
                for (int x = range.Min.X; x < range.Max.X; ++x) {
                    var height = heightMap.GetHeightAt(new Int2(x, y));
                    heightMin = Math.Min(heightMin, height);
                    heightMax = Math.Max(heightMax, height);
                }
            }
            // Allow the shader to reconstruct the height range
            return new HeightMetaData() {
                MinHeight = heightMin,
                HeightRange = heightMax - heightMin,
            };
        }
        unsafe private HeightMetaData ComputeMetadata(LandscapeData.WaterMapReadOnly heightMap, RectI range) {
            // Calculate min/max height range
            int heightMin = int.MaxValue, heightMax = int.MinValue;
            for (int y = range.Min.Y; y < range.Max.Y; ++y) {
                for (int x = range.Min.X; x < range.Max.X; ++x) {
                    var height = heightMap[new Int2(x, y)];
                    if (!height.IsValid) continue;
                    heightMin = Math.Min(heightMin, height.Data);
                    heightMax = Math.Max(heightMax, height.Data);
                }
            }
            if (heightMin > heightMax) return HeightMetaData.Invalid;
            heightMin = LandscapeData.WaterCell.DataToHeight(heightMin);
            heightMax = LandscapeData.WaterCell.DataToHeight(heightMax);
            // Allow the shader to reconstruct the height range
            return new HeightMetaData() {
                MinHeight = heightMin,
                HeightRange = heightMax - heightMin,
            };
        }
        unsafe private void UpdateHeightmap(RectI range) {
            // Get the heightmap data
            var heightmap = LandscapeData.GetRawHeightMap();
            var sizing = LandscapeData.GetSizing();
            var heightMin = runtimeData.LandscapeMeta.MinHeight;
            var heightRange = Math.Max(1, runtimeData.LandscapeMeta.HeightRange);
            // Get the inner texture data
            var pxHeightData = runtimeData.HeightMap.GetTextureData();
            for (int y = range.Min.Y; y < range.Max.Y; ++y) {
                for (int x = range.Min.X; x < range.Max.X; ++x) {
                    var px = sizing.ToIndex(new Int2(x, y));
                    uint *c = &((uint*)pxHeightData.Data)[px];
                    // Pack height into first byte
                    ((byte*)c)[0] = (byte)(255 * (heightmap[px].Height - heightMin) / heightRange);
                    {
                        var h01 = heightmap[sizing.ToIndex(Int2.Clamp(new Int2(x - 1, y), 0, sizing.Size - 1))];
                        var h21 = heightmap[sizing.ToIndex(Int2.Clamp(new Int2(x + 1, y), 0, sizing.Size - 1))];
                        var h10 = heightmap[sizing.ToIndex(Int2.Clamp(new Int2(x, y - 1), 0, sizing.Size - 1))];
                        var h12 = heightmap[sizing.ToIndex(Int2.Clamp(new Int2(x, y + 1), 0, sizing.Size - 1))];
                        var nrm = new Vector3((float)(h01.Height - h21.Height), (float)sizing.Scale1024, (float)(h10.Height - h12.Height));
                        nrm = Vector3.Normalize(nrm);
                        // Pack normal into 2nd and 3rd
                        ((byte*)c)[1] = (byte)(127 + (nrm.X * 127));
                        ((byte*)c)[2] = (byte)(127 + (nrm.Z * 127));
                    }
                }
            }
            // Mark the texture as having been changed
            runtimeData.HeightMap.MarkChanged();
        }
        unsafe private void UpdateControlMap(RectI range) {
            if (Layers == null) return;
            var controlMap = LandscapeData.GetControlMap();
            var heightMap = LandscapeData.GetHeightMap();
            var pxControlMap = runtimeData.ControlMap.GetTextureData();
            for (int y = range.Min.Y; y < range.Max.Y; y++) {
                var yI = y * controlMap.Size.X;
                for (int x = range.Min.X; x < range.Max.X; x++) {
                    var pnt = new Int2(x, y);
                    var cell = controlMap[pnt];
                    var meta = Layers[cell.TypeId];
                    var sample = VoronoiIntNoise.GetNearest_2x2(pnt * 128);
                    var rnd = (uint)(sample.X + (sample.Y << 6));
                    rnd *= 0xA3C59AC3;
                    rnd ^= rnd >> 10;
                    var c = new Color(cell.TypeId, 0, 0, 255);
                    switch (meta.Alignment) {
                        case LandscapeLayer.AlignmentModes.Clustered: {
                            c.G = (byte)rnd;
                        }
                        break;
                        case LandscapeLayer.AlignmentModes.Random90: {
                            c.G = (byte)(rnd & 0xc0);
                        }
                        break;
                        case LandscapeLayer.AlignmentModes.Random: {
                            rnd = (uint)(pnt.X + (pnt.Y << 16));
                            rnd *= 0xA3C59AC3;
                            c.G = (byte)rnd;
                        }
                        break;
                        case LandscapeLayer.AlignmentModes.WithNormal: {
                            var dd = heightMap.GetDerivative(pnt);
                            if (dd.Equals(default)) {
                                c.G = (byte)rnd;
                            } else {
                                var ang = Math.Abs(dd.X) > Math.Abs(dd.Y)
                                    ? (dd.X < 0 ? 128 : 0) + 32 * -dd.Y / dd.X
                                    : (dd.Y < 0 ? 192 : 64) + 32 * dd.X / dd.Y;
                                c.G = (byte)(ang + meta.Rotation * 256 / 360);
                            }
                        }
                        break;
                    }
                    ((uint*)pxControlMap.Data)[yI + x] = c.Packed;
                }
            }
            // Mark the texture as having been changed
            runtimeData.ControlMap.MarkChanged();
        }
        unsafe private void UpdateWaterMap(RectI range) {
            var waterMap = LandscapeData.GetWaterMap();
            var pxHeightData = runtimeData.HeightMap.GetTextureData();
            for (int y = range.Min.Y; y < range.Max.Y; y++) {
                var yI = y * waterMap.Size.X;
                for (int x = range.Min.X; x < range.Max.X; x++) {
                    int i = yI + x;
                    var pnt = new Int2(x, y);
                    ref var c = ref ((Color*)pxHeightData.Data)[i];
                    c.A = waterMap[pnt].Data;
                }
            }
            // Mark the texture as having been changed
            runtimeData.HeightMap.MarkChanged();
        }
        unsafe private void UpdateChunkInstances(MeshDrawInstanced draw, Frustum localFrustum, HeightMetaData[] metadata, Int2 visMinI, Int2 visMaxI) {
            var items = stackalloc UShort2[Int2.CMul(visMaxI - visMinI)];
            int count = 0;
            int drawHash = 0;
            // Render the generated instances
            for (int y = visMinI.Y; y < visMaxI.Y; ++y) {
                for (int x = visMinI.X; x < visMaxI.X; ++x) {
                    var chunk = metadata[x + y * runtimeData.ChunkCount.X];
                    if (!chunk.IsValid) continue;
                    var value = new UShort2((ushort)(x * TileResolution), (ushort)(y * TileResolution));
                    var ctr = new Vector3((x + 0.5f) * TileResolution + 0.5f, 0f, (y + 0.5f) * TileResolution + 0.5f);
                    var ext = new Vector3((TileResolution + 1) / 2.0f, 0f, (TileResolution + 1) / 2.0f);
                    ctr.Y = chunk.HeightMedian / HeightScale;
                    ext.Y = chunk.HeightRange / 2f / HeightScale;
                    if (localFrustum.GetIsVisible(ctr, ext)) {
                        items[count++] = value;
                        drawHash = HashCode.Combine(drawHash, value);
                    }
                }
            }
            draw.SetInstanceData(items, count, 0, drawHash);
        }
        unsafe private void UpdateEdgeInstances(MeshDrawInstanced draw, Frustum localFrustum, HeightMetaData[] metadata, Int2 imin, Int2 imax) {
            var items = stackalloc UShort2[Int2.CSum(imax - imin) * 4 + 2];
            int count = 0;
            int drawHash = 0;
            var lmax = LandscapeData.Size / TileResolution;
            for (int a = 0; a < 2; a++) {
                var amin = a == 0 ? imin : imin.YX;
                var amax = a == 0 ? imax : imax.YX;
                var lsize = a == 0 ? lmax : lmax.YX;
                for (int d = 0; d < 2; d++) {
                    var apnt = d == 0 ? amin.Y : amax.Y;
                    if (apnt != (d == 0 ? 0 : lsize.Y)) continue;
                    var adir = d == 0 ? 1 : -1;
                    var afrm = d == 0 ? amin.X : amax.X;
                    var ato = d == 0 ? amax.X : amin.X;
                    for (int i = afrm; ; i += adir) {
                        var pnt = new Int2(i + ((d ^ a) == 0 ? 0 : 1), apnt + (d == 1 ? 1 : 0));
                        if (a == 1) pnt = pnt.YX;
                        var chunk = metadata[pnt.X + pnt.Y * runtimeData.ChunkCount.X];
                        if (chunk.IsValid) {
                            pnt.X = i * TileResolution;
                            pnt.Y = a + d * 2;
                            var value = new UShort2((ushort)pnt.X, (ushort)pnt.Y);
                            items[count++] = value;
                            drawHash = HashCode.Combine(drawHash, value);
                        }
                        if (i == ato) break;
                    }
                }
            }
            if (count > Int2.CSum(imax - imin)) {
                int a = 0;
            }
            draw.SetInstanceData(items, count, 0, drawHash);
        }
        unsafe public void Render(CSGraphics graphics, ScenePass pass) {
            var transform = GetWorldMatrix();
            var frustum = pass.Frustum;
            var localFrustum = frustum.TransformToLocal(transform);

            ApplyDataChanges();

            // Calculate the min/max AABB of the projected frustum 
            Int2 visMinI, visMaxI;
            {
                var heightMin = (float)runtimeData.LandscapeMeta.MinHeight / HeightScale;
                var heightMax = (float)runtimeData.LandscapeMeta.MaxHeight / HeightScale;
                Span<Vector3> points = stackalloc Vector3[8];
                localFrustum.IntersectPlane(Vector3.UnitY, heightMin, points.Slice(0, 4));
                localFrustum.IntersectPlane(Vector3.UnitY, heightMax, points.Slice(4, 4));
                Vector2 visMin = points[0].toxz();
                Vector2 visMax = points[0].toxz();
                foreach (var point in points) {
                    visMin = Vector2.Min(visMin, point.toxz());
                    visMax = Vector2.Max(visMax, point.toxz());
                }
                Int2 tileCount = (LandscapeData.GetSize() + TileResolution - 1) / TileResolution;
                visMinI = Int2.Max(Int2.FloorToInt(visMin / TileResolution), 0);
                visMaxI = Int2.Min(Int2.CeilToInt(visMax / TileResolution), tileCount - 1);
            }
            if (visMaxI.X <= visMinI.X || visMaxI.Y <= visMinI.Y) return;

            if (pass.TagsToInclude.Has(RenderTag.Default)) {
                UpdateChunkInstances(landscapeDraw, localFrustum, runtimeData.LandscapeChunkMeta, visMinI, visMaxI);
                UpdateEdgeInstances(edgeDraw, localFrustum, runtimeData.LandscapeChunkMeta, visMinI, visMaxI);
                landscapeDraw.Draw(graphics, pass, CSDrawConfig.MakeDefault());
                edgeDraw.Draw(graphics, pass, CSDrawConfig.MakeDefault());
            }
            if (pass.TagsToInclude.Has(RenderTag.Transparent) && LandscapeData.WaterEnabled) {
                UpdateChunkInstances(waterDraw, localFrustum, runtimeData.WaterChunkMeta, visMinI, visMaxI);
                UpdateEdgeInstances(waterEdgeDraw, localFrustum, runtimeData.WaterChunkMeta, visMinI, visMaxI);
                waterDraw.Draw(graphics, pass, CSDrawConfig.MakeDefault());
                waterEdgeDraw.Draw(graphics, pass, CSDrawConfig.MakeDefault());
            }
        }
    }
}
