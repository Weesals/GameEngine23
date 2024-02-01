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
using Weesals.ECS;
using System.Diagnostics;
using Weesals.Rendering;
using Weesals.Game.Gameplay;
using Navigation;

/*
 * TODO:
 * - Picking
 * - Entity selection compatibility
 * - Compute Shader
 * - GPU Driven Rendering
 * - Animation
 * - Gameplay
 */

namespace Weesals.Game {

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

    public class SelectionManager {
        public Action<GenericTarget, bool>? OnEntitySelected;
        public Action<ICollection<GenericTarget>>? OnSelectionChanged;
        private HashSet<GenericTarget> selected = new();

        public IReadOnlyCollection<GenericTarget> Selected => selected;

        public void ClearSelected() {
            using var toDeselect = new PooledList<GenericTarget>(selected.Count);
            foreach (var item in selected) toDeselect.Add(item);
            selected.Clear();
            foreach (var item in toDeselect) NotifySelected(item, false);
        }
        public void SetSelected(GenericTarget item) {
            ClearSelected();
            if (item.IsValid) AppendSelected(item);
        }
        private void AppendSelected(GenericTarget item) {
            selected.Add(item);
            NotifySelected(item, true);
        }

        private void NotifySelected(GenericTarget item, bool selected) {
            if (item.Owner is IEntitySelectable selectable)
                selectable.NotifySelected(item.Data, selected);
            if (OnEntitySelected != null) OnEntitySelected(item, selected);
            if (OnSelectionChanged != null) OnSelectionChanged(this.selected);
        }
    }

    public class Play : IDisposable {

        LandscapeData landscape;
        LandscapeRenderer landscapeRenderer;

        public readonly Updatables Updatables = new();

        public RectI GameViewport { get; private set; }
        public Camera Camera { get; private set; }
        public Scene Scene { get; private set; }
        public Canvas Canvas { get; private set; }
        public LandscapeData Landscape => landscape;
        public int RenderRevision;

        public World RenderWorld { get; private set; }
        public Simulation Simulation { get; private set; }
        public World World => Simulation.World;

        public SelectionManager SelectionManager = new();
        public EntityHighlighting EntityHighlighting;

        public NavDebug NavDebug;

        ShadowPass shadowPass;
        DeferredPass clearPass;
        BasePass basePass;
        TransparentPass transPass;
        HiZPass highZPass;
        AmbientOcclusionPass aoPass;
        BloomPass bloomPass;
        TemporalJitter temporalJitter;
        PostProcessPass postProcessPass;
        DeferredPass canvasPass;

        RenderGraph renderGraph = new();
        ScenePassManager scenePasses;
        public ScenePassManager ScenePasses => scenePasses;

        RenderWorldBinding renderBindings;
        float time = 0;

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
                new LandscapeLayer("TL_Cliff") { BaseColor = "./assets/T_GorgeCliff_BaseColorHeight.png", NormalMap = "./assets/T_GorgeCliff_Normal.jpg", Alignment = LandscapeLayer.AlignmentModes.WithNormal, Rotation = 90.0f, Flags = LandscapeLayer.TerrainFlags.FlagImpassable, },
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

            scenePasses = new(scene);

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
                    RenderBasePass(graphics, basePass);
                },
            };
            transPass = new TransparentPass(scene) {
                PreRender = (graphics) => {
                    RenderBasePass(graphics, transPass);
                }
            };
            highZPass = new();
            aoPass = new();
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
                    Canvas.Render(graphics, Canvas.Material);
                });

            scenePasses.AddPass(shadowPass);
            scenePasses.AddPass(basePass);
            scenePasses.AddPass(transPass);

            Canvas = new Canvas();
            Canvas.AppendChild(new UIPlay(this));

            Simulation = new(landscape);
            NavDebug = new();
            NavDebug.Initialise(Simulation.NavBaker);
            NavDebug.ShowCornerLabels = false;
            NavDebug.ShowTriangleLabels = false;

            RenderWorld = new World();

            renderBindings = new(World, RenderWorld, Scene, scenePasses);
            EntityHighlighting = new(renderBindings);

            Simulation.GenerateWorld();
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
            time += dt;
            EntityHighlighting.Update((uint)(time * 1000f));

            if (dt > 0.02f) dt = 0.02f;
            Simulation.Simulate((int)MathF.Round(dt * 1000));

            Updatables.Invoke(dt);

            Canvas.Update(dt);

            // Control visibility with Spacebar
            //basePass.SetVisible(testObject.Meshes[0], !Input.GetKeyDown(KeyCode.Space));
            //transPass.SetVisible(testObject.Meshes[0], !Input.GetKeyDown(KeyCode.Space));

            // Move camera with WASD/UDLR
            var move = new Vector2(
                Input.GetSignedAxis(KeyCode.LeftArrow, KeyCode.RightArrow) + Input.GetSignedAxis(KeyCode.A, KeyCode.D),
                Input.GetSignedAxis(KeyCode.DownArrow, KeyCode.UpArrow) + Input.GetSignedAxis(KeyCode.S, KeyCode.W)
            ) * (dt * Camera.Position.Y);
            Camera.Position += move.AppendY(0f);
            Camera.FarPlane = 45f;
            Camera.NearPlane = 12f;

            Scene.RootMaterial.SetValue("Time", UnityEngine.Time.time);

            NavDebug.OnDrawGizmosSelected();
        }

        public void PreRender(CSGraphics graphics) {
            if (scenePasses.SetViewProjection(Camera.GetViewMatrix(), Camera.GetProjectionMatrix())) {
                RenderRevision++;
            }
            if (shadowPass.UpdateShadowFrustum(scenePasses.Frustum)) {
                RenderRevision++;
            }
            foreach (var pass in scenePasses.ScenePasses) {
                if (pass.GetHasSceneChanges()) ++RenderRevision;
            }
            renderBindings.UpdateChanged();
            Canvas.RequireComposed();
        }
        public void Render(CSGraphics graphics) {
            //Camera.FarPlane = 45f + (0.5f + 0.5f * MathF.Sin(UnityEngine.Time.time * 10.0f)) * 400.0f;
            renderGraph.Clear();

            // Render shadows
            renderGraph.BeginPass(shadowPass);

            // Render scene color
            renderGraph.BeginPass(clearPass);
            renderGraph.BeginPass(basePass);
            //renderGraph.BeginPass(highZPass);
            //renderGraph.BeginPass(aoPass);
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
        }
        public void PostRender() {
            // Copy current matrices into previous frame slots
            Scene.PostRender();
            // Clear dynamic meshes
            ScenePasses.EndRender();

            Handles.Reset();
        }

        private void RenderBasePass(CSGraphics graphics, ScenePass pass) {
            landscapeRenderer.Render(graphics, pass);
            Handles.Render(graphics, pass);
        }


        // Notify the current play session of an action
        // invoked by the local player
        // eg. Moving a unit or attacking a target
        public void PushLocalCommand(ActionRequest request) {
            foreach (var selected in SelectionManager.Selected) {
                Simulation.EnqueueAction(selected.GetEntity(), request);
            }
        }

        public GenericTarget HitTest(Ray ray) {
            var entityHit = Simulation.HitTest(ray);
            if (entityHit.IsValid) return entityHit;
            if (landscape.Raycast(ray, out var landscapeHit)) {
                return new GenericTarget(landscapeRenderer);
            }
            return default;
        }

    }
}
