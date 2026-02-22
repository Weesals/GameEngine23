using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals;
using Weesals.Engine;
using Weesals.Engine.Jobs;
using Weesals.Impostors;

namespace Game5.Tests {
    public class DualSourceTransPass : MainPass {
        public DualSourceTransPass(Scene scene) : base(scene, "TransparentPass") {
            Inputs = new[] {
                new PassInput("SceneDepth"),
                new PassInput("SceneColor", false),
            };
            Outputs = new[] {
                new PassOutput("SceneDepth", 0),
                new PassOutput("SceneColor", 1),
            };
            TagsToInclude.Clear();
            TagsToInclude.Add(scene.TagManager.RequireTag("MainPass"));
            TagsToInclude.Add(scene.TagManager.RequireTag("DualSource"));
            GetPassMaterial().SetBlendMode(new BlendMode() {
                mBlendColorOp = BlendMode.BlendOp.Add,
                mSrcColorBlend = BlendMode.BlendArg.SrcAlpha,
                mDestColorBlend = BlendMode.BlendArg.SrcInvAlpha,
                mBlendAlphaOp = BlendMode.BlendOp.Max,
                mSrcAlphaBlend = BlendMode.BlendArg.Src1Alpha,
                mDestAlphaBlend = BlendMode.BlendArg.SrcInvAlpha,
            });
            GetPassMaterial().SetDepthMode(DepthMode.MakeReadOnly());
        }
        public override void Render(CSGraphics graphics, ref Context context) {
            using var scopedGraphics = new Graphics.Scoped(graphics, Scene.RootMaterial, RenderQueue);

            var separateTranslucency = RenderTargetPool.RequirePooled(new TextureDesc() { Size = context.Viewport.Size, Format = BufferFormat.FORMAT_R8G8B8A8_UNORM, MipCount = 1, });
            graphics.SetRenderTargets(separateTranslucency, context.ResolvedDepth.Texture);
            graphics.Clear(new(Vector4.Zero));
            if (context.Viewport.Width > 0) graphics.SetViewport(context.Viewport);

            OverrideMaterial.SetValue(RootMaterial.iRes, (Vector2)context.Viewport.Size);

            RenderQueue.Clear();
            OnRender?.Invoke(graphics);
            RenderScene(graphics, ref context);

            graphics.SetRenderTargets(context.ResolvedTargets[0].Texture, context.ResolvedDepth.Texture);
            DrawQuad(graphics, separateTranslucency, RequireBlitMaterial(BlendMode.MakeAlphaBlend()));
            RenderTargetPool.ReturnPooled(separateTranslucency);
        }

        public void CreateInstances(ScenePassManager scenePasses) {
            JobHandle.Schedule(async () => {
                var impostorGenerator = new ImpostorGenerator();
                var impostorGraphics = Core.ActiveInstance.CreateGraphics();
                impostorGraphics.Reset();

                var hull = Resources.LoadModel("./Assets/B_Granary1_Hull.fbx", new() { }).Meshes[0];
                var hull2 = Resources.LoadModel("./Assets/B_House_Hull.fbx", new() { }).Meshes[0];

                using var meshes = new PooledList<(Mesh mesh, Mesh hull)>(16);
                using var tasks = new PooledList<Task<Material>>(16);
                meshes.Add((Resources.LoadModel("./Assets/B_Granary1.fbx").Meshes[0], hull2));
                meshes.Add((Resources.LoadModel("./Assets/B_Granary2.fbx").Meshes[0], hull2));
                meshes.Add((Resources.LoadModel("./Assets/B_Granary3.fbx").Meshes[0], hull2));
                meshes.Add((Resources.LoadModel("./Assets/SM_House.fbx").Meshes[0], hull2));
                meshes.Add((Resources.LoadModel("./Assets/B_House2.fbx").Meshes[0], hull2));
                meshes.Add((Resources.LoadModel("./Assets/B_House3.fbx").Meshes[0], hull2));
                for (int i = 0; i < meshes.Count; i++) {
                    tasks.Add(impostorGenerator.CreateImpostor(impostorGraphics, meshes[i].mesh));
                }
                await JobHandle.RunOnMain((_) => {
                    impostorGraphics.Execute();
                });
                Task.WaitAll(tasks.ToArray());
                for (int i = 0; i < tasks.Count; i++) {
                    var mesh = meshes[i];
                    var material = await tasks[i];
                    material = new Material(
                        Resources.LoadShader("./Assets/Shader/impostor - dualsourcetest.hlsl", "VSMain"),
                        Resources.LoadShader("./Assets/Shader/impostor - dualsourcetest.hlsl", "PSMain_DualSource"),
                        material
                    );
                    await JobHandle.RunOnMain((_) => {
                        var instance = scenePasses.Scene.CreateInstance();
                        scenePasses.Scene.SetTransform(instance, Matrix4x4.Identity);
                        scenePasses.AddInstance(instance, mesh.hull, material, scenePasses.Scene.TagManager.RequireTag("DualSource"));
                    });
                }
                impostorGraphics.Dispose();
                impostorGenerator.Dispose();
            });
        }
    }
}
