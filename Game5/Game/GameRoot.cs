using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
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
        public int RenderHash => Canvas.Revision + RenderRevision + ScenePasses.GetRenderHash();

        public ObservableCollection<object> Editables = new();

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

        public TemporalJitter TemporalPass => temporalJitter;

        public Play Play;

        public GameRoot(Scene scene) {
            Scene = scene;
            scenePasses = new(scene);
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
                    graphics.SetViewport(new RectI(Int2.Zero, context.Viewport.Size));
                    graphics.Clear();
                });
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
            highZPass = new();
            aoPass = new();
            bloomPass = new();
            temporalJitter = new TemporalJitter("TJitter") {
                ScenePasses = scenePasses,
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
            EventSystem.Update(dt);

            Updatables.Invoke(dt);

            Canvas.Update(dt);

            Play.Update(dt);

            Scene.RootMaterial.SetValue("Time", UnityEngine.Time.time);
        }

        public void PreRender(CSGraphics graphics, float dt) {
            var Camera = Play.Camera;
            if (scenePasses.SetViewProjection(Camera.GetViewMatrix(), Camera.GetProjectionMatrix())) {
                RenderRevision++;
            }
            var activeArea = Play.LandscapeRenderer.GetVisibleBounds(scenePasses.Frustum);
            activeArea.Max.Y += 3.0f;   // Size of a building
            if (shadowPass.UpdateShadowFrustum(scenePasses.Frustum, activeArea)) {
                RenderRevision++;
            }
            foreach (var pass in scenePasses.ScenePasses) {
                if (pass.GetHasSceneChanges()) ++RenderRevision;
            }
            Play.PreRender(graphics, dt);
            Canvas.RequireComposed();
        }
        public void Render(CSGraphics graphics) {
            // TODO: Avoid calling this twice when TAA is enabled
            ScenePasses.BeginRender(GameViewport.Size);

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
            Play.RenderBasePass(graphics, pass);
            Handles.Render(graphics, pass);
        }

        public void RegisterEditable(object obj, bool enable) {
            if (enable) Editables.Add(obj);
            else Editables.Remove(obj);
        }
    }
}
