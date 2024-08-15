using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Weesals.Editor;
using Weesals.Engine;
using Weesals.Engine.Jobs;
using Weesals.Engine.Profiling;
using Weesals.Geometry;
using Weesals.Utility;

/* Bulge grass over edge of terrain and blend with terrain edge
 * Add stalactites below terrain
 */

namespace Weesals.Landscape {
    public class LandscapeRenderer {

        public const int TileSize = 8;
        public const int HeightScale = LandscapeData.HeightScale;

        private static ProfilerMarker ProfileMarker_Render = new("Render Landscape");
        private static ProfilerMarker ProfileMarker_UpdateChunks = new("Update Chunks");
        private static ProfilerMarker ProfileMarker_UpdateEdges = new("Update Edges");

        public interface ILandscapeDataListener {
            void NotifyDataChanged(LandscapeData odata, LandscapeData ndata);
        }

        public LandscapeData LandscapeData { get; private set; }
        public LandscapeLayerCollection Layers => LandscapeData.Layers;
        public LandscapeEdgeRenderer EdgeRenderer;

        public Material LandMaterial;
        public Material WaterMaterial;

        // The mesh that is instanced across the surface
        Mesh tileMesh;

        BufferLayoutPersistent layerDataBuffer;

        public struct HeightMetaData {
            public int MinHeight;
            public int HeightRange;
            public int HeightMedian => MinHeight + HeightRange / 2;
            public int MaxHeight => MinHeight + HeightRange;
            public float MinHeightF => (float)MinHeight / HeightScale;
            public float MaxHeightF => (float)MaxHeight / HeightScale;
            public float HeightMedianF => ((float)MinHeight + HeightRange / 2.0f) / HeightScale;
            public float HeightRangeF => (float)HeightRange / HeightScale;
            public bool IsValid => HeightRange >= 0;
            public static readonly HeightMetaData Invalid = new HeightMetaData() { MinHeight = int.MaxValue, HeightRange = -1, };

            public bool HeightRangeChanged(HeightMetaData value) {
                return (value.MinHeight != MinHeight || value.MaxHeight != MaxHeight);
            }
        }
        public struct PropertiesData {
            public Vector4 Sizing;
            public Vector4 Sizing1;
            public Vector4 Scaling;
            public int Hash;
            public PropertiesData(Vector4 sizing, Vector4 scaling) {
                Sizing = sizing;
                Sizing1 = new Vector4(1f / (sizing.X + 1), 1f / (sizing.Y + 1), 0f, 0f);
                Sizing1.Z = Sizing1.X * 0.5f;
                Sizing1.W = Sizing1.Y * 0.5f;
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
        public CSTexture HeightMap => runtimeData.HeightMap;
        public CSTexture ControlMap => runtimeData.ControlMap;
        public Int2 ChunkCount => runtimeData.ChunkCount;

        private List<ILandscapeDataListener> listeners = new();

        struct PassCache {
            public readonly ScenePass Pass;
            public MeshDrawInstanced landscapeDraw;
            public MeshDrawInstanced waterDraw;
            public PassCache(ScenePass pass) { Pass = pass; }
        }
        private PooledList<PassCache> passCache = new();

        int materialPropHash;
        int mRevision;
        JobHandle loadHandle;

        private bool enableStochastic = true;
        private bool highQualityBlend = true;
        private bool secondVariant = false;
        private bool complexity = false;
        private bool parallax = true;
        [EditorField]
        public bool HighQualityBlend {
            get => highQualityBlend;
            set {
                LandMaterial.SetPixelShader(Resources.LoadShader((highQualityBlend = value) ? "./Assets/landscape3x3.hlsl" : "./Assets/landscape.hlsl", "PSMain"));
                LandMaterial.SetVertexShader(Resources.LoadShader((highQualityBlend = value) ? "./Assets/landscape3x3.hlsl" : "./Assets/landscape.hlsl", "VSMain"));
            }
        }
        [EditorField]
        public bool EnableStochastic {
            get => enableStochastic;
            set { enableStochastic = value; runtimeData.Changed.CombineWith(LandscapeChangeEvent.MakeAll(LandscapeData.Size)); }
        }
        [EditorField]
        public bool SecondVariant {
            get => secondVariant;
            set => LandMaterial.SetMacro("VARIANT", (secondVariant = value) ? "1" : CSIdentifier.Invalid);
        }
        [EditorField]
        public bool Complexity {
            get => complexity;
            set => LandMaterial.SetMacro("COMPLEXITY", (complexity = value) ? "1" : CSIdentifier.Invalid);
        }
        [EditorField]
        public bool Parallax {
            get => parallax;
            set => LandMaterial.SetMacro("ENABLEPARALLAX", (parallax = value) ? "1" : CSIdentifier.Invalid);
        }

        public LandscapeRenderer() {
            loadHandle = JobHandle.Schedule(() => {
                using var marker = new ProfilerMarker("Gen TerQuad").Auto();
                tileMesh = LandscapeUtility.GenerateSubdividedQuad(TileSize, TileSize);
                var landMaterial = new Material("./Assets/landscape3x3.hlsl");
                landMaterial.SetMeshShader(Resources.LoadShader("./Assets/landscape3x3.hlsl", "MSMain"));
                landMaterial.SetBuffer("Vertices", tileMesh.VertexBuffer);
                landMaterial.SetBuffer("Indices", tileMesh.IndexBuffer);
                WaterMaterial = new("./Assets/water.hlsl", landMaterial);
                //WaterMaterial.SetBlendMode(BlendMode.MakeAlphaBlend());
                //WaterMaterial.SetDepthMode(DepthMode.MakeReadOnly());
                //EdgeRenderer = new LandscapeEdgeRenderer(this);
                LandMaterial = landMaterial;
                HighQualityBlend = HighQualityBlend;
                SecondVariant = SecondVariant;
                Complexity = Complexity;
                Parallax = Parallax;
            });
        }

        unsafe public void Initialise(LandscapeData landscapeData, Material rootMaterial) {
            LandscapeData = landscapeData;
            runtimeData.Dispose();
            runtimeData = new() {
                Changed = LandscapeChangeEvent.MakeAll(landscapeData.Size),
                Bounds = BoundingBox.FromMinMax(Vector3.Zero, landscapeData.Sizing.LandscapeToWorld(landscapeData.Size)),
            };

            layerDataBuffer = new BufferLayoutPersistent(BufferLayoutPersistent.Usages.Uniform);
            layerDataBuffer.AppendElement(new CSBufferElement("DATA1", BufferFormat.FORMAT_R32G32B32A32_FLOAT));
            layerDataBuffer.AppendElement(new CSBufferElement("DATA2", BufferFormat.FORMAT_R32G32B32A32_FLOAT));

            //if (rootMaterial != null) LandMaterial.InheritProperties(rootMaterial);
            var handle = JobHandle.Schedule(() => {
                using var marker = new ProfilerMarker("Load TerMaps").Auto();
                runtimeData.BaseTextures = Layers.RequireBaseMaps(true);
                runtimeData.BumpTextures = Layers.RequireBumpMaps(true);
                var noise = Resources.LoadTexture("./Assets/Noise.jpg");
                var foam = Resources.LoadTexture("./Assets/FoamMask.jpg");
                JobHandle.RunOnMain((_) => {
                    LandMaterial.SetTexture("BaseMaps", runtimeData.BaseTextures);
                    LandMaterial.SetTexture("BumpMaps", runtimeData.BumpTextures);
                    WaterMaterial.SetTexture("NoiseTex", noise);
                    WaterMaterial.SetTexture("FoamTex", foam);
                });
            }, loadHandle);

            LandscapeData.OnLandscapeChanged += LandscapeChanged;
        }

        private void InitialisePassCache(ref PassCache cache) {
            cache.landscapeDraw = new MeshDrawInstanced(tileMesh, LandMaterial) {
                RenderOrder = 100,
            };
            cache.landscapeDraw.AddInstanceElement("INSTANCE", BufferFormat.FORMAT_R16G16_SINT);
            cache.waterDraw = new MeshDrawInstanced(tileMesh, WaterMaterial);
            cache.waterDraw.AddInstanceElement("INSTANCE", BufferFormat.FORMAT_R16G16_SINT);
        }

        private void LandscapeChanged(LandscapeData data, LandscapeChangeEvent changed) {
            runtimeData.Changed.CombineWith(changed);
        }

        // Check if anything has changed, and apply changes
        public void ApplyDataChanges() {
            loadHandle.Complete(); loadHandle = default;
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
                using var profileTerrain = new ProfilerMarker("Terrain Update").Auto();

                var dependency = UpdateRange(runtimeData.Changed);
                dependency.Complete();

                // Mark the data as current
                mRevision = LandscapeData.Revision;
                runtimeData.Changed = LandscapeChangeEvent.MakeNone();

                var metadata = runtimeData.LandscapeMeta;
                // Sub 1 because 1x1 cell requires 2x2 texels
                var size = LandscapeData.Size - 1;
                var scaleF = 1024f / LandscapeData.Sizing.Scale1024;
                runtimeData.Properties = new PropertiesData(
                    new Vector4(size.X, size.Y, 1f / size.X, 1f / size.Y),
                    new Vector4(scaleF, 1f / scaleF, metadata.MinHeightF, metadata.HeightRangeF)
                );
                requireMaterialUpdates = true;
            }
            if (requireMaterialUpdates) SetMaterialProperties();
        }
        private JobHandle UpdateRange(LandscapeChangeEvent changed) {
            // Avoid reading water data if not allocated
            if (!LandscapeData.WaterEnabled) changed.WaterMapChanged = false;

            var oldMetadata = runtimeData.LandscapeMeta;
            JobHandle metaDependency = UpdateMetadata(changed);

            var size = LandscapeData.GetSize();
            if (!runtimeData.HeightMap.IsValid)
                runtimeData.HeightMap = CSTexture.Create("HeightMap", size.X, size.Y);
            if (!runtimeData.ControlMap.IsValid)
                runtimeData.ControlMap = CSTexture.Create("ControlMap", size.X, size.Y, BufferFormat.FORMAT_R16_UINT);

            JobHandle heightDep = default;
            JobHandle controlDep = default;

            if (changed.ControlMapChanged) {
                controlDep = JobHandle.CombineDependencies(controlDep,
                    UpdateControlMap(changed.Range, default));
            }
            if (changed.WaterMapChanged && LandscapeData.WaterEnabled) {
                heightDep = UpdateWaterMap(changed.Range, heightDep);
            }
            if (changed.HeightMapChanged) {
                metaDependency.Complete();
                runtimeData.Bounds.Min.Y = runtimeData.LandscapeMeta.MinHeightF;
                runtimeData.Bounds.Max.Y = runtimeData.LandscapeMeta.MaxHeightF;

                // If metadata changed, height needs to be renormalized
                heightDep = UpdateHeightmap(changed.Range, oldMetadata, heightDep);
            }

            var dependency = JobHandle.CombineDependencies(metaDependency,
                JobHandle.CombineDependencies(heightDep, controlDep));

            return dependency;
        }

        private void SetMaterialProperties() {
            // Calculate material parameters
            LandMaterial.SetValue("Model", GetWorldMatrix());
            LandMaterial.SetTexture("HeightMap", runtimeData.HeightMap);
            LandMaterial.SetTexture("ControlMap", runtimeData.ControlMap);
            LandMaterial.SetValue("_LandscapeSizing", Properties.Sizing);
            LandMaterial.SetValue("_LandscapeSizing1", Properties.Sizing1);
            LandMaterial.SetValue("_LandscapeScaling", Properties.Scaling);
            if (Layers != null) {
                if (layerDataBuffer.BufferCapacityCount < Layers.LayerCount)
                    layerDataBuffer.AllocResize(Layers.LayerCount);
                layerDataBuffer.SetCount(Layers.LayerCount);
                var data1 = new TypedBufferView<Vector4>(layerDataBuffer.Elements[0], Layers.LayerCount);
                var data2 = new TypedBufferView<Vector4>(layerDataBuffer.Elements[1], Layers.LayerCount);
                for (int l = 0; l < Layers.LayerCount; ++l) {
                    var layer = Layers.TerrainLayers[l];
                    data1[l] = new Vector4(layer.Scale, layer.UvYScroll, Math.Max(0.01f, layer.Fringe), layer.UniformRoughness);
                    data2[l] = new Vector4(layer.UniformMetal, 0f, 0f, 0f);
                }
                layerDataBuffer.NotifyChanged();
                LandMaterial.SetBuffer("_LandscapeLayerData", layerDataBuffer);
            }
        }
        public Matrix4x4 GetWorldMatrix() {
            var scale = LandscapeData.GetScale();
            return Matrix4x4.CreateScale(scale, 1.0f, scale) *
                Matrix4x4.CreateTranslation(LandscapeData.GetSizing().Location);
        }
        public HeightMetaData GetLandscapeMeta() { return runtimeData.LandscapeMeta; }
        public HeightMetaData GetWaterMeta() { return runtimeData.WaterMeta; }
        public HeightMetaData[] GetLandscapeChunkMeta() { return runtimeData.LandscapeChunkMeta; }
        public HeightMetaData[] GetWaterChunkMeta() { return runtimeData.WaterChunkMeta; }

        unsafe private JobHandle UpdateMetadata(LandscapeChangeEvent changed, JobHandle dependency = default) {
            // Get the heightmap data
            var chunkCount = (LandscapeData.Size - 2) / TileSize + 1;
            if (chunkCount != runtimeData.ChunkCount) {
                runtimeData.ChunkCount = chunkCount;
                runtimeData.LandscapeChunkMeta = new HeightMetaData[chunkCount.X * chunkCount.Y];
                runtimeData.WaterChunkMeta = new HeightMetaData[chunkCount.X * chunkCount.Y];
                Array.Fill(runtimeData.LandscapeChunkMeta, HeightMetaData.Invalid);
                Array.Fill(runtimeData.WaterChunkMeta, HeightMetaData.Invalid);
            }

            var chunkMin = (changed.Range.Min - 1) / TileSize;  // -1 because chunks range +1 beyond their max
            var chunkMax = (changed.Range.Max - 1) / TileSize + 1;
            var heightMap = LandscapeData.GetHeightMap();
            var waterMap = LandscapeData.GetWaterMap();
            JobHandle resultDep = default;
            if (changed.HeightMapChanged) {
                var landDep = JobHandle.ScheduleBatch((range) => {
                    foreach (var cy in range) {
                        for (int cx = chunkMin.X; cx < chunkMax.X; ++cx) {
                            runtimeData.LandscapeChunkMeta[cx + cy * chunkCount.X] =
                                ComputeMetadata(heightMap, new RectI(cx * TileSize, cy * TileSize, TileSize, TileSize));
                        }
                    }
                }, RangeInt.FromBeginEnd(chunkMin.Y, chunkMax.Y), dependency);
                resultDep = JobHandle.Schedule(() => {
                    using var marker = new ProfilerMarker("Ter LandMeta").Auto();
                    runtimeData.LandscapeMeta = SummarizeMeta(runtimeData.LandscapeChunkMeta);
                }, landDep);
            }
            if (changed.WaterMapChanged) {
                var waterDep = JobHandle.ScheduleBatch((range) => {
                    foreach (var cy in range) {
                        for (int cx = chunkMin.X; cx < chunkMax.X; ++cx) {
                            runtimeData.WaterChunkMeta[cx + cy * chunkCount.X] =
                                ComputeMetadata(waterMap, new RectI(cx * TileSize, cy * TileSize, TileSize, TileSize));
                        }
                    }
                }, RangeInt.FromBeginEnd(chunkMin.Y, chunkMax.Y), dependency);
                waterDep = JobHandle.Schedule(() => {
                    using var marker = new ProfilerMarker("Ter WaterMeta").Auto();
                    runtimeData.WaterMeta = SummarizeMeta(runtimeData.WaterChunkMeta);
                }, waterDep);
                resultDep = JobHandle.CombineDependencies(resultDep, waterDep);
            }
            return resultDep;
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
            for (int y = range.Min.Y; y <= range.Max.Y; ++y) {
                for (int x = range.Min.X; x <= range.Max.X; ++x) {
                    var height = heightMap.GetHeightAt(new Int2(x, y));
                    heightMin = Math.Min(heightMin, height);
                    heightMax = Math.Max(heightMax, height);
                }
            }
            // Allow the shader to reconstruct the height range
            return new HeightMetaData() { MinHeight = heightMin, HeightRange = heightMax - heightMin, };
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
        unsafe private JobHandle UpdateHeightmap(RectI range, HeightMetaData oldMetadata, JobHandle dependency) {
            // Get the heightmap data
            var heightmap = LandscapeData.GetRawHeightMap();
            var sizing = LandscapeData.GetSizing();
            var allRange = new RectI(0, 0, sizing.Size.X, sizing.Size.Y);
            // Get the inner texture data
            var pxHeightData = runtimeData.HeightMap.GetTextureData();
            var metadata = runtimeData.LandscapeMeta;
            var heightScale = 255f / MathF.Max(1f, metadata.MaxHeight - metadata.MinHeight);
            var heightBias = -metadata.MinHeight * heightScale;
            // Expand dirty range by 1 because normals
            range = metadata.HeightRangeChanged(oldMetadata) ? allRange
                : range.Expand(1).ClampToBounds(allRange);
            dependency = JobHandle.ScheduleBatch((yrange) => {
                float normalScale = 1.0f / LandscapeData.HeightScale * 1024.0f / (2.0f * sizing.Scale1024);
                normalScale *= 16.0f;
                foreach (var y in yrange) {
                    for (int x = range.Min.X; x < range.Max.X; ++x) {
                        var px = sizing.ToIndex(new Int2(x, y));
                        uint* c = &((uint*)pxHeightData.Data)[px];
                        // Pack height into first byte
                        ((byte*)c)[0] = (byte)(heightmap[px].Height * heightScale + heightBias);
                        var h01 = heightmap[sizing.ToIndex(Int2.Clamp(new Int2(x - 1, y), 0, sizing.Size - 1))];
                        var h21 = heightmap[sizing.ToIndex(Int2.Clamp(new Int2(x + 1, y), 0, sizing.Size - 1))];
                        var h10 = heightmap[sizing.ToIndex(Int2.Clamp(new Int2(x, y - 1), 0, sizing.Size - 1))];
                        var h12 = heightmap[sizing.ToIndex(Int2.Clamp(new Int2(x, y + 1), 0, sizing.Size - 1))];
                        ((byte*)c)[1] = (byte)Math.Clamp(127 + (h01.Height - h21.Height) * normalScale, 0, 255);
                        ((byte*)c)[2] = (byte)Math.Clamp(127 + (h10.Height - h12.Height) * normalScale, 0, 255);
                    }
                }
            }, new RangeInt(range.Min.Y, range.Size.Y), dependency);
            dependency = JobHandle.Schedule(() => {
                // Mark the texture as having been changed
                runtimeData.HeightMap.MarkChanged();
            }, dependency);
            return dependency;
        }
        unsafe private JobHandle UpdateControlMap(RectI range, JobHandle dependency) {
            if (Layers == null) return dependency;
            var controlMap = LandscapeData.GetControlMap();
            var heightMap = LandscapeData.GetHeightMap();
            var pxControlMap = runtimeData.ControlMap.GetTextureData();
            dependency = JobHandle.ScheduleBatch((yrange) => {
                foreach (var y in yrange) {
                    var yI = y * controlMap.Size.X;
                    for (int x = range.Min.X; x < range.Max.X; x++) {
                        var pnt = new Int2(x, y);
                        var cell = controlMap[pnt];
                        var layer = Layers[cell.TypeId];
                        uint rnd = 0;
                        if (enableStochastic) {
                            var sample = VoronoiIntNoise.GetNearest_2x2(pnt * 128);
                            rnd = (uint)(sample.X + (sample.Y << 11));
                        }
                        ushort color = 0;
                        //var c = new Color(cell.TypeId, 0, 0, 255);
                        switch (layer.Alignment) {
                            case LandscapeLayer.AlignmentModes.Clustered: {
                                rnd *= 0xA3C59AC3;
                                rnd ^= rnd >> 15;
                                color = (byte)rnd;
                            }
                            break;
                            case LandscapeLayer.AlignmentModes.Random90: {
                                rnd += rnd >> 10;
                                color = (byte)(rnd << 6);
                            }
                            break;
                            case LandscapeLayer.AlignmentModes.Random: {
                                rnd = (uint)(pnt.X + (pnt.Y << 16));
                                rnd *= 0xA3C59AC3;
                                rnd ^= rnd >> 15;
                                color = (byte)rnd;
                            }
                            break;
                            case LandscapeLayer.AlignmentModes.WithNormal: {
                                var dd = heightMap.GetDerivative(pnt);
                                if (dd.Equals(default)) {
                                    rnd *= 0xA3C59AC3;
                                    rnd ^= rnd >> 15;
                                    color = (byte)rnd;
                                } else {
                                    var ang = Math.Abs(dd.X) > Math.Abs(dd.Y)
                                        ? (dd.X < 0 ? 128 : 0) + 32 * -dd.Y / dd.X
                                        : (dd.Y < 0 ? 192 : 64) + 32 * dd.X / dd.Y;
                                    color = (byte)((int)(ang + layer.Rotation * 256 / 360 + 8) & 0xf8);
                                }
                            }
                            break;
                        }
                        color |= (ushort)(cell.TypeId << 8);
                        //c.A = (byte)(((y & 0x01) << 1) | (x & 0x01));
                        ((ushort*)pxControlMap.Data)[yI + x] = color;
                    }
                }
            }, new RangeInt(range.Min.Y, range.Size.Y), dependency);
            dependency = JobHandle.Schedule(() => {
                // Mark the texture as having been changed
                runtimeData.ControlMap.MarkChanged();
            }, dependency);
            return dependency;
        }
        unsafe private JobHandle UpdateWaterMap(RectI range, JobHandle dependency) {
            var waterMap = LandscapeData.GetWaterMap();
            var pxHeightData = runtimeData.HeightMap.GetTextureData();
            dependency = JobHandle.ScheduleBatch((yrange) => {
                foreach (var y in yrange) {
                    var yI = y * waterMap.Size.X;
                    for (int x = range.Min.X; x < range.Max.X; x++) {
                        int i = yI + x;
                        var pnt = new Int2(x, y);
                        ref var c = ref ((Color*)pxHeightData.Data)[i];
                        c.A = waterMap[pnt].Data;
                    }
                }
            }, new RangeInt(range.Min.Y, range.Size.Y), dependency);
            dependency = JobHandle.Schedule(() => {
                // Mark the texture as having been changed
                runtimeData.HeightMap.MarkChanged();
            }, dependency);
            return dependency;
        }
        unsafe private void UpdateChunkInstances(MeshDrawInstanced draw, Frustum localFrustum, HeightMetaData[] metadata, Int2 visMinI, Int2 visMaxI) {
            using var marker = ProfileMarker_UpdateChunks.Auto();
            var items = stackalloc Short2[Int2.CMul(visMaxI - visMinI)];
            int count = 0;
            int drawHash = 0;
            // Render the generated instances
            for (int y = visMinI.Y; y < visMaxI.Y; ++y) {
                for (int x = visMinI.X; x < visMaxI.X; ++x) {
                    var chunk = metadata[x + y * runtimeData.ChunkCount.X];
                    if (!chunk.IsValid) continue;
                    var value = new Short2((short)(x * TileSize), (short)(y * TileSize));
                    var ctr = new Vector3((x + 0.5f) * TileSize + 0.5f, (float)chunk.HeightMedianF, (y + 0.5f) * TileSize + 0.5f);
                    var ext = new Vector3((TileSize + 1) / 2.0f, (float)chunk.HeightRangeF / 2f, (TileSize + 1) / 2.0f);
                    if (localFrustum.GetIsVisible(ctr, ext)) {
                        items[count++] = value;
                        drawHash = HashCode.Combine(drawHash, value);
                    }
                    //int rnd = (x + y * 32) * 123456789; rnd ^= rnd >> 16;
                    //Gizmos.DrawWireCube(ctr, ext * 2.0f, new Color((uint)rnd | 0xff000000));
                }
            }
            draw.SetInstanceData(items, count, 0, drawHash);
        }
        unsafe private void UpdateEdgeInstances(MeshDrawInstanced draw, Frustum localFrustum, HeightMetaData[] metadata, Int2 imin, Int2 imax) {
            using var marker = ProfileMarker_UpdateEdges.Auto();
            int maxCount = Int2.CSum(imax - imin + 1) * 4;
            var itemsRaw = stackalloc Short2[maxCount];
            var items = new Span<Short2>(itemsRaw, maxCount);
            int count = 0;
            int drawHash = 0;
            var lmax = runtimeData.ChunkCount;
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
                        var chunk = metadata[pnt.X + pnt.Y * runtimeData.ChunkCount.X];
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
        private ConvexHull hull = new();
        private void IntlIntersectLocalFrustum(Frustum localFrustum, out Vector2 visMin, out Vector2 visMax) {
            var meta = runtimeData.LandscapeMeta;
            hull.FromFrustum(localFrustum);
            hull.Slice(new Plane(Vector3.UnitY, -meta.MinHeightF));
            hull.Slice(new Plane(-Vector3.UnitY, Math.Max(meta.MinHeightF + 0.1f, meta.MaxHeightF)));
            var bounds = hull.GetAABB();
            visMin = bounds.Min.toxz();
            visMax = bounds.Max.toxz();
            /*Span<Vector3> points = stackalloc Vector3[8];
            localFrustum.IntersectPlane(Vector3.UnitY, meta.MinHeightF, points.Slice(0, 4));
            localFrustum.IntersectPlane(Vector3.UnitY, meta.MaxHeightF, points.Slice(4, 4));
            visMin = points[0].toxz();
            visMax = points[0].toxz();
            foreach (var point in points) {
                visMin = Vector2.Min(visMin, point.toxz());
                visMax = Vector2.Max(visMax, point.toxz());
            }*/
        }
        public BoundingBox GetVisibleBounds(Frustum frustum) {
            var localFrustum = frustum.TransformToLocal(GetWorldMatrix());
            IntlIntersectLocalFrustum(localFrustum, out var visMin, out var visMax);
            var visMinI = Int2.Max(Int2.FloorToInt(visMin / TileSize), 0);
            var visMaxI = Int2.Min(Int2.CeilToInt(visMax / TileSize), runtimeData.ChunkCount);
            Vector3 min = new Vector3(float.MaxValue);
            Vector3 max = new Vector3(float.MinValue);
            for (int y = visMinI.Y; y < visMaxI.Y; ++y) {
                for (int x = visMinI.X; x < visMaxI.X; ++x) {
                    var chunk = runtimeData.LandscapeChunkMeta[x + y * runtimeData.ChunkCount.X];
                    if (!chunk.IsValid) continue;
                    var ctr = new Vector3((x + 0.5f) * TileSize + 0.5f, (float)chunk.HeightMedianF, (y + 0.5f) * TileSize + 0.5f);
                    var ext = new Vector3((TileSize + 1) / 2.0f, (float)chunk.HeightRangeF / 2f, (TileSize + 1) / 2.0f);
                    if (localFrustum.GetIsVisible(ctr, ext)) {
                        min = Vector3.Min(min, ctr - ext);
                        max = Vector3.Max(max, ctr + ext);
                    }
                }
            }
            return BoundingBox.FromMinMax(min, max);
        }
        unsafe public void Render(CSGraphics graphics, ref MaterialStack materials, ScenePass pass) {
            using var marker = ProfileMarker_Render.Auto();
            //if (!pass.TagsToInclude.Has(pass.RetainedRenderer.Scene.TagManager.RequireTag("Terrain"))) return;
            var localFrustum = pass.Frustum.TransformToLocal(GetWorldMatrix());

            ApplyDataChanges();
            graphics.CopyBufferData(layerDataBuffer);

            if (EdgeRenderer != null)
                EdgeRenderer.Render(graphics, ref materials, pass, localFrustum);

            // Calculate the min/max AABB of the projected frustum 
            Int2 visMinI, visMaxI;
            {
                IntlIntersectLocalFrustum(localFrustum, out var visMin, out var visMax);
                visMinI = Int2.Max(Int2.FloorToInt(visMin / TileSize), 0);
                visMaxI = Int2.Min(Int2.CeilToInt(visMax / TileSize), runtimeData.ChunkCount);
            }
            if (visMaxI.X <= visMinI.X || visMaxI.Y <= visMinI.Y) return;

            ref var cache = ref RequirePassCache(pass);

            if (pass.TagsToInclude.HasAny(RenderTag.Default | RenderTag.ShadowCast)) {
                UpdateChunkInstances(cache.landscapeDraw, localFrustum, runtimeData.LandscapeChunkMeta, visMinI, visMaxI);
                LandMaterial.SetBuffer("Instances", cache.landscapeDraw.InstanceBuffer);
                cache.landscapeDraw.Draw(graphics, ref materials, pass, CSDrawConfig.Default);
            }
            if (pass.TagsToInclude.Has(RenderTag.Transparent) && LandscapeData.WaterEnabled) {
                UpdateChunkInstances(cache.waterDraw, localFrustum, runtimeData.WaterChunkMeta, visMinI, visMaxI);
                var macros = new Material();
                using var push = materials.Push(macros);
                macros.SetDepthMode(DepthMode.MakeReadOnly());
                macros.SetBlendMode(new BlendMode(
                    BlendMode.BlendArg.Zero, BlendMode.BlendArg.One,
                    BlendMode.BlendArg.One, BlendMode.BlendArg.Src1Color,
                    BlendMode.BlendOp.Add, BlendMode.BlendOp.Add));
                cache.waterDraw.Draw(graphics, ref materials, pass, CSDrawConfig.Default);
                macros.SetMacro("NOTHING", "1");
                macros.SetBlendMode(BlendMode.MakeNone());
                macros.SetDepthMode(DepthMode.MakeDefault());
                cache.waterDraw.Draw(graphics, ref materials, pass, CSDrawConfig.Default);
            }
        }

        private ref PassCache RequirePassCache(ScenePass pass) {
            for (int i = 0; i < passCache.Count; i++) {
                if (passCache[i].Pass == pass) return ref passCache[i];
            }
            passCache.Add(new PassCache(pass));
            InitialisePassCache(ref passCache[^1]);
            return ref passCache[^1];
        }

        [EditorButton]
        public void Save() { LandscapeData.Save(); }
        [EditorButton]
        public void Load() { LandscapeData.Load(); }
        [EditorButton]
        public void Reset() { LandscapeData.Reset(); }
    }
}
