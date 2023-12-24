using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Weesals.Utility;

namespace Weesals.Engine {
    /*
     * RenderPasses can have multiple inputs and outputs
     * Inputs can be write-only (transparent rendering on top of opaque pass)
     * Outputs can be RT or direct to screen
     */
    public struct TextureDesc {
        public Int2 Size = 0;
        public BufferFormat Format;
        public int MipCount = 1;
        public TextureDesc() { }
        public override string ToString() { return $"<{Size}:{Format}:{MipCount}>"; }
    }
    public interface ICustomOutputTextures {
        bool FillTextures(ref RenderGraph.CustomTexturesContext context);
    }
    public class RenderPass {
        protected static Material blitMaterial;
        protected static Mesh quadMesh;

        public struct PassInput {
            public readonly CSIdentifier Name;
            public readonly bool RequireAttachment;
            public PassInput(CSIdentifier name, bool requireAttachment = true) { Name = name; RequireAttachment = requireAttachment; }
            public override string ToString() { return Name.ToString(); }
        }
        public struct PassOutput {
            public readonly CSIdentifier Name;
            public readonly int PassthroughInput;
            public TextureDesc TargetDesc { get; private set; } = new();
            public PassOutput(CSIdentifier name, int passthroughInput = -1) {
                Name = name; PassthroughInput = passthroughInput;
            }
            public PassOutput SetTargetDesc(TextureDesc desc) { TargetDesc = desc; return this; }
            public override string ToString() { return Name.ToString(); }
        }
        public struct IOContext : IDisposable {
            public readonly RenderGraph Graph;
            public readonly int PassId;
            private PooledList<PassInput> inputs;
            private PooledList<PassOutput> outputs;

            public IOContext(RenderGraph renderGraph, int passId) : this() {
                Graph = renderGraph;
                PassId = passId;
                inputs = new();
                outputs = new();
            }
            public RectI GetViewport() {
                return Graph.GetPassData(PassId).Viewport;
            }

            public void AddInput(PassInput input) { inputs.Add(input); }
            public void AddOutput(PassOutput output) { outputs.Add(output); }
            public void Dispose() {
                inputs.Dispose();
                outputs.Dispose();
            }

            public Span<PassInput> GetInputs() { return inputs; }
            public Span<PassOutput> GetOutputs() { return outputs; }
        }

        public readonly string Name;
        public PassInput[] Inputs { get; set; } = Array.Empty<PassInput>();
        // First item is always Depth
        public PassOutput[] Outputs { get; set; } = Array.Empty<PassOutput>();

        //public CSRenderTarget RenderTarget { get; protected set; }
        public Material OverrideMaterial { get; protected set; }

        private List<RenderPass> dependencies = new();

        public RenderPass(string name) {
            Name = name;
            OverrideMaterial = new();
            OverrideMaterial.SetValue("View", Matrix4x4.Identity);
            OverrideMaterial.SetValue("Projection", Matrix4x4.Identity);
        }

        public virtual void GetInputOutput(ref IOContext context) {
            var viewport = context.GetViewport();
            foreach (var input in Inputs) {
                context.AddInput(input);
            }
            foreach (var output in Outputs) context.AddOutput(output);
        }
        /*public virtual void AdjustParameters(ref ParamContext context) {

        }*/

        public Material GetPassMaterial() {
            return OverrideMaterial;
        }

        public void RegisterDependency(RenderPass other) {
            dependencies.Add(other);
        }
        public int FindInputI(CSIdentifier name) {
            for (int i = 0; i < Inputs.Length; ++i) if (Inputs[i].Name == name) return i;
            return -1;
        }
        public int FindOutputI(CSIdentifier identifier) {
            for (int i = 0; i < Outputs.Length; i++) if (Outputs[i].Name == identifier) return i;
            return -1;
        }

        /*private CSRenderTarget RequireRenderTarget() {
            if (!RenderTarget.IsValid()) {
                var target = CSRenderTarget.Create();
                target.SetSize(targetDesc.Size != 0 ? targetDesc.Size : 1024);
                if (targetDesc.Format != BufferFormat.FORMAT_UNKNOWN)
                    target.SetFormat(targetDesc.Format);
                RenderTarget = target;
            }
            return RenderTarget;
        }
        public void SetDependency(CSIdentifier name, RenderPass other) {
            var rt = other.RequireRenderTarget();
            GetPassMaterial().SetTexture(name, rt);
        }*/

        public struct Target {
            public CSRenderTarget Texture;
            public int Mip;
            public int Slice;
            public Target(CSRenderTarget texture) {
                Texture = texture;
                Mip = 0;
                Slice = 0;
            }
            public bool IsValid() { return Texture.IsValid(); }
            public override string ToString() { return $"{Texture}@{Mip}.{Slice}"; }
        }
        public ref struct Context {
            public Target ResolvedDepth;
            public readonly Span<Target> ResolvedTargets;
            public RectI Viewport;
            public Context(Target depth, Span<Target> targets) { ResolvedDepth = depth; ResolvedTargets = targets; }
        }
        protected void BindRenderTargets(CSGraphics graphics, ref Context context) {
            var colorTargets = new PooledList<CSRenderTarget>();
            foreach (var item in context.ResolvedTargets) colorTargets.Add(item.Texture);
            graphics.SetRenderTargets(colorTargets, context.ResolvedDepth.Texture);
            if (context.Viewport.Width > 0) graphics.SetViewport(context.Viewport);
        }
        public virtual void Render(CSGraphics graphics, ref Context context) {
            BindRenderTargets(graphics, ref context);
        }
        unsafe protected void DrawQuad(CSGraphics graphics, CSTexture texture, Material material = null) {
            if (material == null) {
                if (blitMaterial == null) {
                    blitMaterial = new Material("./assets/blit.hlsl");
                    blitMaterial.SetBlendMode(BlendMode.MakeOpaque());
                    blitMaterial.SetDepthMode(DepthMode.MakeOff());
                }
                material = blitMaterial;
            }
            if (quadMesh == null) {
                quadMesh = new Mesh("Quad");
                quadMesh.RequireVertexPositions(BufferFormat.FORMAT_R32G32B32_FLOAT);
                quadMesh.RequireVertexTexCoords(0, BufferFormat.FORMAT_R32G32_FLOAT);
                quadMesh.SetVertexCount(4);
                quadMesh.SetIndexCount(6);
                Span<Vector3> verts = stackalloc Vector3[] { new Vector3(-1f, 1f, 0f), new Vector3(1f, 1f, 0f), new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f), };
                quadMesh.GetPositionsV().Set(verts);
                Span<Vector2> uvs = stackalloc Vector2[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), };
                quadMesh.GetTexCoordsV(0).Set(uvs);
                Span<int> inds = stackalloc int[] { 0, 1, 2, 1, 3, 2, };
                quadMesh.SetIndices(inds);
            }

            if (texture.IsValid()) {
                material.SetTexture("Texture", texture);
                material.SetValue("TextureSize", (Vector2)texture.GetSize());
            }
            Span<CSBufferLayout> bindings = stackalloc CSBufferLayout[2];
            bindings[0] = quadMesh.IndexBuffer;
            bindings[1] = quadMesh.VertexBuffer;
            var pso = MaterialEvaluator.ResolvePipeline(graphics, bindings, new Span<Material>(ref material));
            var resources = MaterialEvaluator.ResolveResources(graphics, pso, new Span<Material>(ref material));
            fixed (CSBufferLayout* bindingsPtr = bindings) {
                graphics.Draw(pso, new MemoryBlock<CSBufferLayout>(bindingsPtr, bindings.Length), resources, CSDrawConfig.MakeDefault());
            }
        }
        public override string ToString() { return Name; }
    }
    public class ScenePass : RenderPass {
        public readonly RenderQueue RenderQueue;
        public readonly RetainedRenderer RetainedRenderer;
        public Scene Scene => RetainedRenderer.Scene;
        public Matrix4x4 View { get; protected set; }
        public Matrix4x4 Projection { get; protected set; }
        public Frustum Frustum { get; protected set; }

        public RenderTags TagsToInclude = RenderTags.Default;
        public RenderTags TagsToExclude = RenderTags.None;

        public Action<CSGraphics> PreRender;

        public ScenePass(Scene scene, string name) : base(name) {
            RenderQueue = new();
            RetainedRenderer = new(scene);
        }

        public bool SetViewProjection(in Matrix4x4 view, in Matrix4x4 proj) {
            if (View == view && Projection == proj) return false;
            View = view;
            Projection = proj;
            Frustum = new Frustum(view * proj);
            OverrideMaterial.SetValue(RootMaterial.iVMat, view);
            OverrideMaterial.SetValue(RootMaterial.iPMat, proj);
            return true;
        }
        public Frustum GetFrustum() {
            return Frustum;
        }

        public void AddInstance(CSInstance instance, Mesh mesh, Span<Material> materials) {
            RetainedRenderer.AppendInstance(mesh, materials, instance.GetInstanceId());
        }
        public void SetVisible(CSInstance instance, bool visible) {
            RetainedRenderer.SetVisible(instance.GetInstanceId(), visible);
        }
        public void RemoveInstance(CSInstance instance) {
            RetainedRenderer.RemoveInstance(instance.GetInstanceId());
        }

        public override void Render(CSGraphics graphics, ref Context context) {
            base.Render(graphics, ref context);
            RenderQueue.Clear();
            RenderScene(graphics, ref context);
        }
        public virtual void RenderScene(CSGraphics graphics, ref Context context) {
            PreRender?.Invoke(graphics);
            RetainedRenderer.SubmitToRenderQueue(graphics, RenderQueue, Frustum);
            Scene.SubmitToGPU(graphics);
            RenderQueue.Render(graphics);
        }
    }

    public class ShadowPass : ScenePass {
        public ShadowPass(Scene scene) : base(scene, "Shadows") {
            Outputs = new[] { new PassOutput("ShadowMap").SetTargetDesc(new TextureDesc() { Size = 1024, Format = BufferFormat.FORMAT_D24_UNORM_S8_UINT, }) };
            OverrideMaterial.SetRenderPassOverride("ShadowCast");
        }
        public bool UpdateShadowFrustum(Frustum frustum) {
            // Create shadow projection based on frustum near/far corners
            Span<Vector3> corners = stackalloc Vector3[8];
            frustum.GetCorners(corners);
            var lightViewMatrix = Matrix4x4.CreateLookAt(new Vector3(40, 50, -70), Vector3.Zero, Vector3.UnitY);
            foreach (ref var corner in corners) corner = Vector3.Transform(corner, lightViewMatrix);
            var lightFMin = new Vector3(float.MaxValue);
            var lightFMax = new Vector3(float.MinValue);
            foreach (var corner in corners) {
                lightFMin = Vector3.Min(lightFMin, corner);
                lightFMax = Vector3.Max(lightFMax, corner);
            }
            // Or project onto terrain if smaller
            frustum.IntersectPlane(Vector3.UnitY, 0.0f, corners);
            frustum.IntersectPlane(Vector3.UnitY, 5.0f, corners.Slice(4));
            foreach (ref var corner in corners) corner = Vector3.Transform(corner, lightViewMatrix);
            var lightTMin = new Vector3(float.MaxValue);
            var lightTMax = new Vector3(float.MinValue);
            foreach (var corner in corners) {
                lightTMin = Vector3.Min(lightTMin, corner);
                lightTMax = Vector3.Max(lightTMax, corner);
            }

            var lightMin = Vector3.Max(lightFMin, lightTMin);
            var lightMax = Vector3.Min(lightFMax, lightTMax);
            var lightSize = lightMax - lightMin;

            lightViewMatrix.Translation = lightViewMatrix.Translation - (lightMin + lightMax) / 2.0f;
            var lightProjMatrix = Matrix4x4.CreateOrthographic(lightSize.X, lightSize.Y, -lightSize.Z / 2.0f, lightSize.Z / 2.0f);
            return SetViewProjection(
                lightViewMatrix,
                lightProjMatrix
            );
        }
        public override void RenderScene(CSGraphics graphics, ref Context context) {
            graphics.Clear();
            base.RenderScene(graphics, ref context);
        }
    }
    public class MainPass : ScenePass {
        public MainPass(Scene scene, string name) : base(scene, name) {
        }
        public void UpdateShadowParameters(ScenePass shadowPass) {
            var basePassMat = OverrideMaterial;
            var shadowPassViewProj = shadowPass.View * shadowPass.Projection;
            Matrix4x4.Invert(View, out var basePassInvView);
            basePassMat.SetValue("ShadowViewProjection", shadowPassViewProj);
            basePassMat.SetValue("ShadowIVViewProjection", basePassInvView * shadowPassViewProj);
            basePassMat.SetValue("_WorldSpaceLightDir0", Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ, basePassInvView)));
            basePassMat.SetValue("_LightColor0", 2 * new Vector3(1.0f, 0.98f, 0.95f) * 2.0f);
        }
    }
    public class BasePass : MainPass {
        public BasePass(Scene scene) : base(scene, "BasePass") {
            Inputs = new[] {
                new PassInput("SceneDepth", false),
                new PassInput("SceneColor", false),
                new PassInput("SceneVelId", false),
                new PassInput("ShadowMap"),
            };
            Outputs = new[] {
                new PassOutput("SceneDepth", 0).SetTargetDesc(new TextureDesc() { Size = -1, Format = BufferFormat.FORMAT_D24_UNORM_S8_UINT, }),
                new PassOutput("SceneColor", 1).SetTargetDesc(new TextureDesc() { Size = -1, Format = BufferFormat.FORMAT_R11G11B10_FLOAT, MipCount = 1, }),
                new PassOutput("SceneVelId", 2).SetTargetDesc(new TextureDesc() { Size = -1, Format = BufferFormat.FORMAT_R8G8B8A8_SNORM, MipCount = 1, }),
            };
            TagsToInclude.Add(scene.TagManager.RequireTag("MainPass"));
        }
    }
    public class TransparentPass : MainPass {
        public TransparentPass(Scene scene) : base(scene, "TransparentPass") {
            Inputs = new[] {
                new PassInput("SceneDepth"),
                new PassInput("SceneColor", false),
                new PassInput("ShadowMap"),
            };
            Outputs = new[] {
                new PassOutput("SceneDepth", 0).SetTargetDesc(new TextureDesc() { Size = -1, Format = BufferFormat.FORMAT_D24_UNORM_S8_UINT, }),
                new PassOutput("SceneColor", 1).SetTargetDesc(new TextureDesc() { Size = -1, }),
            };
            TagsToInclude.Clear();
            TagsToInclude.Add(scene.TagManager.RequireTag("Transparent"));
            TagsToInclude.Add(scene.TagManager.RequireTag("MainPass"));
            GetPassMaterial().SetBlendMode(BlendMode.MakeAlphaBlend());
            GetPassMaterial().SetDepthMode(DepthMode.MakeReadOnly());
        }
    }
    public class TemporalJitter : RenderPass, ICustomOutputTextures {
        public ScenePassManager ScenePasses;
        public Action<Int2>? OnBegin;
        public DeferredPass.RenderPassEvaluator? OnRender;
        private Material temporalMaterial;
        CSRenderTarget[] targets = new CSRenderTarget[2];
        CSRenderTarget accumulation;
        int frame = 0;
        private Matrix4x4 previousViewProj;
        public TemporalJitter(string name) : base(name) {
            Inputs = new[] { new RenderPass.PassInput("SceneDepth"), new RenderPass.PassInput("SceneColor", true), new RenderPass.PassInput("SceneVelId", true), };
            Outputs = new[] { new RenderPass.PassOutput("SceneColor").SetTargetDesc(new TextureDesc() { Size = -1, }), };
            temporalMaterial = new Material("./assets/temporal.hlsl", GetPassMaterial());
            //temporalMaterial.SetBlendMode(BlendMode.MakeAlphaBlend());
        }
        public bool FillTextures(ref RenderGraph.CustomTexturesContext context) {
            ++frame;
            int targetId = frame % 2;
            if (!targets[targetId].IsValid() || targets[targetId].GetSize() != context.Viewport.Size) {
                if (targets[targetId].IsValid()) targets[targetId].Dispose();
                targets[targetId] = CSRenderTarget.Create("Temporal " + targetId);
                targets[targetId].SetSize(context.Viewport.Size);
                targets[targetId].SetFormat(BufferFormat.FORMAT_R11G11B10_FLOAT);
            }
            /*if (!accumulation.IsValid() || accumulation.GetSize() != context.Viewport.Size) {
                if (accumulation.IsValid()) accumulation.Dispose();
                accumulation = CSRenderTarget.Create();
                accumulation.SetSize(context.Viewport.Size);
                accumulation.SetFormat(BufferFormat.FORMAT_R11G11B10_FLOAT);
            }*/
            //context.OverwriteInput(context.Inputs[1], targets[targetId]);
            context.OverwriteOutput(context.Outputs[0], targets[targetId]);
            OnBegin?.Invoke(context.Viewport.Size);
            return true;
        }
        public override void Render(CSGraphics graphics, ref Context context) {
            base.Render(graphics, ref context);
            var viewProj = ScenePasses.View * ScenePasses.Projection;
            Matrix4x4.Invert(previousViewProj, out var invPrevVP);
            Matrix4x4.Invert(viewProj, out var invVP);
            temporalMaterial.SetValue("PreviousVP", previousViewProj);
            temporalMaterial.SetValue("CurrentVP", invVP);
            temporalMaterial.SetValue("TemporalJitter", ScenePasses.TemporalOffset * 0.5f);
            OnRender?.Invoke(graphics, ref context);
            //var targetId = frame % 2;
            //DrawQuad(graphics, GetPassMaterial().GetUniformTexture("SceneColor"));
            var sceneColor = GetPassMaterial().GetUniformTexture("SceneColor");
            var target0 = frame % 2;
            var target1 = (frame - 1) % 2;
            if (targets[target1].IsValid()) {
                temporalMaterial.SetTexture("CurrentFrame", sceneColor);
                temporalMaterial.SetTexture("PreviousFrame", targets[target1]);
                DrawQuad(graphics, default, temporalMaterial);
            } else {
                DrawQuad(graphics, sceneColor);
            }
            previousViewProj = viewProj;
        }
    }
    public class HiZPass : RenderPass, ICustomOutputTextures {
        protected Material firstPassMaterial;
        protected Material highZMaterial;
        public HiZPass() : base("HighZ") {
            Inputs = new[] {
                new PassInput("SceneDepth"),
            };
            Outputs = new[] {
                new PassOutput("HighZ").SetTargetDesc(new TextureDesc() { Size = -2, Format = BufferFormat.FORMAT_R8G8_UNORM, MipCount = 0, }),
            };
            firstPassMaterial = new Material("./assets/highz.hlsl", GetPassMaterial());
            firstPassMaterial.SetPixelShader(Resources.LoadShader("./assets/highz.hlsl", "FirstPassPS"));
            firstPassMaterial.SetBlendMode(BlendMode.MakeOpaque());
            firstPassMaterial.SetDepthMode(new DepthMode(DepthMode.Comparisons.Always, true));
            highZMaterial = new Material(firstPassMaterial);
            highZMaterial.SetPixelShader(Resources.LoadShader("./assets/highz.hlsl", "HighZPassPS"));
        }
        public bool FillTextures(ref RenderGraph.CustomTexturesContext context) {
            var inputSize = context.FindInputSize(0);
            if (inputSize.X <= 0) return false;
            var outputDesc = context.Outputs[0].Output.TargetDesc;
            outputDesc.Size = inputSize >> 1;
            context.Outputs[0].Output.SetTargetDesc(outputDesc);
            return true;
        }
        unsafe public override void Render(CSGraphics graphics, ref Context context) {
            var sceneDepth = context.ResolvedDepth;
            var size = sceneDepth.Texture.GetSize();
            for (int m = 1; m < sceneDepth.Texture.GetMipCount(); ++m) {
                bool isOdd = ((size.X | size.Y) & 0x01) != 0;
                size = Int2.Max(size >> 1, Int2.One);
                graphics.SetRenderTargets(default(CSRenderTargetBinding), new CSRenderTargetBinding(sceneDepth.Texture.mRenderTarget, m, 0));
                graphics.SetViewport(new RectI(Int2.Zero, size));
                firstPassMaterial.SetMacro("ODD", isOdd ? "1" : "0");
                DrawQuad(graphics, sceneDepth.Texture, firstPassMaterial);
            }
        }
    }
    public class BloomPass : RenderPass {
        protected Material bloomChainMaterial;
        public BloomPass() : base("Bloom") {
            Inputs = new[] {
                new PassInput("SceneColor"),
            };
            Outputs = new[] {
                new PassOutput("BloomChain").SetTargetDesc(new TextureDesc() { Size = -1, Format = BufferFormat.FORMAT_R11G11B10_FLOAT, MipCount = 7, }),
            };
            bloomChainMaterial = new Material("./assets/bloomchain.hlsl");
            bloomChainMaterial.SetBlendMode(BlendMode.MakeOpaque());
            bloomChainMaterial.SetDepthMode(DepthMode.MakeOff());
        }
        public override void GetInputOutput(ref IOContext context) {
            base.GetInputOutput(ref context);
            //var desc = Outputs[0].TargetDesc;
            //desc.MipCount = 
        }
        unsafe public override void Render(CSGraphics graphics, ref Context context) {
            var sceneColor = GetPassMaterial().GetUniformTexture("SceneColor");
            var bloomChain = context.ResolvedTargets[0];
            graphics.SetRenderTargets(new CSRenderTargetBinding(bloomChain.Texture.mRenderTarget, 0, 0), default);
            bloomChainMaterial.SetMacro("CopyPass", "1");
            DrawQuad(graphics, sceneColor, bloomChainMaterial);
            bloomChainMaterial.SetMacro("CopyPass", "0");
            var size = bloomChain.Texture.GetSize();
            for (int m = 1; m < bloomChain.Texture.GetMipCount(); ++m) {
                size = Int2.Max(size >> 1, Int2.One);
                graphics.SetRenderTargets(new CSRenderTargetBinding(bloomChain.Texture.mRenderTarget, m, 0), default);
                graphics.SetViewport(new RectI(Int2.Zero, size));
                DrawQuad(graphics, bloomChain.Texture, bloomChainMaterial);
            }
        }
    }
    public class PostProcessPass : RenderPass {
        protected Material postMaterial;
        public PostProcessPass() : base("PostProcess") {
            Inputs = new[] {
                new PassInput("SceneColor"),
                new PassInput("BloomChain"),
            };
            Outputs = new[] {
                new PassOutput("SceneColor").SetTargetDesc(new TextureDesc() { Size = -1, }),
            };
            postMaterial = new Material("./assets/postprocess.hlsl");
            postMaterial.SetBlendMode(BlendMode.MakePremultiplied());
        }
        public override void Render(CSGraphics graphics, ref Context context) {
            base.Render(graphics, ref context);
            DrawQuad(graphics, GetPassMaterial().GetUniformTexture("SceneColor"));
            postMaterial.SetTexture("BloomChain", GetPassMaterial().GetUniformTexture("BloomChain"));
            DrawQuad(graphics, GetPassMaterial().GetUniformTexture("BloomChain"), postMaterial);
        }
    }
    public class PresentPass : RenderPass {
        public PresentPass() : base("Present") { }
        public override void Render(CSGraphics graphics, ref Context context) {
            base.Render(graphics, ref context);
        }
    }

    public class DeferredPass : RenderPass {
        public delegate void RenderPassEvaluator(CSGraphics graphics, ref RenderPass.Context context);
        public RenderPassEvaluator OnRender;
        public DeferredPass(string name, PassInput[]? inputs, PassOutput[]? outputs, RenderPassEvaluator onRender) : base(name) {
            if (inputs != null) Inputs = inputs;
            if (outputs != null) Outputs = outputs;
            OnRender = onRender;
        }
        public override void Render(CSGraphics graphics, ref Context context) {
            base.Render(graphics, ref context);
            OnRender(graphics, ref context);
        }
    }

    public class WorldObject {
        public List<CSInstance> Meshes = new();
    }
    public class ScenePassManager {
        public readonly Scene Scene;
        private Matrix4x4 view, projection;
        private List<ScenePass> scenePasses = new();
        private List<CSInstance> dynamicInstances = new();

        public Matrix4x4 View => view;
        public Matrix4x4 Projection => projection;
        public Frustum Frustum => new Frustum(view * projection);
        Matrix4x4 previousView, previousProj;

        Vector2[] offsets = new Vector2[] {
            new Vector2(-0.8f, -0.266f),
            new Vector2(0.8f, 0.266f),
            new Vector2(-0.266f, 0.8f),
            new Vector2(0.266f, -0.8f),
        };
        int frame = 0;

        public Vector2 TemporalOffset => offsets[frame % offsets.Length];

        public ScenePassManager(Scene scene) {
            Scene = scene;
        }

        public void AddInstance(CSInstance instance, Mesh mesh) {
            AddInstance(instance, mesh, null, RenderTags.Default);
        }
        public void AddInstance(CSInstance instance, Mesh mesh, Material? material, RenderTags tags) {
            foreach (var pass in scenePasses) {
                if (!pass.TagsToInclude.HasAny(tags)) continue;
                if (pass.TagsToExclude.HasAny(tags)) continue;
                using var materials = new PooledList<Material>();
                if (material != null) materials.Add(material);
                if (mesh.Material != null) materials.Add(mesh.Material);
                if (pass.OverrideMaterial != null) materials.Add(pass.OverrideMaterial);
                materials.Add(pass.RetainedRenderer.Scene.RootMaterial);
                pass.AddInstance(instance, mesh, materials);
            }
        }
        private void RemoveInstance(CSInstance instance) {
            foreach (var pass in scenePasses) {
                pass.RemoveInstance(instance);
            }
        }

        public void AddPass(ScenePass pass) {
            scenePasses.Add(pass);
        }

        public bool SetViewProjection(Matrix4x4 view, Matrix4x4 projection) {
            if (this.view == view && this.projection == projection) return false;
            this.view = view;
            this.projection = projection;
            return true;
        }

        public void BeginRender(Int2 viewportSize) {
            ++frame;
            var jitter = TemporalOffset;
            var jitteredProjection = projection;
            var jitteredPrevProj = previousProj;
            jitteredProjection.M31 += jitter.X / viewportSize.X;
            jitteredProjection.M32 += jitter.Y / viewportSize.Y;
            jitteredPrevProj.M31 += jitter.X / viewportSize.X;
            jitteredPrevProj.M32 += jitter.Y / viewportSize.Y;
            var previousVP = previousView * jitteredPrevProj;
            foreach (var pass in scenePasses) {
                if (pass.TagsToInclude.Has(pass.Scene.TagManager.RequireTag("MainPass"))) {
                    pass.OverrideMaterial.SetValue("PreviousViewProjection", previousVP);
                    pass.OverrideMaterial.SetValue("TemporalJitter", Input.GetKeyDown(KeyCode.Q) ? default : jitter * 0.5f);
                    pass.OverrideMaterial.SetValue("TemporalFrame", (float)(frame % 12));
                    pass.SetViewProjection(view, jitteredProjection);
                }
            }
            previousView = View;
            previousProj = Projection;
        }
        public void EndRender() {
            foreach (var instance in dynamicInstances) {
                Scene.RemoveInstance(instance);
                RemoveInstance(instance);
            }
            dynamicInstances.Clear();
        }
        public CSInstance DrawDynamicMesh(Mesh mesh, Matrix4x4 transform, Material material) {
            var instance = Scene.CreateInstance();
            dynamicInstances.Add(instance);
            Scene.SetTransform(instance, transform);
            AddInstance(instance, mesh, material, RenderTags.Default);
            return instance;
        }
    }
}
