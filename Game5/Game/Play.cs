﻿using System;
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
using Weesals.Engine.Particles;
using Weesals.Engine.Jobs;
using Weesals.Engine.Profiling;

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
        public Action<ItemReference, bool>? OnEntitySelected;
        public Action<ICollection<ItemReference>>? OnSelectionChanged;
        private HashSet<ItemReference> selected = new();

        public IReadOnlyCollection<ItemReference> Selected => selected;

        public void ClearSelected() {
            using var toDeselect = new PooledList<ItemReference>(selected.Count);
            foreach (var item in selected) toDeselect.Add(item);
            selected.Clear();
            foreach (var item in toDeselect) NotifySelected(item, false);
        }
        public void SetSelected(ItemReference newItem) {
            using var toDeselect = new PooledList<ItemReference>(selected.Count);
            foreach (var item in selected) if (item != newItem) toDeselect.Add(item);
            if (selected.Count == toDeselect.Count) selected.Clear();
            else foreach (var item in toDeselect) selected.Remove(item);
            foreach (var item in toDeselect) NotifySelected(item, false);
            if (newItem.IsValid) AppendSelected(newItem);
        }
        public void AppendSelected(ItemReference item) {
            if (selected.Add(item)) NotifySelected(item, true);
        }
        public void RemoveSelected(ItemReference item) {
            if (selected.Remove(item)) NotifySelected(item, false);
        }

        private void NotifySelected(ItemReference item, bool selected) {
            if (item.Owner is IEntitySelectable selectable)
                selectable.NotifySelected(item.Data, selected);
            if (OnEntitySelected != null) OnEntitySelected(item, selected);
            if (OnSelectionChanged != null) OnSelectionChanged(this.selected);
        }
    }

    public class Play : IDisposable {

        private static ProfilerMarker ProfileMarker_PlayUpdate = new ProfilerMarker("Play.Update");

        public Camera Camera { get; private set; }
        public readonly GameRoot GameRoot;
        public Scene Scene => GameRoot.Scene;
        public ScenePassManager ScenePasses => GameRoot.ScenePasses;
        public Updatables Updatables => GameRoot.Updatables;
        public LandscapeData Landscape => landscape;
        public LandscapeRenderer LandscapeRenderer => landscapeRenderer;
        public ParticleSystemManager ParticleManager => particleManager;

        public Simulation Simulation { get; private set; }
        public World World => Simulation.World;

        public SelectionManager SelectionManager = new();
        public EntityHighlighting EntityHighlighting;
        public VisualsCollection EntityVisuals;

        public NavDebug? NavDebug;
        
        RenderWorldBinding renderBindings;

        LandscapeData landscape;
        LandscapeRenderer landscapeRenderer;
        LandscapeFoliageRenderer foliageRenderer;

        LayeredLandscapeData layeredLandscape;

        ParticleSystemManager particleManager;
        ParticleSystem fireParticles;
        ParticleSystem.Emitter mouseFire;
        UIPlay playUI;

        [EditorField] public bool EnableFog = false;
        [EditorField] public bool EnableAO = false;
        [EditorField] public float FogIntensity = 0.25f;
        [EditorField] public bool DrawVisibilityVolume = false;

        [EditorField] public int LoadedModelCount => Resources.LoadedModelCount;
        [EditorField] public int LoadedShaderCount => Resources.LoadedShaderCount;
        [EditorField] public int LoadedTextureCount => Resources.LoadedTextureCount;

        float time = 0;

        public event Action<CSGraphics, float> OnRender;

        public Play(GameRoot root) {
            GameRoot = root;

            JobHandle loadHandle = default;

            var landscapeHandle = (JobHandle.Schedule(() => {
                using var marker = new ProfilerMarker("Create Landscape").Auto();
                landscape = new LandscapeData();
                var layers = new LandscapeLayerCollection();
                layers.TerrainLayers = new[] {
                    new LandscapeLayer("TL_Grass") { BaseColor = "./Assets/Terrain/T_Grass_BaseColor.png", NormalMap = "./Assets/Terrain/T_Grass_Normal.png", },
                    new LandscapeLayer("TL_Dirt") { BaseColor = "./Assets/Terrain/T_Dirt_BaseColor.png", NormalMap = "./Assets/Terrain/T_Dirt_Normal.png", },
                    new LandscapeLayer("TL_DirtyMoss") { BaseColor = "./Assets/Terrain/T_DirtyMoss_BaseColor.png", NormalMap = "./Assets/Terrain/T_DirtyMoss_Normal.png", },
                    new LandscapeLayer("TL_Moss") { BaseColor = "./Assets/Terrain/T_Moss_BaseColor.png", NormalMap = "./Assets/Terrain/T_Moss_Normal.png", },
                    new LandscapeLayer("TL_Tiles") { BaseColor = "./Assets/Terrain/T_Tiles_BaseColor.png", NormalMap = "./Assets/Terrain/T_Tiles_Normal.png", Alignment = LandscapeLayer.AlignmentModes.Random90, Scale = 0.25f, },
                    new LandscapeLayer("TL_WaterFloor") { BaseColor = "./Assets/Terrain/T_Dirt_BaseColor.png", NormalMap = "./Assets/Terrain/T_Dirt_Normal.png", },
                    new LandscapeLayer("TL_Sand") { BaseColor = "./Assets/Terrain/T_Dirt_BaseColor.png", NormalMap = "./Assets/Terrain/T_Dirt_Normal.png", },
                    new LandscapeLayer("TL_Cliff") { BaseColor = "./Assets/Terrain/T_GorgeCliff_BaseColorHeight.png", NormalMap = "./Assets/Terrain/T_GorgeCliff_Normal.png", Alignment = LandscapeLayer.AlignmentModes.WithNormal, Rotation = 90.0f, Flags = LandscapeLayer.TerrainFlags.FlagImpassable, },
                };
                using (new ProfilerMarker("Init").Auto()) {
                    landscape.Initialise(layers);
                }
                using (new ProfilerMarker("Load").Auto()) {
                    landscape.Load();
                    if (landscape.Size.X == 0) landscape.SetSize(255);
                    //landscape.Resize(300);
                }
                using (new ProfilerMarker("Renderer").Auto()) {
                    landscapeRenderer = new LandscapeRenderer();
                    landscapeRenderer.Initialise(landscape, Scene.RootMaterial);
                    landscape.OnLandscapeChanged += (landscape, change) => { root.RenderRevision++; };
                }
                using (new ProfilerMarker("Foliage").Auto()) {
                    foliageRenderer = new(landscapeRenderer);
                }
            }));

            loadHandle = loadHandle.Join(JobHandle.Schedule(() => {
                using var marker = new ProfilerMarker("Particles").Auto();
                particleManager = new ParticleSystemManager();
                particleManager.Initialise(512);
            }));

            JobHandle visualsJob = default;
            loadHandle = loadHandle.Join(JobHandle.Schedule(() => {
                using var marker = new ProfilerMarker("Load Visuals").Auto();
                EntityVisuals = new(this);
                string contents = File.ReadAllText("./Assets/Visuals.json");
                using (new ProfilerMarker("Parse ").Auto()) {
                    visualsJob = EntityVisuals.Load(contents);
                }
            }));

            var simHandle = JobHandle.Schedule(() => {
                using var marker = new ProfilerMarker("Create Simulation").Auto();
                Simulation = new();
            }).
            Join(landscapeHandle).
            Then(() => {
                using var marker = new ProfilerMarker("Bind Simulation").Auto();
                Simulation.SetLandscape(landscape);
            });

            Camera = new Camera() {
                FOV = 3.14f * 0.15f,
                Position = new Vector3(-0f, 25f, -0f),
                Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 3.14f * 0.25f)
                    * Quaternion.CreateFromAxisAngle(Vector3.UnitX, 3.14f * 0.2f),
                NearPlane = 5.0f,
                FarPlane = 5000.0f,
            };

            using (new ProfilerMarker("UI Play").Auto()) {
                root.Canvas.AppendChild(playUI = new UIPlay(this));
            }

            loadHandle.Join(simHandle).Complete();

            visualsJob.Complete();

            GameRoot.RegisterEditable(this, true);
        }
        public void Dispose() {
            GameRoot.RegisterEditable(this, false);
        }

        public void Initialise() {
            using (new ProfilerMarker("Render Bindings").Auto()) {
                renderBindings = new(World, Scene, GameRoot.ScenePasses, ParticleManager, EntityVisuals);
                EntityHighlighting = new(renderBindings);
            }
            using (new ProfilerMarker("Generate World").Auto()) {
                Simulation.GenerateWorld();
            }
        }

        public void Update(float dt) {
            using var marker = ProfileMarker_PlayUpdate.Auto();
            time += dt;
            if (dt > 0.02f) dt = 0.02f;

            NavDebug?.OnDrawGizmosSelected();

            Simulation.Step((int)MathF.Round(dt * 1000));
            EntityHighlighting.Update((uint)(time * 1000f));

            Scene.RootMaterial.SetValue("Time", time);
            Scene.RootMaterial.SetValue("CloudDensity", FogIntensity);

            if (Input.GetKeyPressed(KeyCode.Z)) landscapeRenderer.SecondVariant = !landscapeRenderer.SecondVariant;

            if (Input.GetKeyDown(KeyCode.Control) && Input.GetKeyDown(KeyCode.Alt) && Input.GetKeyPressed(KeyCode.F11)) {
                Resources.ReloadAssets();
            }

            var mpos = Camera.ViewportToRay(Input.GetMousePosition() / (Vector2)GameRoot.Canvas.GetSize()).ProjectTo(new Plane(Vector3.UnitY, 0f));
            particleManager.RootMaterial.SetValue("AvoidPoint", mpos);
            if (mouseFire != null) mouseFire.Position = mpos;
            if (Input.GetMouseButtonDown(0) && fireParticles != null) {
                fireParticles.CreateEmitter(mpos)
                    .SetDelayedDeath(2f);
            }

            foreach (var accessor in World.QueryAll<ECTransform, ECParticleBinding>()) {
                ((ECParticleBinding)accessor).Emitter.Position =
                    ((ECTransform)accessor).GetWorldPosition();
            }
            var moveTypeId = World.Context.RequireComponentTypeId<ECActionMove>();
            foreach (var accessor in World.QueryAll<CAnimation>()) {
                var anim = (CAnimation)accessor;
                bool isMoving =
                    accessor.Archetype.TryGetSparseComponent(moveTypeId, out var moveColumn) &&
                    accessor.Archetype.GetHasSparseComponent(moveColumn, accessor.Row);
                accessor.Component1Ref.Animation = isMoving ? anim.WalkAnim : anim.IdleAnim;
            }
            /*if (Input.GetKeyPressed(KeyCode.Space)) {
                var query = World.GetEntities();
                query.MoveNext();
                var entity = query.Current;
                ref var tform = ref World.GetComponentRef<ECTransform>(entity);
                tform.Position.X += 1000;
            }*/
        }

        public void SetAutoQuality(CSGraphics graphics) {
            if (graphics.GetDeviceName().ToString().Contains("nvidia", StringComparison.InvariantCultureIgnoreCase)) {
                EnableFog = true;
                EnableAO = true;
            } else {
                landscapeRenderer.HighQualityBlend = false;
                landscapeRenderer.Parallax = false;
                landscapeRenderer.EnableStochastic = false;
            }
        }

        [EditorButton]
        public void ToggleNavDebug() {
            if (NavDebug == null) {
                NavDebug = new();
                NavDebug.Initialise(Simulation.NavBaker);
                SelectionManager.SetSelected(new(NavDebug));
            } else {
                SelectionManager.RemoveSelected(new(NavDebug));
                NavDebug = null;
            }
        }

        public void PreRender() {
            renderBindings.UpdateChanged();
        }

        public void RenderUpdate(CSGraphics graphics, float dt) {
            OnRender?.Invoke(graphics, dt);
            particleManager.Update(graphics, dt);
        }
        public void PrepareBasePass(CSGraphics graphics, ScenePass pass) {
            foliageRenderer.UpdateInstances(graphics, pass);
        }
        public void RenderBasePass(CSGraphics graphics, ScenePass pass) {
            var materialStack = new MaterialStack(Scene.RootMaterial);
            using var passMat = materialStack.Push(pass.OverrideMaterial);
            landscapeRenderer.Render(graphics, ref materialStack, pass);
            if (pass.TagsToInclude.Has(RenderTag.Default)) {
                foliageRenderer.RenderInstances(graphics, ref materialStack, pass);
            }
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

        public ItemReference HitTest(Ray ray) {
            float nearestDst2 = float.MaxValue;
            ItemReference entityHit = ItemReference.None;
            foreach (var accessor in World.QueryAll<ECTransform, CModel>()) {
                var epos = (ECTransform)accessor;
                var emodel = (CModel)accessor;
                var prefab = EntityVisuals.GetVisuals(emodel.PrefabName);
                if (prefab == null) continue;
                foreach (var model in prefab.Models) {
                    foreach (var mesh in model.Meshes) {
                        var lray = ray;
                        lray.Origin -= SimulationWorld.SimulationToWorld(epos.GetPosition3());
                        var dst = mesh.BoundingBox.RayCast(lray);
                        if (dst >= 0f && dst < nearestDst2) {
                            entityHit = Simulation.EntityProxy.MakeHandle(accessor);
                            nearestDst2 = dst;
                        }
                    }
                }
            }
            if (entityHit.IsValid) return entityHit;
            if (landscape.Raycast(ray, out var landscapeHit)) {
                return new ItemReference(landscapeRenderer);
            }
            return ItemReference.None;
        }

    }
}
