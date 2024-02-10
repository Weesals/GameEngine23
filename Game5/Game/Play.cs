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
using Navigation;
using Game5.UI;
using Weesals.Editor;
using Weesals.Engine.Rendering;

/*
 * TODO:
 * - Picking
 * - Entity selection compatibility
 * - Compute Shader
 * - GPU Driven Rendering
 * - Animation
 * - Gameplay
 * - Particles
 */

namespace Game5.Game {

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

        public Camera Camera { get; private set; }
        public readonly GameRoot GameRoot;
        public Scene Scene => GameRoot.Scene;
        public ScenePassManager ScenePasses => GameRoot.ScenePasses;
        public Updatables Updatables => GameRoot.Updatables;
        public LandscapeData Landscape => landscape;
        public LandscapeRenderer LandscapeRenderer => landscapeRenderer;

        public World RenderWorld { get; private set; }
        public Simulation Simulation { get; private set; }
        public World World => Simulation.World;

        public SelectionManager SelectionManager = new();
        public EntityHighlighting EntityHighlighting;

        public NavDebug? NavDebug;

        RenderWorldBinding renderBindings;
        float time = 0;

        ParticleSystemManager particleManager;
        public ParticleSystemManager ParticleManager => particleManager;

        [EditorField]
        public float TemporalX = 0.4273f;//0.34731f;
        [EditorField]
        public float TemporalY = 0.6796f;//0.7656f;
        [EditorField]
        public float TemporalFrac = 188.1f;//651.5216f;
        [EditorField]
        public float TemporalFrac2 = 254.76f;//651.5216f;
        [EditorField]
        public float TemporalFrac3 = 0.11f;//651.5216f;

        public Play(GameRoot root) {
            GameRoot = root;

            landscape = new LandscapeData();
            var layers = new LandscapeLayerCollection();
            layers.TerrainLayers = new[] {
                new LandscapeLayer("TL_Grass") { BaseColor = "./Assets/T_Grass_BaseColor.png", NormalMap = "./Assets/T_Moss_Normal.jpg", },
                new LandscapeLayer("TL_Dirt") { BaseColor = "./Assets/T_Dirt_BaseColor.jpg", NormalMap = "./Assets/T_Dirt_Normal.jpg", },
                new LandscapeLayer("TL_DirtyMoss") { BaseColor = "./Assets/T_DirtyMoss_BaseColor.jpg", NormalMap = "./Assets/T_DirtyMoss_Normal.jpg", },
                new LandscapeLayer("TL_Moss") { BaseColor = "./Assets/T_Moss_BaseColor.jpg", NormalMap = "./Assets/T_Moss_Normal.jpg", },
                new LandscapeLayer("TL_Tiles") { BaseColor = "./Assets/T_Tiles_BaseColor.jpg", NormalMap = "./Assets/T_Tiles_Normal.jpg", },
                new LandscapeLayer("TL_WaterFloor") { BaseColor = "./Assets/T_Dirt_BaseColor.jpg", NormalMap = "./Assets/T_Dirt_Normal.jpg", },
                new LandscapeLayer("TL_Sand") { BaseColor = "./Assets/T_Dirt_BaseColor.jpg", NormalMap = "./Assets/T_Dirt_Normal.jpg", },
                new LandscapeLayer("TL_Cliff") { BaseColor = "./Assets/T_GorgeCliff_BaseColorHeight.png", NormalMap = "./Assets/T_GorgeCliff_Normal.jpg", Alignment = LandscapeLayer.AlignmentModes.WithNormal, Rotation = 90.0f, Flags = LandscapeLayer.TerrainFlags.FlagImpassable, },
            }; 
            landscape.Initialise(128, layers);
            landscapeRenderer = new LandscapeRenderer();
            landscapeRenderer.Initialise(landscape, Scene.RootMaterial);
            landscape.OnLandscapeChanged += (landscape, change) => { root.RenderRevision++; };

            var particleTest = new ParticleGenerator();
            var particleSpawn = particleTest.GetStage(ParticleGenerator.Stage.Modes.ParticleSpawn);
            particleSpawn.InsertModule(ParticleGenerator.FindModule("PositionInSphere")).SetInput("Radius", "1.0");
            //particleSpawn.InsertModule(ParticleGenerator.FindModule("Custom")).SetInput("Code", "Position.z -= 20.0;");
            particleSpawn.InsertModule(ParticleGenerator.FindModule("Custom")).SetInput("Code", "Position.z += 5.0;");
            //particleSpawn.InsertModule(ParticleGenerator.FindModule("Custom")).SetInput("Code", "Velocity.y += 20.0;");
            var particleStep = particleTest.GetStage(ParticleGenerator.Stage.Modes.ParticleStep);
            particleStep.InsertModule(ParticleGenerator.FindModule("IncrementLifetime"));
            //particleStep.InsertModule(ParticleGenerator.FindModule("ApplyGravity"));
            particleStep.InsertModule(ParticleGenerator.FindModule("ApplyVelocity"));
            particleStep.InsertModule(ParticleGenerator.FindModule("ApplyDrag")).SetInput("Drag", "0.2");
            particleStep.InsertModule(ParticleGenerator.FindModule("Turbulence")).SetInput("Speed", "10.0");
            particleStep.InsertModule(ParticleGenerator.FindModule("Custom")).SetInput("Code", "Velocity.x += 10.0 * DeltaTime;");
            particleStep.InsertModule(ParticleGenerator.FindModule("KillDeadParticles"));
            var particleVertex = particleTest.GetStage(ParticleGenerator.Stage.Modes.ParticleVertex);
            particleVertex.InsertModule(ParticleGenerator.FindModule("UVRotate")).SetInput("Rotation", "Age * 1.0 + Seed * 6.0");
            particleVertex.InsertModule(ParticleGenerator.FindModule("UVAtlas")).SetInput("AtlasCount", "2.0").SetInput("AtlasIndex", "0.0");
            var particlePixel = particleTest.GetStage(ParticleGenerator.Stage.Modes.ParticlePixel);
            particlePixel.InsertModule(ParticleGenerator.FindModule("Opacity")).SetInput("Opacity", "0.1");
            particlePixel.InsertModule(ParticleGenerator.FindModule("TextureSample"));
            var hlsl = particleTest.Generate();
            Directory.CreateDirectory("./Assets/Generated/");
            File.WriteAllText("./Assets/Generated/ParticleTest.hlsl", hlsl);
            particleManager = new ParticleSystemManager();
            particleManager.Initialise(128);
            var particleSystem = new ParticleSystem();
            particleSystem.Generator = particleTest;
            particleSystem.SpawnRate = 500;
            particleSystem.DrawMaterial.SetTexture("Texture", Resources.LoadTexture("Assets/ParticleAtlas.png"));
            particleManager.AppendSystem(particleSystem);

            Camera = new Camera() {
                FOV = 3.14f * 0.25f,
                Position = new Vector3(0, 20f, -10f),
                Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 3.14f * 0.3f),
            };


            root.Canvas.AppendChild(new UIPlay(this));

            Simulation = new();
            Simulation.SetLandscape(landscape);

            RenderWorld = new World();

            renderBindings = new(World, RenderWorld, Scene, root.ScenePasses);
            EntityHighlighting = new(renderBindings);

            Simulation.GenerateWorld();

            GameRoot.RegisterEditable(this, true);
        }
        public void Dispose() {
            GameRoot.RegisterEditable(this, false);
        }

        public void Update(float dt) {
            time += dt;
            if (dt > 0.02f) dt = 0.02f;

            Simulation.Step((int)MathF.Round(dt * 1000));
            EntityHighlighting.Update((uint)(time * 1000f));

            Camera.FarPlane = 50f;
            Camera.NearPlane = 10f;

            Scene.RootMaterial.SetValue("Time", UnityEngine.Time.time);

            NavDebug?.OnDrawGizmosSelected();

            GameRoot.TemporalPass.TemporalMaterial.SetValue("RandomX", TemporalX);
            GameRoot.TemporalPass.TemporalMaterial.SetValue("RandomY", TemporalY);
            GameRoot.TemporalPass.TemporalMaterial.SetValue("TemporalFrac", TemporalFrac);
            GameRoot.TemporalPass.TemporalMaterial.SetValue("TemporalFrac2", TemporalFrac2);
            GameRoot.TemporalPass.TemporalMaterial.SetValue("TemporalFrac3", TemporalFrac3);

            var mpos = Camera.ViewportToRay(Input.GetMousePosition() / (Vector2)GameRoot.Canvas.GetSize()).ProjectTo(new Plane(Vector3.UnitY, 0f));
            particleManager.RootMaterial.SetValue("AvoidPoint", mpos);
        }

        [EditorButton]
        public void ToggleNavDebug() {
            if (NavDebug == null) {
                NavDebug = new() { ShowCornerLabels = false, ShowTriangleLabels = false, };
                NavDebug.Initialise(Simulation.NavBaker);
            } else {
                NavDebug = null;
            }
        }

        public void PreRender(CSGraphics graphics, float dt) {
            renderBindings.UpdateChanged();
            for (int i = 0; i < 1; i++) {
                //particleManager.RootMaterial.SetValue("RandomMul", TemporalFrac);
                //particleManager.RootMaterial.SetValue("RandomAdd", TemporalFrac2);
                particleManager.Update(graphics, dt);
            }
        }
        public void PostRender() {
        }

        public void RenderBasePass(CSGraphics graphics, ScenePass pass) {
            landscapeRenderer.Render(graphics, pass);
            if (pass.TagsToInclude.Has(RenderTag.Transparent)) {
                particleManager.Draw(graphics, pass.GetPassMaterial(), Scene.RootMaterial);
            }
        }


        // Notify the current play session of an action
        // invoked by the local player
        // eg. Moving a unit or attacking a target
        public void PushLocalCommand(ActionRequest request) {
            foreach (var selected in SelectionManager.Selected) {
                if (!World.IsValid(selected.GetEntity())) continue;
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
