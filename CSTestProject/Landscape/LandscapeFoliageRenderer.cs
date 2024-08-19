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

        public LandscapeRenderer LandscapeRenderer;
        public LandscapeData LandscapeData => LandscapeRenderer.LandscapeData;

        public Material FoliageMaterial;

        private BoundingBox visibleBounds = BoundingBox.Invalid;
        private int terrainRevision = 0;
        private Vector4 boundsMinMax;

        public class FoliageInstance {
            public MeshDrawIndirect MeshDraw;
            public Material FoliageMaterial;
            public float Density = 7f;
            public BufferLayoutPersistent InstanceBuffer;
        }
        private List<FoliageInstance> foliageInstances = new();

        public LandscapeFoliageRenderer(LandscapeRenderer landscape) {
            LandscapeRenderer = landscape;

            FoliageMaterial = new("./Assets/Shader/FoliageInstance.hlsl", landscape.LandMaterial);
            FoliageMaterial.SetBlendMode(BlendMode.MakeOpaque());
            FoliageMaterial.SetDepthMode(DepthMode.MakeDefault());

            JobHandle.Schedule(() => {
                using var marker = new ProfilerMarker("Load Foliage").Auto();

                var model = Resources.LoadModel("./Assets/Models/SM_GrassClump.fbx");
                model.Meshes[0].Material.SetTexture("Texture", Resources.LoadTexture("./Assets/Models/Grass.png"));
                AppendFoliageMesh(model.Meshes[0]);

                var tropPlant = Resources.LoadModel("./Assets/Models/Yughues/Tropical Plants/tropical_plant.FBX");
                if (tropPlant.Meshes[0] != null) {
                    tropPlant.Meshes[0].Material.SetTexture("Texture", Resources.LoadTexture("./Assets/Models/Yughues/Tropical Plants/diffuse.png"));
                    var instance = AppendFoliageMesh(tropPlant.Meshes[0]);
                    instance.Density = 0.1f;
                    instance.FoliageMaterial.SetMacro("VWIND", "1");
                }
            });
        }

        private BufferLayoutPersistent CreateInstanceBuffer() {
            const int Capacity = 32 * 1024;
            var instanceBuffer = new BufferLayoutPersistent(BufferLayoutPersistent.Usages.Uniform);
            instanceBuffer.AppendElement(new("INDIRECTINSTANCES", BufferFormat.FORMAT_R32G32B32A32_FLOAT));
            instanceBuffer.AllocResize(4);
            instanceBuffer.BufferLayout.mCount = -1;
            instanceBuffer.BufferLayout.SetAllowUnorderedAccess(true);
            instanceBuffer.BufferLayout.size = instanceBuffer.BufferStride * Capacity;
            instanceBuffer.BufferLayout.revision = -1;
            new TypedBufferView<Vector4>(instanceBuffer.Elements[0], 1).Set(Vector4.Zero);
            return instanceBuffer;
        }
        public FoliageInstance AppendFoliageMesh(Mesh mesh) {
            var instanceMaterial = new Material(FoliageMaterial);
            var instanceBuffer = CreateInstanceBuffer();
            instanceMaterial.SetBuffer("Instances", instanceBuffer);
            using var materials = new PooledList<Material>();
            materials.Add(instanceMaterial);
            materials.Add(mesh.Material);
            FoliageInstance instance = new() {
                MeshDraw = new(mesh, materials),
                FoliageMaterial = instanceMaterial,
                InstanceBuffer = instanceBuffer,
            };
            foliageInstances.Add(instance);
            return instance;
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

            Span<Vector3> corners = stackalloc Vector3[4];
            pass.Frustum.IntersectPlane(Vector3.UnitY, 0, corners);
            if (visibleBounds == newBounds && terrainRevision == LandscapeData.Revision) {
                if (Vector2.DistanceSquared(corners[0].toxz(), boundsMinMax.toxy()) < 0.5f &&
                    Vector2.DistanceSquared(corners[2].toxz(), boundsMinMax.tozw()) < 0.5f) {
                    return;
                }
            }

            visibleBounds = newBounds;
            terrainRevision = LandscapeData.Revision;
            boundsMinMax = new Vector4(corners[0].X, corners[0].Z, corners[2].X, corners[2].Z);

            FoliageMaterial.SetValue("BoundsMin", visibleBounds.Min.toxz());
            FoliageMaterial.SetValue("BoundsMax", visibleBounds.Max.toxz());

            var computeShader = Resources.RequireShader(graphics,
                Resources.LoadShader("./Assets/Shader/GenerateFoliageInstances.hlsl", "CSGenerateFoliage"), "cs_6_2", default, default);
            var computePSO = graphics.RequireComputePSO(computeShader.NativeShader);

            foreach (var instance in foliageInstances) {
                using var materials = new PooledList<Material>();
                instance.FoliageMaterial.SetBuffer("Instances", instance.InstanceBuffer);
                instance.FoliageMaterial.SetValue("Density", instance.Density);
                materials.Add(instance.FoliageMaterial);
                materials.Add(pass.OverrideMaterial);
                materials.Add(LandscapeRenderer.LandMaterial);
                materials.Add(pass.Scene.RootMaterial);
                var resources = MaterialEvaluator.ResolveResources(graphics, computePSO, materials);
                // Reset count
                graphics.CopyBufferData(instance.InstanceBuffer, new RangeInt(0, 4));
                // Dispatch compute
                graphics.DispatchCompute(computePSO, resources.AsCSSpan(), dispatchCount);
                // Should only copy fist-frame, then no-op
                graphics.CopyBufferData(instance.MeshDraw.ArgsBuffer);
                // Copy count into instance buffer
                graphics.CopyBufferData(instance.InstanceBuffer, instance.MeshDraw.ArgsBuffer, new RangeInt(0, 4), 4);
            }
        }
        unsafe public void RenderInstances(CSGraphics graphics, ref MaterialStack materials, ScenePass pass) {
            if (foliageInstances.Count == 0) return;
            if (!visibleBounds.IsValid) return;
            if (!pass.TagsToInclude.Has(pass.Scene.TagManager.RequireTag("MainPass"))) return;
            foreach (var instance in foliageInstances) {
                instance.MeshDraw.Draw(graphics, ref materials, pass, CSDrawConfig.Default);
            }
        }

    }
}
