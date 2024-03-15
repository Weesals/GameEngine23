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
using Weesals.Landscape;
using Weesals.UI;
using Weesals.Utility;

namespace Game5.Game {
    public class GameRoot {

        public readonly Scene Scene;

        public readonly Canvas Canvas = new();
        public readonly EventSystem EventSystem;
        public readonly Updatables Updatables = new();

        public RectI GameViewport { get; private set; }
        public int RenderRevision;
        public int RenderHash => Canvas.Revision + RenderRevision + ScenePasses.GetRenderHash() + Handles.RenderHash;

        public ObservableCollection<object> Editables = new();

        ShadowPass shadowPass;
        DeferredPass clearPass;
        SkyboxPass skyboxPass;
        BasePass basePass;
        TransparentPass transPass;
        VolumetricFogPass fogPass;
        HiZPass highZPass;
        AmbientOcclusionPass aoPass;
        BloomPass bloomPass;
        TemporalJitter temporalJitter;
        PostProcessPass postProcessPass;
        DeferredPass canvasPass;
        FinalPass finalPass;

        RenderGraph renderGraph = new();
        ScenePassManager scenePasses;
        public ScenePassManager ScenePasses => scenePasses;

        public TemporalJitter TemporalPass => temporalJitter;

        public Play Play;

        public GameRoot() {
            Scene = new();
            scenePasses = new(Scene);
            EventSystem = new(Canvas);

            SetupPasses();
        }
        public void Dispose() {
            Play.Dispose();
            Canvas.Dispose();
        }

        private void SetupPasses() {
            shadowPass = new ShadowPass(Scene) {
                OnPreRender = (graphics) => {
                    RenderBasePass(graphics, shadowPass);
                },
                OnPostRender = () => {
                    basePass.UpdateShadowParameters(shadowPass);
                    transPass.UpdateShadowParameters(shadowPass);
                    fogPass?.UpdateShadowParameters(shadowPass);
                    skyboxPass?.UpdateShadowParameters(shadowPass);
                }
            };
            clearPass = new DeferredPass("Clear",
                default,
                new[] {
                    new RenderPass.PassOutput("SceneDepth").SetTargetDesc(new TextureDesc() { Format = BufferFormat.FORMAT_D24_UNORM_S8_UINT, }),
                    new RenderPass.PassOutput("SceneColor"),
                    new RenderPass.PassOutput("SceneVelId"),
                },
                (CSGraphics graphics, ref RenderPass.Context context) => {
                    graphics.SetViewport(context.Viewport);
                    graphics.Clear();
                });
            skyboxPass = new() { ScenePasses = scenePasses };
            basePass = new BasePass(Scene) {
                OnPreRender = (graphics) => {
                    RenderBasePass(graphics, basePass);
                },
            };
            transPass = new TransparentPass(Scene) {
                OnPreRender = (graphics) => {
                    RenderBasePass(graphics, transPass);
                }
            };
            fogPass = new() { ScenePasses = scenePasses, };
            fogPass?.OverrideMaterial.InheritProperties(Scene.RootMaterial);
            highZPass = new();
            aoPass = new();
            bloomPass = new();
            temporalJitter = new TemporalJitter("TJitter") {
                ScenePasses = scenePasses,
            };
            basePass.UpdateShadowParameters(shadowPass);
            transPass.UpdateShadowParameters(shadowPass);
            fogPass?.UpdateShadowParameters(shadowPass);
            skyboxPass?.UpdateShadowParameters(shadowPass);
            postProcessPass = new();
            canvasPass = new DeferredPass("Canvas",
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

            Play = new Play(this);
        }

        public void SetViewport(RectI gameViewport) {
            if (GameViewport == gameViewport) return;
            GameViewport = gameViewport;
            RenderRevision++;
            Play.Camera.Aspect = (float)gameViewport.Size.X / gameViewport.Size.Y;
            Canvas.SetSize(gameViewport.Size);
        }

        public void Update(float dt) {
            UnityEngine.Time.Update(dt);

            EventSystem.Update(dt);

            Updatables.Invoke(dt);

            Canvas.Update(dt);

            Play.Update(dt);
            PreRender();

            Scene.RootMaterial.SetValue("Time", UnityEngine.Time.time);
        }

        public void PreRender() {
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
            Play.PreRender();
            Canvas.RequireComposed();
        }
        public void Render(CSGraphics graphics, float dt) {
            // This requires graphics calls, do it first
            // TODO: Check visibility and dont process culled
            Play.UpdateParticles(graphics, dt);

            // TODO: Avoid calling this twice when TAA is enabled
            ScenePasses.SetupRender(GameViewport.Size);

            //Camera.FarPlane = 45f + (0.5f + 0.5f * MathF.Sin(UnityEngine.Time.time * 10.0f)) * 400.0f;
            renderGraph.Clear();

            // Render shadows
            renderGraph.BeginPass(shadowPass);

            // Render scene color
            renderGraph.BeginPass(clearPass);
            renderGraph.BeginPass(basePass);
            renderGraph.BeginPass(skyboxPass);
            //renderGraph.BeginPass(highZPass);
            //renderGraph.BeginPass(aoPass);
            renderGraph.BeginPass(transPass);
            if (Play.EnableFog && fogPass != null) renderGraph.BeginPass(fogPass);

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
            // Clear dynamic meshes (probably added in Update() )
            ScenePasses.ClearDynamicDraws();

            Handles.Reset();
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
                    var particleSystem = Play.ParticleManager.RequireSystemFromJSON(item.FilePath);
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
            editorWindow.Hierarchy.OnEntitySelected += (entity, selected) => {
                var item = this.Play.Simulation.EntityProxy.MakeHandle(entity);
                if (selected) this.Play.SelectionManager.AppendSelected(item);
                else this.Play.SelectionManager.RemoveSelected(item);
            };
            editorWindow.Inspector.AppendEditables(this.Editables);
            this.Play.SelectionManager.OnEntitySelected += (entity, selected) => {
                editorWindow.Hierarchy.NotifySelected(entity.GetEntity(), selected);
            };
            this.Play.SelectionManager.OnSelectionChanged += (selection) => {
                foreach (var selected in selection) {
                    if (selected.Owner is LandscapeRenderer landscape) {
                        editorWindow.ActivateLandscapeTools(landscape);
                        return;
                    }
                    var entity = selected;
                    if (entity.Owner is IEntityRedirect redirect)
                        entity = redirect.GetOwner(entity.Data);
                    if (entity.Owner is World world) {
                        editorWindow.ActivateEntityInspector(world, entity.GetEntity());
                        return;
                    }
                }
                editorWindow.Inspector.SetInspector(default);
            };
            editorWindow.EventSystem.KeyboardFilter.Insert(0, this.EventSystem);
        }
    }
}
