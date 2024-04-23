using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.Utility;

namespace Weesals.Landscape {
    public class LandscapeFoliageRenderer {

        public LandscapeRenderer LandscapeRenderer;
        public LandscapeData LandscapeData => LandscapeRenderer.LandscapeData;

        public Material FoliageMaterial;
        public Model Model;

        private MeshDrawIndirect meshDraw;
        private BufferLayoutPersistent instanceBuffer;
        private BoundingBox visibleBounds;

        public LandscapeFoliageRenderer(LandscapeRenderer landscape) {
            LandscapeRenderer = landscape;

            instanceBuffer = new(BufferLayoutPersistent.Usages.Uniform);
            instanceBuffer.AppendElement(new("INDIRECTINSTANCES", BufferFormat.FORMAT_R32G32B32A32_FLOAT));
            instanceBuffer.AllocResize(32 * 1024);
            instanceBuffer.SetCount(32 * 1024);
            instanceBuffer.BufferLayout.SetAllowUnorderedAccess(true);
            instanceBuffer.BufferLayout.revision++;
            new TypedBufferView<Vector4>(instanceBuffer.Elements[0], 1).Set(Vector4.Zero);

            FoliageMaterial = new("./Assets/Shader/FoliageInstance.hlsl", landscape.LandMaterial);
            FoliageMaterial.SetBuffer("Instances", instanceBuffer);
            FoliageMaterial.SetBlendMode(BlendMode.MakeOpaque());
            FoliageMaterial.SetDepthMode(DepthMode.MakeDefault());

            Model = Resources.LoadModel("./Assets/Models/SM_GrassClump.fbx");
            Model.Meshes[0].Material.SetTexture("Texture", Resources.LoadTexture("./Assets/Models/Grass.png"));

            using var materials = new PooledList<Material>();
            materials.Add(Model.Meshes[0].Material);
            materials.Add(FoliageMaterial);
            meshDraw = new(Model.Meshes[0], materials);
        }

        public void UpdateInstances(CSGraphics graphics, ScenePass pass) {
            graphics.CopyBufferData(instanceBuffer, new RangeInt(0, 4));
            LandscapeRenderer.ApplyDataChanges();

            visibleBounds = LandscapeRenderer.GetVisibleBounds(pass.Frustum);
            visibleBounds.Min = visibleBounds.Min.Floor();
            visibleBounds.Max = visibleBounds.Max.Ceil();
            var dispatchCount = new Int3((int)visibleBounds.Size.X / 8, (int)visibleBounds.Size.Z / 8, 1);
            if (dispatchCount.X <= 0 || dispatchCount.Y <= 0) {
                visibleBounds = BoundingBox.Invalid;
                return;
            }

            FoliageMaterial.SetValue("BoundsMin", visibleBounds.Min.toxz());
            FoliageMaterial.SetValue("BoundsMax", visibleBounds.Max.toxz());
            {
                var computeShader = Resources.RequireShader(graphics,
                    Resources.LoadShader("./Assets/Shader/GenerateFoliageInstances.hlsl", "CSGenerateFoliage"), "cs_6_2", default, default);
                var computePSO = graphics.RequireComputePSO(computeShader.NativeShader);
                using var materials = new PooledList<Material>();
                materials.Add(FoliageMaterial);
                materials.Add(pass.OverrideMaterial);
                materials.Add(LandscapeRenderer.LandMaterial);
                materials.Add(pass.Scene.RootMaterial);
                var resources = MaterialEvaluator.ResolveResources(graphics, computePSO, materials);
                //dispatchCount.X = 1;
                //dispatchCount.Y = 1;
                graphics.DispatchCompute(computePSO, resources, dispatchCount);
            }
        }
        unsafe public void RenderInstances(CSGraphics graphics, ref MaterialStack materials, ScenePass pass) {
            if (!visibleBounds.IsValid) return;
            //using var foliageMat = materials.Push(FoliageMaterial);
            //if (pass.TagsToInclude.Has(RenderTag.ShadowCast)) return;
            graphics.CopyBufferData(meshDraw.ArgsBuffer);
            graphics.CopyBufferData(instanceBuffer, meshDraw.ArgsBuffer, new RangeInt(0, 4), 4);
            meshDraw.Draw(graphics, ref materials, pass, CSDrawConfig.Default);
        }

    }
}
