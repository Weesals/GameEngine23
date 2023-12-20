using Flecs.NET.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.Landscape;
using Weesals.UI;
using Weesals.Utility;

namespace Weesals.Game {

    public struct Position {
        public Vector3 Value;
    }

    public interface IUpdatable {
        void Update(float dt);
    }
    public class Updatables : List<IUpdatable> {
        public void RegisterUpdatable(IUpdatable updatable, bool enable) {
            if (enable) Add(updatable); else Remove(updatable);
        }
        public void Invoke(float dt) {
            foreach (var updatable in this) {
                updatable.Update(dt);
            }
        }
    }

    public class Play : IDisposable {

        LandscapeData landscape;
        LandscapeRenderer landscapeRenderer;

        public readonly Updatables Updatables = new();

        public RectI GameViewport { get; private set; }
        public Camera Camera { get; private set; }
        public Scene Scene { get; private set; }
        public World World { get; private set; }
        public Canvas Canvas { get; private set; }
        public LandscapeData Landscape => landscape;
        public int RenderRevision;

        ShadowPass shadowPass;
        DeferredPass clearPass;
        BasePass basePass;
        TransparentPass transPass;
        HiZPass highZPass;
        BloomPass bloomPass;
        TemporalJitter temporalJitter;
        PostProcessPass postProcessPass;
        DeferredPass canvasPass;

        RenderGraph renderGraph = new();
        ScenePassManager scenePasses = new();

        private WorldObject testObject;

        public Play(Scene scene) {
            Scene = scene;

            landscape = new LandscapeData();
            var layers = new LandscapeLayerCollection();
            layers.TerrainLayers = new[] {
                new LandscapeLayer("TL_Grass") { BaseColor = "./assets/T_Grass_BaseColor.png", NormalMap = "./assets/T_Moss_Normal.jpg", },
                new LandscapeLayer("TL_Dirt") { BaseColor = "./assets/T_Dirt_BaseColor.jpg", NormalMap = "./assets/T_Dirt_Normal.jpg", },
                new LandscapeLayer("TL_DirtyMoss") { BaseColor = "./assets/T_DirtyMoss_BaseColor.jpg", NormalMap = "./assets/T_DirtyMoss_Normal.jpg", },
                new LandscapeLayer("TL_Moss") { BaseColor = "./assets/T_Moss_BaseColor.jpg", NormalMap = "./assets/T_Moss_Normal.jpg", },
                new LandscapeLayer("TL_Tiles") { BaseColor = "./assets/T_Tiles_BaseColor.jpg", NormalMap = "./assets/T_Tiles_Normal.jpg", },
                new LandscapeLayer("TL_WaterFloor") { BaseColor = "./assets/T_Dirt_BaseColor.jpg", NormalMap = "./assets/T_Dirt_Normal.jpg", },
                new LandscapeLayer("TL_Sand") { BaseColor = "./assets/T_Dirt_BaseColor.jpg", NormalMap = "./assets/T_Dirt_Normal.jpg", },
                new LandscapeLayer("TL_Cliff") { BaseColor = "./assets/T_GorgeCliff_BaseColorHeight.png", NormalMap = "./assets/T_GorgeCliff_Normal.jpg", Alignment = LandscapeLayer.AlignmentModes.WithNormal, Rotation = 90.0f,},
            }; 
            landscape.Initialise(128, layers);
            landscapeRenderer = new LandscapeRenderer();
            landscapeRenderer.Initialise(landscape, Scene.RootMaterial);
            landscape.OnLandscapeChanged += (landscape, change) => { RenderRevision++; };

            Camera = new Camera() {
                FOV = 3.14f * 0.25f,
                Position = new Vector3(0, 20f, -10f),
                Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 3.14f * 0.3f),
            };

            shadowPass = new ShadowPass(scene);
            clearPass = new DeferredPass("Clear",
                default,
                new[] {
                    new RenderPass.PassOutput("SceneDepth").SetTargetDesc(new TextureDesc() { Format = BufferFormat.FORMAT_D24_UNORM_S8_UINT, }),
                    new RenderPass.PassOutput("SceneColor"),
                    new RenderPass.PassOutput("SceneVelId"),
                },
                (CSGraphics graphics, ref RenderPass.Context context) => {
                    graphics.SetViewport(new RectI(Int2.Zero, context.ResolvedDepth.Texture.GetSize()));
                    graphics.Clear();
                });
            basePass = new BasePass(scene) {
                PreRender = (graphics) => {
                    //graphics.SetViewport(GameViewport);
                    RenderBasePass(graphics, basePass);
                },
            };
            transPass = new TransparentPass(scene) {
                PreRender = (graphics) => {
                    //graphics.SetViewport(GameViewport);
                    RenderBasePass(graphics, transPass);
                }
            };
            highZPass = new();
            bloomPass = new();
            temporalJitter = new TemporalJitter("TJitter") {
                ScenePasses = scenePasses,
                OnBegin = (size) => {
                    scenePasses.BeginRender(size);
                    basePass.UpdateShadowParameters(shadowPass);
                    transPass.UpdateShadowParameters(shadowPass);
                },
            };
            basePass.UpdateShadowParameters(shadowPass);
            transPass.UpdateShadowParameters(shadowPass);
            postProcessPass = new();
            canvasPass = new DeferredPass("Canvas",
                new[] { new RenderPass.PassInput("SceneColor", false) },
                new[] { new RenderPass.PassOutput("SceneColor", 0), },
                (CSGraphics graphics, ref RenderPass.Context context) => {
                    //graphics.SetViewport(GameViewport);
                    Canvas.Render(graphics, Canvas.Material);
                });

            scenePasses.AddPass(shadowPass);
            scenePasses.AddPass(basePass);
            scenePasses.AddPass(transPass);

            testObject = new();
            var model = Resources.LoadModel("./assets/SM_Barracks.fbx");
            foreach (var mesh in model.Meshes) {
                var instance = scene.CreateInstance();
                testObject.Meshes.Add(instance);
                scenePasses.AddInstance(instance, mesh);
            };
            Scene.SetLocation(testObject, Vector3.Zero);

            Canvas = new Canvas();
            Canvas.AppendChild(new UIPlay(this));

            World = World.Create();

            var test = World.Entity()
                .Set(new Position());

            World.Query();
        }
        public void Dispose() {
            Canvas.Dispose();
        }

        public void SetViewport(RectI gameViewport) {
            if (GameViewport == gameViewport) return;
            GameViewport = gameViewport;
            RenderRevision++;
            Camera.Aspect = (float)gameViewport.Size.X / gameViewport.Size.Y;
            Canvas.SetSize(gameViewport.Size);
        }

        public void Update(float dt) {
            Updatables.Invoke(dt);

            Canvas.Update(dt);

            // Control visibility with Spacebar
            basePass.SetVisible(testObject.Meshes[0], !Input.GetKeyDown(KeyCode.Space));
            transPass.SetVisible(testObject.Meshes[0], !Input.GetKeyDown(KeyCode.Space));

            // Move camera with WASD/UDLR
            var move = new Vector2(
                Input.GetSignedAxis(KeyCode.LeftArrow, KeyCode.RightArrow) + Input.GetSignedAxis(KeyCode.A, KeyCode.D),
                Input.GetSignedAxis(KeyCode.DownArrow, KeyCode.UpArrow) + Input.GetSignedAxis(KeyCode.S, KeyCode.W)
            ) * (dt * Camera.Position.Y);
            Camera.Position += move.AppendY(0f);

            Scene.RootMaterial.SetValue("Time", UnityEngine.Time.time);
        }

        public void PreRender(CSGraphics graphics) {
            if (scenePasses.SetViewProjection(Camera.GetViewMatrix(), Camera.GetProjectionMatrix())) {
                RenderRevision++;
            }
            if (shadowPass.UpdateShadowFrustum(scenePasses.Frustum)) {
                RenderRevision++;
            }
            Canvas.RequireComposed();
        }
        public void Render(CSGraphics graphics) {
            var velTgtDsc = basePass.Outputs[2].TargetDesc;
            velTgtDsc.Format = Input.GetKeyDown(KeyCode.Z) ? BufferFormat.FORMAT_R16G16_FLOAT : BufferFormat.FORMAT_R8G8B8A8_SNORM;
            basePass.Outputs[2].SetTargetDesc(velTgtDsc);

            renderGraph.Clear();
            // Render shadows
            renderGraph.BeginPass(shadowPass);

            // Render scene color
            renderGraph.BeginPass(clearPass);

            renderGraph.BeginPass(basePass);
            renderGraph.BeginPass(highZPass);
            renderGraph.BeginPass(transPass);

            // Intercept render to set up jitter offset
            renderGraph.BeginPass(temporalJitter);

            // Render post processing
            renderGraph.BeginPass(bloomPass);
            renderGraph.BeginPass(postProcessPass);

            // Render UI
            renderGraph.BeginPass(canvasPass)
                .SetViewport(GameViewport);
            renderGraph.Execute(canvasPass, graphics);

            // Copy current matrices into previous frame slots
            Scene.PostRender();
        }

        private void RenderBasePass(CSGraphics graphics, ScenePass pass) {
            landscapeRenderer.Render(graphics, pass);
        }

        public WorldObject? HitTest(Ray ray) {
            return testObject;
        }

    }
}
