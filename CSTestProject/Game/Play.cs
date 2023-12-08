using Flecs.NET.Core;
using GameEngine23.Interop;
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
        public int RenderRevision;

        internal List<CSInstance> instances = new();
        ShadowPass shadowPass;
        BasePass basePass;

        public Play(Scene scene) {
            Scene = scene;

            landscape = new LandscapeData();
            landscape.SetSize(128);
            landscapeRenderer = new LandscapeRenderer();
            landscapeRenderer.Initialise(landscape, Scene.RootMaterial);

            var model = Resources.LoadModel("./assets/SM_Barracks.fbx");
            Camera = new Camera() {
                FOV = 3.14f * 0.25f,
                Position = new Vector3(0, 20f, -10f),
                Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 3.14f * 0.3f),
            };

            shadowPass = new ShadowPass(scene, "Shadows");
            basePass = new BasePass(scene, "BasePass");
            basePass.UpdateShadowParameters(shadowPass);
            basePass.AddDependency("ShadowMap", shadowPass);

            foreach (var mesh in model.Meshes) {
                var instance = scene.CSScene.CreateInstance();
                instances.Add(instance);
                using var materials = new PooledList<Material>();
                materials.Add(mesh.Material);
                materials.Add(basePass.OverrideMaterial);
                materials.Add(scene.RootMaterial);
                basePass.AddInstance(instance, mesh, materials);
                materials[1] = shadowPass.OverrideMaterial;
                shadowPass.AddInstance(instance, mesh, materials);
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
            basePass.SetVisible(instances[0], !Input.GetKeyDown(KeyCode.Space));

            // Move camera with WASD/UDLR
            var move = new Vector2(
                Input.GetSignedAxis(KeyCode.LeftArrow, KeyCode.RightArrow) + Input.GetSignedAxis(KeyCode.A, KeyCode.D),
                Input.GetSignedAxis(KeyCode.DownArrow, KeyCode.UpArrow) + Input.GetSignedAxis(KeyCode.S, KeyCode.W)
            ) * (dt * Camera.Position.Y);
            Camera.Position += move.AppendY(0f);
        }

        public void PreRender(CSGraphics graphics) {
            if (basePass.SetViewProjection(Camera.GetViewMatrix(), Camera.GetProjectionMatrix())) RenderRevision++;
            if (shadowPass.UpdateShadowFrustum(basePass)) RenderRevision++;
            Canvas.RequireComposed();
        }
        public void Render(CSGraphics graphics) {
            // Render the shadow pass
            shadowPass.Bind(graphics);
            graphics.Clear();
            shadowPass.Render(graphics);

            // Render the base pass
            basePass.UpdateShadowParameters(shadowPass);
            basePass.Bind(graphics);
            graphics.Clear();
            graphics.SetViewport(GameViewport);
            RenderBasePass(graphics, basePass);
            basePass.Render(graphics);
            Canvas.Render(graphics, Canvas.Material);

        }

        private void RenderBasePass(CSGraphics graphics, BasePass pass) {
            landscapeRenderer.Render(graphics, pass);
        }

    }
}
