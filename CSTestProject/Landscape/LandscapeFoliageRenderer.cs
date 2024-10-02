using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.Engine.Jobs;
using Weesals.Engine.Profiling;
using Weesals.Utility;

namespace Weesals.Landscape {
    public class LandscapeFoliageRenderer {

        const int BufferCapacity = 32 * 1024;

        public LandscapeRenderer LandscapeRenderer;
        public LandscapeData LandscapeData => LandscapeRenderer.LandscapeData;

        public Material FoliageMaterial;

        private BoundingBox visibleBounds = BoundingBox.Invalid;
        private int terrainRevision = 0;
        private Vector4 boundsMinMax;

        public class FoliageInstance {
            public FoliageType FoliageType;
            public MeshDrawIndirect MeshDraw;
            public Material FoliageMaterial;
            public BufferLayoutPersistent TypeDataBuffer;
            public BufferLayoutPersistent InstanceBuffer;
        }
        private List<FoliageInstance> foliageInstances = new();

        public LandscapeFoliageRenderer(LandscapeRenderer landscape) {
            LandscapeRenderer = landscape;

            FoliageMaterial = new("./Assets/Shader/FoliageInstance.hlsl", landscape.LandMaterial);
            FoliageMaterial.SetBlendMode(BlendMode.MakeOpaque());
            FoliageMaterial.SetDepthMode(DepthMode.MakeDefault());

            for (int l = 0; l < landscape.Layers.TerrainLayers.Length; l++) {
                var layer = landscape.Layers.TerrainLayers[l];
                if (layer.Foliage == null) continue;
                foreach (var layerFoliage in layer.Foliage) {
                    int i = 0;
                    for (; i < foliageInstances.Count; i++) if (foliageInstances[i].FoliageType == layerFoliage.FoliageType) break;
                    if (i >= foliageInstances.Count) {
                        var instanceMaterial = new Material(FoliageMaterial);
                        var instanceBuffer = CreateInstanceBuffer();
                        var typeDataBuffer = CreateTypeBuffer();
                        instanceMaterial.SetBuffer("Instances", instanceBuffer);
                        instanceMaterial.SetBuffer("TypeData", typeDataBuffer);
                        instanceMaterial.SetValue("Density", 10f);
                        foliageInstances.Add(new() {
                            FoliageType = layerFoliage.FoliageType,
                            FoliageMaterial = instanceMaterial,
                            InstanceBuffer = instanceBuffer,
                            TypeDataBuffer = typeDataBuffer,
                        });
                    }
                    {
                        var typeDataBuffer = foliageInstances[i].TypeDataBuffer;
                        var typeData = new TypedBufferView<uint>(typeDataBuffer.Elements[0], typeDataBuffer.Count);
                        typeData[l] = (byte)Math.Clamp(255 * layerFoliage.Density / 10f, 0, 255);
                    }
                }
            }
        }

        unsafe private BufferLayoutPersistent CreateInstanceBuffer() {
            var instanceBuffer = new BufferLayoutPersistent(BufferLayoutPersistent.Usages.Uniform) { UnorderedAccess = true, };
            instanceBuffer.AppendElement(new("INDIRECTINSTANCES", BufferFormat.FORMAT_R32G32B32A32_FLOAT));
            instanceBuffer.InitializeAppendBuffer(BufferCapacity);
            *(uint*)instanceBuffer.Elements[0].mData = 0;
            return instanceBuffer;
        }
        private BufferLayoutPersistent CreateTypeBuffer() {
            var typeDataBuffer = new BufferLayoutPersistent(BufferLayoutPersistent.Usages.Uniform, 64) { UnorderedAccess = true, };
            typeDataBuffer.AppendElement(new("TypeData", BufferFormat.FORMAT_R32_UINT));
            new TypedBufferView<uint>(typeDataBuffer.Elements[0], typeDataBuffer.Count).Set(0);
            return typeDataBuffer;
        }

        public void UpdateInstances(CSGraphics graphics, ScenePass pass) {
            if (foliageInstances.Count == 0) return;
            LandscapeRenderer.ApplyDataChanges();

            var newBounds = LandscapeRenderer.GetVisibleBounds(pass.Frustum);
            newBounds.Min = newBounds.Min.Floor();
            newBounds.Max = newBounds.Max.Ceil();
            var dispatchCount = new Int3((int)newBounds.Size.X / 8, (int)newBounds.Size.Z / 8, 1);
            if (dispatchCount.X <= 0 || dispatchCount.Y <= 0) {
                visibleBounds = BoundingBox.Invalid;
                return;
            }

            bool refresh = false;

            foreach (var instance in foliageInstances) {
                if (instance.FoliageType.LoadHandle == JobHandle.None) continue;
                if (!instance.FoliageType.LoadHandle.IsComplete) continue;
                var mesh = instance.FoliageType.Mesh;
                using var materials = new PooledList<Material>();
                materials.Add(instance.FoliageMaterial);
                materials.Add(mesh.Material);
                instance.MeshDraw = new(instance.FoliageType.Mesh, materials);
                instance.FoliageType.LoadHandle = JobHandle.None;
                refresh = true;
            }

            Span<Vector3> corners = stackalloc Vector3[4];
            pass.Frustum.IntersectPlane(Vector3.UnitY, 0, corners);
            if (visibleBounds == newBounds && terrainRevision == LandscapeData.Revision && !refresh) {
                if (Vector2.DistanceSquared(corners[0].toxz(), boundsMinMax.toxy()) < 0.5f &&
                    Vector2.DistanceSquared(corners[2].toxz(), boundsMinMax.tozw()) < 0.5f) {
                    return;
                }
            }

            var computeShader = Resources.RequireShader(graphics,
                Resources.LoadShader("./Assets/Shader/GenerateFoliageInstances.hlsl", "CSGenerateFoliage"), "cs_6_2", default, default, out var loadHandle);
            if (loadHandle.IsComplete) {
                visibleBounds = newBounds;
                terrainRevision = LandscapeData.Revision;
                boundsMinMax = new Vector4(corners[0].X, corners[0].Z, corners[2].X, corners[2].Z);

                FoliageMaterial.SetValue("BoundsMin", visibleBounds.Min.toxz());
                FoliageMaterial.SetValue("BoundsMax", visibleBounds.Max.toxz());

                var computePSO = graphics.RequireComputePSO(computeShader.NativeShader);

                foreach (var instance in foliageInstances) {
                    if (instance.MeshDraw == null) continue;
                    //using var materialStack = new MaterialStack(pass.Scene.RootMaterial);
                    //materialStack.Push()
                    using var materials = new PooledList<Material>();
                    materials.Add(instance.FoliageMaterial);
                    materials.Add(pass.OverrideMaterial);
                    materials.Add(LandscapeRenderer.LandMaterial);
                    materials.Add(pass.Scene.RootMaterial);
                    var resources = MaterialEvaluator.ResolveResources(graphics, computePSO, materials);
                    // Reset count
                    graphics.CopyBufferData(instance.InstanceBuffer, new RangeInt(0, 4));
                    // Apply any type changes
                    graphics.CopyBufferData(instance.TypeDataBuffer);
                    // Dispatch compute
                    graphics.DispatchCompute(computePSO, resources.AsCSSpan(), dispatchCount);
                    // Should only copy fist-frame, then no-op
                    graphics.CopyBufferData(instance.MeshDraw.ArgsBuffer);
                    // Copy count into instance buffer
                    graphics.CopyBufferData(instance.InstanceBuffer, instance.MeshDraw.ArgsBuffer, new RangeInt(0, 4), 4);
                }
            }
        }
        unsafe public void RenderInstances(CSGraphics graphics, ref MaterialStack materials, ScenePass pass) {
            if (foliageInstances.Count == 0) return;
            if (!visibleBounds.IsValid) return;
            if (!pass.TagsToInclude.Has(pass.Scene.TagManager.RequireTag("MainPass"))) return;
            foreach (var instance in foliageInstances) {
                if (instance.MeshDraw == null) continue;
                instance.MeshDraw.Draw(graphics, ref materials, pass, CSDrawConfig.Default);
            }
        }

    }
}
