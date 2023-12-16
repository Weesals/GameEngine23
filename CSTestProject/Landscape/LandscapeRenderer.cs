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
            public HeightMetaData Metadata;
            public LandscapeChangeEvent Changed;
            public PropertiesData Properties;
            public BoundingBox Bounds;

            //public Texture2DArray BaseTextures;
            //public Texture2DArray BumpTextures;

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
        int landscapeDrawHash;
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
            if (LandMaterial == null) {
                LandMaterial = new Material("./assets/landscape.hlsl");
				if (rootMaterial != null) LandMaterial.InheritProperties(rootMaterial);

                var baseColor = CSTexture.Create()
                    .SetSize(512)
                    .SetArrayCount(Layers.LayerCount);
                var normals = CSTexture.Create()
                    .SetSize(256)
                    .SetArrayCount(Layers.LayerCount);
                for (int i = 0; i < Layers.LayerCount; ++i) {
                    CSResources.LoadTexture(Layers[i].BaseColor)
                        .GetTextureData()
                        .CopyTo(baseColor.GetTextureData(0, i));
                    CSResources.LoadTexture(Layers[i].NormalMap)
                        .GetTextureData()
                        .CopyTo(normals.GetTextureData(0, i));
                }
                baseColor.MarkChanged();
                normals.MarkChanged();
                baseColor.GenerateMips();
                normals.GenerateMips();
                LandMaterial.SetTexture("BaseMaps", baseColor);
                LandMaterial.SetTexture("BumpMaps", normals);
            }
            if (tileMesh == null) tileMesh = LandscapeUtility.GenerateSubdividedQuad(TileResolution, TileResolution);
            if (edgeMesh == null) edgeMesh = LandscapeUtility.GenerateSubdividedQuad(TileResolution, 1, false, true, true);

            LandscapeData.OnLandscapeChanged += LandscapeChanged;
			landscapeDraw = new MeshDrawInstanced(tileMesh, LandMaterial);
            landscapeDraw.AddInstanceElement("INSTANCE", BufferFormat.FORMAT_R16G16_UINT);
            WaterMaterial = new("./assets/water.hlsl");
            if (rootMaterial != null) WaterMaterial.InheritProperties(LandMaterial);
            WaterMaterial.SetTexture("NoiseTex", CSResources.LoadTexture("./assets/Noise.jpg"));
            WaterMaterial.SetTexture("FoamTex", CSResources.LoadTexture("./assets/FoamMask.jpg"));
            WaterMaterial.SetBlendMode(BlendMode.MakeAlphaBlend());
            waterDraw = new MeshDrawInstanced(tileMesh, WaterMaterial);
            waterDraw.AddInstanceElement("INSTANCE", BufferFormat.FORMAT_R16G16_UINT);
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
                var metadata = runtimeData.Metadata;
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
        private void UpdateRange(LandscapeChangeEvent changeEvent) {
            var range = changeEvent.Range;
            var size = LandscapeData.GetSize();
            if (!runtimeData.HeightMap.IsValid())
                runtimeData.HeightMap = CSTexture.Create(size.X, size.Y);
            if (!runtimeData.ControlMap.IsValid())
                runtimeData.ControlMap = CSTexture.Create(size.X, size.Y);

            var allRange = new RectI(0, 0, size.X, size.Y);

            if (changeEvent.HeightMapChanged) {
                var heightDirtyRange = new RectI(range.X - 1, range.Y - 1, range.Width + 2, range.Height + 2);
                heightDirtyRange = heightDirtyRange.ClampToBounds(allRange);
                var newMetadata = ComputeMetadata(heightDirtyRange);
                // If metadata changed, we have to update the entire heightmap
                // (height needs to be renormalized)
                if (!runtimeData.Metadata.Equals(newMetadata)) {
                    heightDirtyRange = allRange;
                    runtimeData.Metadata = newMetadata;
                }
                UpdateHeightmap(heightDirtyRange);
            }
            if (changeEvent.ControlMapChanged) {
                UpdateControlMap(range);
            }
            if (changeEvent.WaterMapChanged && LandscapeData.WaterEnabled) {
                UpdateWaterMap(range);
            }

            // Mark the data as current
            mRevision = LandscapeData.Revision;
            SetMaterialProperties();
        }

        private void SetMaterialProperties() {
            // Calculate material parameters
            var scale = LandscapeData.GetScale();
            var xform = Matrix4x4.CreateScale(scale, 1.0f, scale) *
                Matrix4x4.CreateTranslation(LandscapeData.GetSizing().Location);

            LandMaterial.SetValue("Model", xform);
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

        unsafe private HeightMetaData ComputeMetadata(RectI range) {
            // Get the heightmap data
            var heightmap = LandscapeData.GetRawHeightMap();
            // Calculate min/max height range
            int heightMin = int.MaxValue, heightMax = int.MinValue;
            foreach (var height in heightmap) {
                heightMin = Math.Min(heightMin, height.Height);
                heightMax = Math.Max(heightMax, height.Height);
            }
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
            var heightMin = runtimeData.Metadata.MinHeight;
            var heightRange = Math.Max(1, runtimeData.Metadata.HeightRange);
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
                    //var pos = BurstLibNoise.Generator.Voronoi.GetBurstPosition2D(pnt.x * 0.2f, pnt.y * 0.2f, 1);
                    //var rnd = (uint)pos.x.GetHashCode();
                    var sample = VoronoiIntNoise.GetNearest_2x2(pnt * 128);
                    var rnd = (uint)(sample.X + (sample.Y << 6));
                    rnd *= 0xA3C59AC3;
                    rnd ^= rnd >> 10;
                    var c = new Color(cell.TypeId, 0, 0, 255);
                    switch (meta.Alignment) {
                        case LandscapeLayer.AlignmentModes.Clustered: {
                            //c.G = (byte)(sample.Z / 512);
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
                    //rnd ^= rnd << 3;
                    //rnd ^= rnd >> 5;
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
        unsafe public void Render(CSGraphics graphics, ScenePass pass) {
            var scale = LandscapeData.GetScale();
            var xform = Matrix4x4.CreateScale(scale, 1.0f, scale) *
                Matrix4x4.CreateTranslation(LandscapeData.GetSizing().Location);
            var frustum = pass.Frustum;
            var viewProj = frustum.CalculateViewProj();
            var localFrustum = frustum.TransformToLocal(xform);

            ApplyDataChanges();

            // How many tile instances we need to render
            Int2 tileCount = (LandscapeData.GetSize() + TileResolution - 1) / TileResolution;

            // Calculate the min/max AABB of the projected frustum 
            Vector2 visMin, visMax;
            var heightMin = (float)runtimeData.Metadata.MinHeight / HeightScale;
            var heightMax = (float)runtimeData.Metadata.MaxHeight / HeightScale;
            {
                Span<Vector3> points = stackalloc Vector3[8];
                localFrustum.IntersectPlane(Vector3.UnitY, heightMin, points.Slice(0, 4));
                localFrustum.IntersectPlane(Vector3.UnitY, heightMax, points.Slice(4, 4));
                visMin = points[0].toxz();
                visMax = points[0].toxz();
                foreach (var point in points) {
                    visMin = Vector2.Min(visMin, point.toxz());
                    visMax = Vector2.Max(visMax, point.toxz());
                }
            }
            Int2 visMinI = Int2.Max(Int2.FloorToInt(visMin / TileResolution), (Int2)0);
            Int2 visMaxI = Int2.Min(Int2.CeilToInt(visMax / TileResolution), tileCount - 1);
            if (visMaxI.X <= visMinI.X || visMaxI.Y <= visMinI.Y) return;

            var instanceOffsets = graphics.RequireFrameData<UShort2>(Int2.CMul(visMaxI - visMinI));
            int i = 0;
            // Render the generated instances
            for (int y = visMinI.Y; y < visMaxI.Y; ++y) {
                for (int x = visMinI.X; x < visMaxI.X; ++x) {
                    var value = new UShort2((ushort)(x * TileResolution), (ushort)(y * TileResolution));
                    var ctr = new Vector3((x + 0.5f) * TileResolution + 0.5f, 0f, (y + 0.5f) * TileResolution + 0.5f);
                    var ext = new Vector3((TileResolution + 1) / 2.0f, 0f, (TileResolution + 1) / 2.0f);
                    ctr.Y += (heightMax + heightMin) / 2f;
                    ext.Y += (heightMax - heightMin) / 2f;
                    if (!localFrustum.GetIsVisible(ctr, ext)) continue;
                    instanceOffsets[i] = value;
                    ++i;
                }
            }
            // TODO: Return the unused per-frame data?

            var drawHash = 0;
            foreach (var item in instanceOffsets) drawHash = HashCode.Combine(drawHash, item);
            var tagTrans = pass.Scene.TagManager.RequireTag("Transparent");
            if (pass.TagsToInclude.Has(RenderTag.Default)) {
                landscapeDraw.SetInstanceData(instanceOffsets.Data, i, 0, drawHash != landscapeDrawHash);
                landscapeDraw.Draw(graphics, pass, CSDrawConfig.MakeDefault());
            }
            if (pass.TagsToInclude.Has(tagTrans)) {
                if (LandscapeData.WaterEnabled) {
                    waterDraw.SetInstanceData(instanceOffsets.Data, i, 0, drawHash != landscapeDrawHash);
                    waterDraw.Draw(graphics, pass, CSDrawConfig.MakeDefault());
                }
            }

            landscapeDrawHash = drawHash;
        }


    }
}
