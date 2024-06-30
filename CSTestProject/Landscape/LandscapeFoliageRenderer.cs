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

        private MeshDrawIndirect meshDraw;
        private BufferLayoutPersistent instanceBuffer;
        private BoundingBox visibleBounds = BoundingBox.Invalid;
        private int terrainRevision = 0;
        private Vector4 boundsMinMax;

        public LandscapeFoliageRenderer(LandscapeRenderer landscape) {
            LandscapeRenderer = landscape;

            instanceBuffer = new(BufferLayoutPersistent.Usages.Uniform);
            instanceBuffer.AppendElement(new("INDIRECTINSTANCES", BufferFormat.FORMAT_R32G32B32A32_FLOAT));
            instanceBuffer.AllocResize(4);
            instanceBuffer.BufferLayout.mCount = 32 * 1024;
            instanceBuffer.BufferLayout.SetAllowUnorderedAccess(true);
            instanceBuffer.BufferLayout.size = instanceBuffer.BufferStride * instanceBuffer.Count;
            instanceBuffer.BufferLayout.revision = -1;
            new TypedBufferView<Vector4>(instanceBuffer.Elements[0], 1).Set(Vector4.Zero);

            FoliageMaterial = new("./Assets/Shader/FoliageInstance.hlsl", landscape.LandMaterial);
            FoliageMaterial.SetBuffer("Instances", instanceBuffer);
            FoliageMaterial.SetBlendMode(BlendMode.MakeOpaque());
            FoliageMaterial.SetDepthMode(DepthMode.MakeDefault());

            JobHandle.Schedule(() => {
                using var marker = new ProfilerMarker("Load Foliage").Auto();
                var model = Resources.LoadModel("./Assets/Models/SM_GrassClump.fbx");
                model.Meshes[0].Material.SetTexture("Texture", Resources.LoadTexture("./Assets/Models/Grass.png"));

                using var materials = new PooledList<Material>();
                materials.Add(model.Meshes[0].Material);
                materials.Add(FoliageMaterial);
                meshDraw = new(model.Meshes[0], materials);
            });
        }

        public void UpdateInstances(CSGraphics graphics, ScenePass pass) {
            return;
            if (meshDraw == null) return;
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

            // Should only copy fist-frame, then no-op
            graphics.CopyBufferData(meshDraw.ArgsBuffer);

            var computeShader = Resources.RequireShader(graphics,
                Resources.LoadShader("./Assets/Shader/GenerateFoliageInstances.hlsl", "CSGenerateFoliage"), "cs_6_2", default, default);
            var computePSO = graphics.RequireComputePSO(computeShader.NativeShader);
            using var materials = new PooledList<Material>();
            materials.Add(FoliageMaterial);
            materials.Add(pass.OverrideMaterial);
            materials.Add(LandscapeRenderer.LandMaterial);
            materials.Add(pass.Scene.RootMaterial);
            var resources = MaterialEvaluator.ResolveResources(graphics, computePSO, materials);
            // Reset count
            graphics.CopyBufferData(instanceBuffer, new RangeInt(0, 4));
            // Dispatch compute
            graphics.DispatchCompute(computePSO, resources, dispatchCount);
            // Copy count into instance buffer
            graphics.CopyBufferData(instanceBuffer, meshDraw.ArgsBuffer, new RangeInt(0, 4), 4);
        }
        unsafe public void RenderInstances(CSGraphics graphics, ref MaterialStack materials, ScenePass pass) {
            if (meshDraw == null) return;
            if (!visibleBounds.IsValid) return;
            if (!pass.TagsToInclude.Has(pass.Scene.TagManager.RequireTag("MainPass"))) return;
            meshDraw.Draw(graphics, ref materials, pass, CSDrawConfig.Default);
        }

    }
}
