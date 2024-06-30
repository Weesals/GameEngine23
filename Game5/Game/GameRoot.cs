using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.ECS;
using Weesals.Editor;
using Weesals.Engine;
using Weesals.Engine.Jobs;
using Weesals.Engine.Profiling;
using Weesals.Landscape;
using Weesals.UI;
using Weesals.Utility;

namespace Game5.Game {
    public class GameRoot {

        private static ProfilerMarker ProfileMarker_Update = new("Update");
        private static ProfilerMarker ProfileMarker_PreRender = new("PreRender");
        private static ProfilerMarker ProfileMarker_Render = new("Render");
        private static ProfilerMarker ProfileMarker_ResetFrame = new("Reset Frame");

        public readonly Scene Scene;

        public readonly Canvas Canvas = new();
        public readonly EventSystem EventSystem;
        public readonly Updatables Updatables = new();

        public RectI GameViewport { get; private set; }
        public int RenderRevision;
        public int RenderHash => Canvas.Revision + RenderRevision + ScenePasses.GetRenderHash() + Handles.RenderHash;

        public ObservableCollection<object> Editables = new();

        ShadowPass shadowPass;
        ClearPass clearPass;
        SkyboxPass skyboxPass;
        BasePass basePass;
        TransparentPass transPass;
        GTAOPass gtaoPass;
        DeferredPass deferredPass;
        VolumetricGatherPass volGatherPass;
        VolumetricFogPass fogPass;
        HiZPass highZPass;
        BloomPass bloomPass;
        TemporalJitter temporalJitter;
        PostProcessPass postProcessPass;
        DelegatePass canvasPass;
        FinalPass finalPass;

        RenderGraph renderGraph = new();
        ScenePassManager scenePasses;
        public ScenePassManager ScenePasses => scenePasses;

        public TemporalJitter TemporalPass => temporalJitter;

        public Play Play;

        public GameRoot() {
            using (new ProfilerMarker("Create Scene").Auto()) {
                Scene = new();
            }
            using (new ProfilerMarker("Create EventSystem").Auto()) {
                EventSystem = new(Canvas);
            }
        }
        public JobHandle Initialise() {
            var playJob = JobResult<Play>.Schedule(() => {
                using var marker = new ProfilerMarker("Create Play").Auto();
                return new Play(this);
            });
            var passesJob = JobHandle.Schedule(() => {
                using var marker = new ProfilerMarker("Create Passes").Auto();
                ScenePassManager passes;
                using (new ProfilerMarker("Create SceneManager").Auto()) {
                    passes = new(Scene);
                }
                SetupPasses(passes);
                scenePasses = passes;
            });
            return passesJob.Join(playJob.Handle).Then(() => {
                using var marker = new ProfilerMarker("Play Init").Auto();
                Play = playJob.Complete();
                Play.Initialise();
                volGatherPass.SetParticleSystem(Play.ParticleManager);
            });
        }
        public void Dispose() {
            Play.Dispose();
            Canvas.Dispose();
        }

        private async void SetupPasses(ScenePassManager scenePasses) {
            using (new ProfilerMarker("Setup Passes").Auto()) {
                Action updateShadowParameters = () => {
                    basePass.UpdateShadowParameters(shadowPass);
                    transPass.UpdateShadowParameters(shadowPass);
                    fogPass?.UpdateShadowParameters(shadowPass);
                    skyboxPass?.UpdateShadowParameters(shadowPass);
                    deferredPass?.UpdateShadowParameters(shadowPass);
                };
                shadowPass = new ShadowPass(Scene) {
                    OnRender = (graphics) => {
                        RenderBasePass(graphics, shadowPass);
                    },
                    OnPostRender = () => {
                        updateShadowParameters();
                    }
                };
                clearPass = new();
                skyboxPass = new(scenePasses);
                skyboxPass.OverrideMaterial.InheritProperties(scenePasses.MainSceneMaterial);
                skyboxPass.OverrideMaterial.InheritProperties(Scene.RootMaterial);
                basePass = new BasePass(Scene) {
                    OnPrepare = (graphics) => {
                        PrepareBasePass(graphics, basePass);
                    },
                    OnRender = (graphics) => {
                        RenderBasePass(graphics, basePass);
                    },
                };
                transPass = new TransparentPass(Scene) {
                    OnRender = (graphics) => {
                        RenderBasePass(graphics, transPass);
                    }
                };
                gtaoPass = new(scenePasses);
                gtaoPass.OverrideMaterial.InheritProperties(scenePasses.MainSceneMaterial);
                gtaoPass.OverrideMaterial.InheritProperties(Scene.RootMaterial);
                volGatherPass = new(scenePasses);
                volGatherPass.OverrideMaterial.InheritProperties(scenePasses.MainSceneMaterial);
                volGatherPass.OverrideMaterial.InheritProperties(Scene.RootMaterial);
                fogPass = new(scenePasses);
                fogPass.OverrideMaterial.InheritProperties(scenePasses.MainSceneMaterial);
                fogPass.OverrideMaterial.InheritProperties(Scene.RootMaterial);
                deferredPass = new(scenePasses);
                deferredPass.OverrideMaterial.InheritProperties(scenePasses.MainSceneMaterial);
                deferredPass.OverrideMaterial.InheritProperties(fogPass.OverrideMaterial);
                deferredPass.OverrideMaterial.InheritProperties(Scene.RootMaterial);
                highZPass = new();
                bloomPass = new();
                temporalJitter = new TemporalJitter("TJitter") {
                    ScenePasses = scenePasses,
                };
                updateShadowParameters();

                postProcessPass = new();
                canvasPass = new DelegatePass("Canvas",
                    new[] { new RenderPass.PassInput("SceneColor", false) },
                    new[] { new RenderPass.PassOutput("SceneColor", 0), },
                    (CSGraphics graphics, ref RenderPass.Context context) => {
                        Canvas.Render(graphics);
                    });
                finalPass = new("Final",
                    new[] { new RenderPass.PassInput("SceneColor", false) },
                    new[] { new RenderPass.PassOutput("SceneColor", 0), },
                    (CSGraphics graphics, ref RenderGraph.CustomTexturesContext context) => {
                        context.OverwriteOutput(context.Outputs[0], graphics.GetSurface().GetBackBuffer());
                        return true;
                    });

                scenePasses.AddPass(shadowPass);
                scenePasses.AddPass(basePass);
                scenePasses.AddPass(transPass);
            }
        }

        public void SetViewport(RectI gameViewport) {
            if (GameViewport == gameViewport) return;
            GameViewport = gameViewport;
            RenderRevision++;
            Play.Camera.Aspect = (float)gameViewport.Size.X / gameViewport.Size.Y;
            Canvas.SetSize(gameViewport.Size);
        }

        public void Update(float dt) {
            using var updateMarker = ProfileMarker_Update.Auto();
            UnityEngine.Time.Update(dt);

            EventSystem.Update(dt);

            Updatables.Invoke(dt);

            Canvas.Update(dt);

            Play.Update(dt);
            PreRender();

            Scene.RootMaterial.SetValue("Time", UnityEngine.Time.time);
        }

        public void PreRender() {
            using var updateMarker = ProfileMarker_PreRender.Auto();
            Play.PreRender();
            var Camera = Play.Camera;
            if (scenePasses.SetViewProjection(Camera.GetViewMatrix(), Camera.GetProjectionMatrix())) {
                RenderRevision++;
            }
            var activeArea = Play.LandscapeRenderer.GetVisibleBounds(scenePasses.Frustum);
            activeArea = BoundingBox.Union(activeArea, Play.Scene.GetActiveBounds());
            if (activeArea.Extents.X <= 0f) {
                activeArea = new BoundingBox(new Vector3(0f), new Vector3(1f));
            }
            if (shadowPass.UpdateShadowFrustum(scenePasses.Frustum, activeArea)) {
                RenderRevision++;
            }
            if (Play.DrawVisibilityVolume) shadowPass.DrawVolume();
            foreach (var pass in scenePasses.ScenePasses) {
                if (pass.GetHasSceneChanges()) ++RenderRevision;
            }
            Canvas.RequireComposed();
        }
        public void Render(CSGraphics graphics, float dt) {
            using var updateMarker = ProfileMarker_Render.Auto();
            // This requires graphics calls, do it first
            // TODO: Check visibility and dont process culled
            Play.RenderUpdate(graphics, dt);

            // TODO: Avoid calling this twice when TAA is enabled
            ScenePasses.SetupRender(GameViewport.Size);

            //Camera.FarPlane = 45f + (0.5f + 0.5f * MathF.Sin(UnityEngine.Time.time * 10.0f)) * 400.0f;
            renderGraph.Clear();

            // Render shadows
            renderGraph.BeginPass(shadowPass);

            // Render scene color
            renderGraph.BeginPass(clearPass);
            renderGraph.BeginPass(basePass);
            renderGraph.BeginPass(highZPass);
            if (Play.EnableAO && gtaoPass != null) renderGraph.BeginPass(gtaoPass);
            deferredPass.SetAOEnabled(Play.EnableAO);
            deferredPass.SetFogEnabled(Play.EnableFog);
            renderGraph.BeginPass(deferredPass);
            renderGraph.BeginPass(skyboxPass);
            renderGraph.BeginPass(transPass);
            if (Play.EnableFog && fogPass != null) {
                //renderGraph.BeginPass(volGatherPass);
                renderGraph.BeginPass(fogPass);
            }

            // Intercept render to set up jitter offset
            renderGraph.BeginPass(temporalJitter);

            // Render post processing
            renderGraph.BeginPass(bloomPass);
            renderGraph.BeginPass(postProcessPass);

            // Render UI
            renderGraph.BeginPass(canvasPass);

            // Set render region/target
            renderGraph.BeginPass(finalPass)
                .SetViewport(GameViewport)
                .SetOutput("SceneColor", graphics.GetSurface().GetBackBuffer());

            renderGraph.Execute(finalPass, graphics);

            // Copy current matrices into previous frame slots
            Scene.CommitMotion();
        }
        public void ResetFrame() {
            using var updateMarker = ProfileMarker_ResetFrame.Auto();
            // Clear dynamic meshes (probably added in Update() )
            ScenePasses.ClearDynamicDraws();

            Handles.Reset();
        }

        private void PrepareBasePass(CSGraphics graphics, ScenePass pass) {
            Play.PrepareBasePass(graphics, pass);
        }
        private void RenderBasePass(CSGraphics graphics, ScenePass pass) {
            Play.RenderBasePass(graphics, pass);
            Handles.Render(graphics, pass);
        }

        public void RegisterEditable(object obj, bool enable) {
            if (enable) Editables.Add(obj);
            else Editables.Remove(obj);
        }

        public void AttachToEditor(EditorWindow editorWindow) {
            editorWindow.GameView.EventSystem = this.EventSystem;
            editorWindow.GameView.Camera = this.Play.Camera;
            editorWindow.GameView.Scene = this.ScenePasses;
            editorWindow.GameView.OnReceiveDrag += (events, item) => {
                var name = Path.GetFileNameWithoutExtension(item.FilePath);
                var mpos = Canvas.GetComputedLayout().InverseTransformPosition2DN(events.CurrentPosition);
                var mray = Play.Camera.ViewportToRay(mpos);
                var pos = mray.ProjectTo(new Plane(Vector3.UnitY, 0f));

                if (item.FilePath.EndsWith(".json")) {
                    var particleSystem = Play.ParticleManager.RequireSystemFromJSON(Scene, item.FilePath);
                    if (particleSystem != null) {
                        var emitter = particleSystem.CreateEmitter(pos);
                        var entity = Play.World.CreateEntity(name);
                        Play.World.AddComponent<ECTransform>(entity, new(SimulationWorld.WorldToSimulation(pos).XZ));
                        Play.World.AddComponent<ECParticleBinding>(entity, new() { Emitter = emitter, });
                    }
                }
                if (item.FilePath.EndsWith(".fbx")) {
                    var entity = Play.World.CreateEntity(name);
                    Play.World.AddComponent<ECTransform>(entity, new(SimulationWorld.WorldToSimulation(pos).XZ));
                    Play.World.AddComponent<CModel>(entity, new() { Model = Resources.LoadModel(item.FilePath), });
                }
            };
            editorWindow.Hierarchy.World = this.Play.World;
            // When selecting an entity's UI, select it in Play
            editorWindow.Hierarchy.OnEntitySelected += (entity, selected) => {
                var item = this.Play.Simulation.EntityProxy.MakeHandle(entity);
                if (selected) this.Play.SelectionManager.AppendSelected(item);
                else this.Play.SelectionManager.RemoveSelected(item);
            };
            editorWindow.Inspector.AppendEditables(this.Editables);
            // If Play selection changes, force update project selection
            this.Play.SelectionManager.OnSelectionChanged += (selection) => {
                using var scope = new Weesals.UI.SelectionManager.Scope(editorWindow.Editor.ProjectSelection);
                foreach (var item in selection) {
                    var view = editorWindow.Hierarchy.GetEntityView(item.GetEntity());
                    if (view != null) { scope.Append(new(view)); continue; }
                    scope.Append(item);
                }
                //editorWindow.Editor.ProjectSelection.SetSelectedItems(selection);
            };
            editorWindow.EventSystem.KeyboardFilter.Insert(0, this.EventSystem);
        }
    }
}
