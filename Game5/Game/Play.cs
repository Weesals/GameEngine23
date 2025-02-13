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
using Game5.Rendering;
using System.ComponentModel.DataAnnotations;

/*
 * TODO:
 * x Picking
 * x Entity selection compatibility
 * x Compute Shader
 * - GPU Driven Rendering
 * - Animation
 * - Gameplay
 * x Particles
 */

namespace Game5.Game
{
    public class Play : IDisposable {

        private static ProfilerMarker ProfileMarker_PlayUpdate = new ProfilerMarker("Play.Update");
        private static ProfilerMarker ProfileMarker_SimulationUpdate = new ProfilerMarker("SimUpdate");
        private static ProfilerMarker ProfileMarker_AnimationUpdate = new ProfilerMarker("AnimUpdate");

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

        public SelectionManager SelectionManager = new(null);
        public EntityHighlighting EntityHighlighting;
        public VisualsCollection EntityVisuals;

        public NavDebug? NavDebug;
        
        RenderWorldBinding renderBindings;

        LandscapeData landscape;
        LandscapeRenderer landscapeRenderer;
        LandscapeFoliageRenderer foliageRenderer;

        LayeredLandscapeData layeredLandscape;

        ParticleSystemManager particleManager;
        SelectionRenderer selectionRenderer;
        PlayerColorManager playerColorManager;

        UIPlay playUI;

        [EditorField] public bool EnableFog = false;
        [EditorField] public bool EnableAO = false;
        [EditorField] public bool EnableBloom = false;
        [EditorField] public bool EnableFoliage = false;
        [EditorField] public float FogIntensity = 0.25f;
        [EditorField] public bool DrawVisibilityVolume = false;
        [EditorField] public bool DrawSceneBVH {
            get => Scene.DrawSceneBVH;
            set => Scene.DrawSceneBVH = value;
        }

        [EditorField] public int LoadedModelCount => Resources.LoadedModelCount;
        [EditorField] public int LoadedShaderCount => Resources.LoadedShaderCount;
        [EditorField] public int LoadedTextureCount => Resources.LoadedTextureCount;
        [EditorField, Range(0f, 1f)] public float ShadowPerspective {
            get => GameRoot.ShadowPass.ShadowPerspective;
            set => GameRoot.ShadowPass.ShadowPerspective = value;
        }
        [EditorField]
        public bool NavigationDebug {
            get => NavDebug != null;
            set {
                if (NavDebug == null && value) {
                    NavDebug = new();
                    NavDebug.Initialise(Simulation.NavigationSystem.GetBaker());
                    SelectionManager.SetSelected(new(NavDebug));
                } else if (NavDebug != null && !value) {
                    SelectionManager.RemoveSelected(new(NavDebug));
                    NavDebug = null;
                }
            }
        }

        float time = 0;

        public Play(GameRoot root) {
            GameRoot = root;

            JobHandle loadHandle = default;

            var landscapeHandle = (JobHandle.Schedule(() => {
                using var marker = new ProfilerMarker("Create Landscape").Auto();
                landscape = new LandscapeData();
                var layers = new LandscapeLayerCollection();
                layers.TerrainLayers = new[] {
                    new LandscapeLayer("TL_Grass") { BaseColor = "./Assets/Terrain/T_Grass_BaseColor.png", NormalMap = "./Assets/Terrain/T_Grass_Normal.png", Scale = 0.2f, },
                    new LandscapeLayer("TL_Dirt") { BaseColor = "./Assets/Terrain/T_Dirt_BaseColor.png", NormalMap = "./Assets/Terrain/T_Dirt_Normal.png", },
                    new LandscapeLayer("TL_DirtyMoss") { BaseColor = "./Assets/Terrain/T_DirtyMoss_BaseColor.png", NormalMap = "./Assets/Terrain/T_DirtyMoss_Normal.png", },
                    new LandscapeLayer("TL_Moss") { BaseColor = "./Assets/Terrain/T_Moss_BaseColor.png", NormalMap = "./Assets/Terrain/T_Moss_Normal.png", },
                    new LandscapeLayer("TL_Tiles") { BaseColor = "./Assets/Terrain/T_Tiles_BaseColor.png", NormalMap = "./Assets/Terrain/T_Tiles_Normal.png", Alignment = LandscapeLayer.AlignmentModes.Random90, Scale = 0.25f, },
                    new LandscapeLayer("TL_WaterFloor") { BaseColor = "./Assets/Terrain/T_Dirt_BaseColor.png", NormalMap = "./Assets/Terrain/T_Dirt_Normal.png", },
                    new LandscapeLayer("TL_Sand") { BaseColor = "./Assets/Terrain/T_Dirt_BaseColor.png", NormalMap = "./Assets/Terrain/T_Dirt_Normal.png", },
                    new LandscapeLayer("TL_Cliff") { BaseColor = "./Assets/Terrain/T_GorgeCliff_BaseColorHeight.png", NormalMap = "./Assets/Terrain/T_GorgeCliff_Normal.png", Alignment = LandscapeLayer.AlignmentModes.WithNormal, Rotation = 90.0f, Flags = LandscapeLayer.TerrainFlags.FlagImpassable, },
                };
                var grassFoliage = new FoliageType() { };
                JobHandle.Schedule(() => {
                    var grassClump = Resources.LoadModel("./Assets/Models/SM_GrassClump.fbx", out var meshHandle);
                    grassFoliage.LoadHandle = meshHandle.Then(() => {
                        grassFoliage.Mesh = grassClump.Meshes[0];
                        grassFoliage.Mesh.Material.SetTexture("Texture", Resources.LoadTexture("./Assets/Models/Grass.png"));
                    });
                });
                var elLeafFoliage = new FoliageType() { };
                JobHandle.Schedule(() => {
                    var tropPlant = Resources.LoadModel("./Assets/Models/Yughues/Tropical Plants/tropical_plant.FBX", out var meshHandle);
                    elLeafFoliage.LoadHandle = meshHandle.Then(() => {
                        if (tropPlant.Meshes.Count > 0)
                        {
                            elLeafFoliage.Mesh = tropPlant.Meshes[0];
                            elLeafFoliage.Mesh.Material.SetTexture("Texture", Resources.LoadTexture("./Assets/Models/Yughues/Tropical Plants/diffuse.png"));
                            elLeafFoliage.Mesh.Material.SetMacro("VWIND", "1");
                        }
                    });
                });
                layers.TerrainLayers[0].Foliage = new[] {
                    new LandscapeFoliageType() { FoliageType = grassFoliage, Density = 7f, },
                    new LandscapeFoliageType() { FoliageType = elLeafFoliage, Density = 0.1f, }
                };
                layers.TerrainLayers[1].Foliage = new[] {
                    new LandscapeFoliageType() { FoliageType = grassFoliage, Density = 7f, }
                };
                layers.TerrainLayers[2].Foliage = new[] {
                    new LandscapeFoliageType() { FoliageType = grassFoliage, Density = 7f, }
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
                    landscapeRenderer = new LandscapeProxy();
                    landscapeRenderer.Initialise(landscape, Scene.RootMaterial);
                    landscape.OnLandscapeChanged += (landscape, change) => { root.RenderRevision++; };
                }
                using (new ProfilerMarker("Foliage").Auto()) {
                    foliageRenderer = new(landscapeRenderer);
                }
            }));

            particleManager = new ParticleSystemManager();
            loadHandle = loadHandle.Join(JobHandle.Schedule(() => {
                using var marker = new ProfilerMarker("Particles").Auto();
                particleManager.Initialise(1024);
            }));

            loadHandle = loadHandle.Join(JobHandle.Schedule(() => {
                playerColorManager = new();
                selectionRenderer = new(SelectionManager);
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
                /*Position = new Vector3(-0f, 10f, 10f),
                Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 3.14f * 0.45f)
                    * Quaternion.CreateFromAxisAngle(Vector3.UnitX, 3.14f * 0.15f),*/
                NearPlane = 5.0f,
                FarPlane = 20000.0f,
            };
            //Camera.FOV = 0f;
            //Camera.OrthoSize = 20f;

            using (new ProfilerMarker("UI Play").Auto()) {
                root.Canvas.AppendChild(playUI = new UIPlay(this));
            }

            loadHandle.Join(simHandle).Complete();

            visualsJob.Complete();

            /*var img = new Image(new CSBufferReference(GameRoot.ShadowPass.shadowBuffer)) {
                Transform = CanvasTransform.MakeAnchored(new Vector2(512, 512), new Vector2(0f, 0f)),
            };
            root.Canvas.AppendChild(img);*/

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

            using (ProfileMarker_SimulationUpdate.Auto()) {
                Simulation.Step((int)MathF.Round(dt * 1000));
            }
            EntityHighlighting.Update((uint)(time * 1000f));

            Scene.RootMaterial.SetValue("Time", time);
            Scene.RootMaterial.SetValue("CloudDensity", FogIntensity);

            if (Input.GetKeyPressed(KeyCode.Z)) landscapeRenderer.SecondVariant = !landscapeRenderer.SecondVariant;

            if (Input.GetKeyDown(KeyCode.Control) && Input.GetKeyDown(KeyCode.Alt) && Input.GetKeyPressed(KeyCode.F11)) {
                Resources.ReloadAssets();
            }

            //var mpos = Camera.ViewportToRay(Input.GetMousePosition() / (Vector2)GameRoot.Canvas.GetSize()).ProjectTo(new Plane(Vector3.UnitY, 0f));
            //particleManager.RootMaterial.SetValue("AvoidPoint", mpos);

            foreach (var accessor in World.QueryAll<ECTransform, ECParticleBinding>()) {
                ((ECParticleBinding)accessor).Emitter.Position =
                    ((ECTransform)accessor).GetWorldPosition();
            }
            using (ProfileMarker_AnimationUpdate.Auto()) {
                var moveTypeId = World.Context.RequireComponentTypeId<ECActionMove>();
                foreach (var accessor in World.QueryAll<CAnimation>()) {
                    var anim = (CAnimation)accessor;
                    bool isMoving =
                        accessor.Archetype.TryGetSparseColumn(moveTypeId, out var moveColumn) &&
                        accessor.Archetype.GetHasSparseComponent(ref World.Manager.ColumnStorage, moveColumn, accessor.Row);
                    var desiredClip = isMoving ? anim.WalkAnim : anim.IdleAnim;
                    if (!accessor.Component1.Animation.Equals(desiredClip)) {
                        accessor.Component1Ref.Animation = desiredClip;
                    }
                }
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
            if (!graphics.GetDeviceName().ToString().Contains("intel", StringComparison.InvariantCultureIgnoreCase)) {
                EnableFog = true;
                EnableAO = true;
                EnableFoliage = true;
                EnableBloom = true;
            } else {
                landscapeRenderer.HighQualityBlend = false;
                landscapeRenderer.EnableStochastic = false;
                landscapeRenderer.Parallax = false;
            }
        }

        public void PreRender() {
            renderBindings.UpdateChanged();
        }

        public void RenderUpdate(CSGraphics graphics, float dt) {
            if (Input.GetKeyPressed(KeyCode.X)) {
                //ParticleManager.VelocityBuffer
            }
            particleManager.Update(graphics, dt);
        }
        public void PrepareBasePass(CSGraphics graphics, ScenePass pass) {
            if (EnableFoliage) {
                foliageRenderer.UpdateInstances(graphics, pass);
            }
        }
        public void RenderBasePass(CSGraphics graphics, ScenePass pass) {
            using var passMat = Graphics.MaterialStack.Push(pass.GetPassMaterial());
            using var colMat = Graphics.MaterialStack.Push(playerColorManager.PlayerColorMaterial);

            landscapeRenderer.Render(graphics, ref Graphics.MaterialStack, pass);
            if (pass.TagsToInclude.Has(RenderTag.Default) && EnableFoliage) {
                foliageRenderer.RenderInstances(graphics, pass);
            }
            if (pass.TagsToInclude.Has(RenderTag.Transparent)) {
                particleManager.Draw(graphics);
                selectionRenderer.Draw(graphics, pass.RenderQueue);
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
            var entityMap = World.GetOrCreateSystem<EntityMapSystem>();
            var chunkBeg = (SimulationWorld.WorldToSimulation(ray.Origin).XZ);
            var chunkEnd = (SimulationWorld.WorldToSimulation(ray.ProjectTo(new Plane(Vector3.UnitY, 0f))).XZ);
            var sign = new Int2(chunkEnd.X > chunkBeg.X ? 1 : -1, chunkEnd.Y > chunkBeg.Y ? 1 : -1);
            var rayIt = new GridThickRayIterator(chunkBeg, chunkEnd - chunkBeg, 0, EntityMapSystem.Separation);
            foreach (var cell in rayIt) {
                var entities = entityMap.AllEntities.GetEntitiesEnumerator(cell, 0);
                if (!entities.HasAny) continue;
                Gizmos.DrawWireCube(
                    SimulationWorld.SimulationToWorld(EntityMapSystem.ChunkToSim(cell), EntityMapSystem.Separation / 2),
                    Vector3.One * (EntityMapSystem.Separation * SimulationWorld.WorldScale * 0.9f)
                );
                foreach (var entity in entities) {
                    if (!World.TryGetComponent<ECTransform>(entity, out var epos)) continue;
                    if (!World.TryGetComponent<CModel>(entity, out var emodel)) continue;
                    var prefab = EntityVisuals.GetVisuals(emodel.PrefabName, emodel.Variant);
                    if (prefab == null) continue;
                    foreach (var model in prefab.Models) {
                        foreach (var mesh in model.Meshes) {
                            var lray = ray;
                            lray.Origin -= SimulationWorld.SimulationToWorld(epos.GetPosition3());
                            var dst = mesh.BoundingBox.RayCast(lray);
                            if (dst >= 0f && dst < nearestDst2) {
                                entityHit = Simulation.EntityProxy.MakeHandle(entity);
                                nearestDst2 = dst;
                            }
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

        public void GetEntitiesInScreenRect(RectF rect, ref PooledHashSet<ItemReference> entities) {
            const int MaxEntitySize = 4000;
            var world = World;
            // Adjust the projection matrix to get a tight frustum around the rect
            var proj = Camera.GetProjectionMatrix();
            var cameraRot = Camera.Orientation;
            var size = playUI.Canvas.GetSize();
            proj.M11 /= rect.Width / size.X;
            proj.M22 /= rect.Height / size.Y;
            proj.M31 -= 2f * ((rect.Centre.X - size.X / 2f) / (rect.Width));
            proj.M32 += 2f * ((rect.Centre.Y - size.Y / 2f) / (rect.Height));
            var frustum = new Frustum4(Camera.GetViewMatrix() * proj);
            frustum.Normalize();
            Vector3 rangeMin = new(float.MaxValue), rangeMax = new(float.MinValue);
            Span<Vector3> points = stackalloc Vector3[4];
            frustum.IntersectPlane(Vector3.UnitY, 0f, points);
            for (int i = 0; i < 4; i++) {
                rangeMin = Vector3.Min(rangeMin, points[i]);
                rangeMax = Vector3.Max(rangeMax, points[i]);
            }
            frustum.IntersectPlane(Vector3.Transform(Vector3.UnitZ, cameraRot),
                Vector3.Dot(Vector3.Transform(Vector3.UnitZ, cameraRot), Camera.Position) + Camera.NearPlane, points);
            for (int i = 0; i < 4; i++) {
                rangeMin = Vector3.Min(rangeMin, points[i]);
                rangeMax = Vector3.Max(rangeMax, points[i]);
            }
            var cmin = EntityMapSystem.SimToChunk(SimulationWorld.WorldToSimulation(rangeMin).XZ - MaxEntitySize);
            var cmax = EntityMapSystem.SimToChunk(SimulationWorld.WorldToSimulation(rangeMax).XZ + MaxEntitySize);
            var entityMapSystem = World.GetOrCreateSystem<EntityMapSystem>();
            int count = 0;
            var seenBuckets = new PooledHashSet<int>(8);
            for (int y = cmin.Y; y <= cmax.Y; y++) {
                for (int x = cmin.X; x <= cmax.X; x++) {
                    var chunkEntities = entityMapSystem.AllEntities.GetEntitiesEnumerator(new Int2(x, y), 0);
                    for (; chunkEntities.MoveNextBucket();) {
                        if (!seenBuckets.AddUnique(chunkEntities.BucketId)) {
                            continue;
                        }
                        for (; chunkEntities.MoveNextEntity();) {
                            var entity = chunkEntities.Current;
                            if (!World.IsValid(entity)) continue;
                            if (!World.TryGetComponent<PrototypeData>(entity, out var protoData)) continue;
                            var footprint = protoData.Footprint;
                            var entitySize = new Vector3(
                                (float)footprint.Size.X * SimulationWorld.WorldScale * 0.5f,
                                (float)footprint.Height / SimulationWorld.AltitudeGranularity,
                                (float)footprint.Size.Y * SimulationWorld.WorldScale * 0.5f
                            );
                            var tform = World.GetComponent<ECTransform>(entity);
                            var pos = SimulationWorld.SimulationToWorld(tform.GetPosition3());
                            pos.Y += entitySize.Y * 0.5f;
                            ++count;
                            if (frustum.GetIsVisible(pos, entitySize)) {
                                entities.Add(Simulation.EntityProxy.MakeHandle(entity));
                            }
                        }
                    }
                }
            }
        }
    }
}
