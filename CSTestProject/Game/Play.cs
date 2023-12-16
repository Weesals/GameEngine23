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
        BasePass basePass;
        TransparentPass transPass;
        ScenePassManager passes;

        public class WorldObject {
            public List<CSInstance> Meshes = new();
        }
        private WorldObject testObject;
        private WorldObject transObject;

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

            var model = Resources.LoadModel("./assets/SM_Barracks.fbx");
            Camera = new Camera() {
                FOV = 3.14f * 0.25f,
                Position = new Vector3(0, 20f, -10f),
                Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 3.14f * 0.3f),
            };

            shadowPass = new ShadowPass(scene);
            basePass = new BasePass(scene);
            transPass = new TransparentPass(scene);
            basePass.UpdateShadowParameters(shadowPass);
            transPass.UpdateShadowParameters(shadowPass);
            passes = new();
            passes.AddPass(shadowPass);
            passes.AddPass(basePass);
            passes.AddPass(transPass);

            testObject = new();
            foreach (var mesh in model.Meshes) {
                var instance = scene.CreateInstance();
                var mat = Matrix4x4.CreateRotationY(3.14f);
                unsafe { Scene.UpdateInstanceData(instance, &mat, sizeof(Matrix4x4)); }
                testObject.Meshes.Add(instance);
                passes.AddInstance(instance, mesh);
            };

            transObject = new();
            var material = new Material("./assets/transparent.hlsl");
            foreach (var mesh in model.Meshes) {
                var instance = scene.CreateInstance();
                var mat = Matrix4x4.CreateRotationY(3.14f);
                unsafe { Scene.UpdateInstanceData(instance, &mat, sizeof(Matrix4x4)); }
                transObject.Meshes.Add(instance);
                passes.AddInstance(instance, mesh, material, RenderTag.Transparent);
            };

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
            if (basePass.SetViewProjection(Camera.GetViewMatrix(), Camera.GetProjectionMatrix())) RenderRevision++;
            if (transPass.SetViewProjection(Camera.GetViewMatrix(), Camera.GetProjectionMatrix())) RenderRevision++;
            if (shadowPass.UpdateShadowFrustum(basePass)) RenderRevision++;
            Canvas.RequireComposed();
        }
        RenderGraph renderGraph = new();
        public void Render(CSGraphics graphics) {
            renderGraph.Clear();
            // Render shadows
            renderGraph.BeginPass(shadowPass)
                .SetEvaluator((CSGraphics graphics, ref RenderPass.Context context) => {
                    shadowPass.Bind(graphics, ref context);
                    graphics.Clear();
                    shadowPass.Render(graphics);

                });
            // Render opaques
            renderGraph.BeginPass(basePass)
                .SetEvaluator((CSGraphics graphics, ref RenderPass.Context context) => {
                    basePass.UpdateShadowParameters(shadowPass);
                    basePass.Bind(graphics, ref context);
                    graphics.Clear();
                    graphics.SetViewport(GameViewport);
                    RenderBasePass(graphics, basePass);
                    basePass.Render(graphics);
                })
                .SetDependency("ShadowMap", shadowPass, 0);
            // Render transparents
            renderGraph.BeginPass(transPass)
                .SetEvaluator((CSGraphics graphics, ref RenderPass.Context context) => {
                    transPass.UpdateShadowParameters(shadowPass);
                    transPass.Bind(graphics, ref context);
                    graphics.SetViewport(GameViewport);
                    transPass.Render(graphics);
                    Canvas.Render(graphics, Canvas.Material);
                })
                .SetDependency("BasePass", basePass, 1)
                .SetDependency("SceneDepth", basePass, 0)
                .SetDependency("ShadowMap", shadowPass, 0);
            renderGraph.Execute(transPass, graphics);
        }

        private void RenderBasePass(CSGraphics graphics, BasePass pass) {
            landscapeRenderer.Render(graphics, pass);
        }

        public WorldObject? HitTest(Ray ray) {
            return testObject;
        }

        unsafe public Vector3 GetLocation(WorldObject target) {
            foreach (var instance in target.Meshes) {
                var data = Scene.GetInstanceData(instance);
                return ((Matrix4x4*)data.Data)->Translation;
            }
            return default;
        }
        unsafe public void SetLocation(WorldObject target, Vector3 pos) {
            foreach (var instance in target.Meshes) {
                var data = Scene.GetInstanceData(instance);
                Matrix4x4 mat = *(Matrix4x4*)data.Data;
                mat.Translation = pos;
                Scene.UpdateInstanceData(instance, &mat, sizeof(Matrix4x4));
            }
        }
    }
}
