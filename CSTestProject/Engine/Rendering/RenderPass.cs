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
using Weesals.ECS;
using Weesals.Engine.Jobs;
using Weesals.Engine.Profiling;
using Weesals.Geometry;
using Weesals.Utility;

namespace Weesals.Engine {
    /*
     * RenderPasses can have multiple inputs and outputs
     * Inputs can be write-only (transparent rendering on top of opaque pass)
     * Outputs can be RT or direct to screen
     */
    public struct TextureDesc : IEquatable<TextureDesc> {
        public Int2 Size = 0;
        public BufferFormat Format;
        public int MipCount = 1;
        public TextureDesc() { }
        public TextureDesc(Int2 size, BufferFormat fmt = BufferFormat.FORMAT_R8G8B8A8_UNORM, int mipCount = 1) {
            Size = size; Format = fmt; MipCount = mipCount;
        }
        public TextureDesc(CSRenderTarget rt) { Size = rt.Size; Format = rt.Format; MipCount = rt.MipCount; }

        public bool Equals(TextureDesc other)
            => Size == other.Size && Format == other.Format && MipCount == other.MipCount;
        public override string ToString() { return $"<{Size}:{Format}:{MipCount}>"; }
        public override int GetHashCode() => (Size, Format, MipCount).GetHashCode();
    }
    public interface ICustomOutputTextures {
        bool FillTextures(CSGraphics graphics, ref RenderGraph.CustomTexturesContext context);
    }
    public class RenderPass {

        private static ProfilerMarker ProfileMarker_Bind = new("Bind");
        private static ProfilerMarker ProfileMarker_DrawQuad = new("DrawQuad");

        protected static Material blitMaterial;
        protected static Mesh quadMesh;

        public struct PassInput {
            public readonly CSIdentifier Name;
            public readonly bool RequireAttachment;
            public readonly DefaultTexture DefaultTexture;

            public PassInput(CSIdentifier name, bool requireAttachment = true, DefaultTexture defaultTexture = DefaultTexture.None) {
                Name = name;
                RequireAttachment = requireAttachment;
                DefaultTexture = defaultTexture;
            }
            public override string ToString() { return Name.ToString(); }
        }
        public struct PassOutput {
            public enum Channels : byte { None = 0, Clear = 1, Data = 2, All = Clear | Data }
            public readonly CSIdentifier Name;
            public readonly int PassthroughInput;
            public readonly Channels WriteChannels;
            public TextureDesc TargetDesc { get; private set; } = new() { Size = -1, };
            public PassOutput(CSIdentifier name) : this(name, -1, Channels.All) { }
            public PassOutput(CSIdentifier name, int passthroughInput = -1, Channels writeChannels = Channels.Data) {
                Name = name; PassthroughInput = passthroughInput; WriteChannels = writeChannels;
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
                return Graph.GetPassData(PassId).RequireViewport();
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
        public readonly ProfilerMarker RenderMarker;
        protected PassInput[] Inputs { get; set; } = Array.Empty<PassInput>();
        // First item is always Depth
        protected PassOutput[] Outputs { get; set; } = Array.Empty<PassOutput>();

        //public CSRenderTarget RenderTarget { get; protected set; }
        public Material OverrideMaterial { get; protected set; }
        public Material DefaultMaterial { get; protected set; }

        private List<RenderPass> dependencies = new();

        public RenderPass(string name) {
            Name = name;
            RenderMarker = new ProfilerMarker("Pass_" + Name);
            OverrideMaterial = new();
            OverrideMaterial.SetValue("View", Matrix4x4.Identity);
            OverrideMaterial.SetValue("Projection", Matrix4x4.Identity);
        }

        public void SetDefaultMaterial(Material material) {
            DefaultMaterial = material;
        }

        public virtual void GetInputOutput(scoped ref IOContext context) {
            foreach (var input in Inputs) context.AddInput(input);
            foreach (var output in Outputs) context.AddOutput(output);
        }

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
            if (!RenderTarget.IsValid) {
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
            public bool IsValid => Texture.IsValid;
            public override string ToString() { return $"{Texture}@{Mip}.{Slice}"; }
        }
        public ref struct Context {
            public Target ResolvedDepth;
            public readonly Span<Target> ResolvedTargets;
            public RectI Viewport;
            public Context(Target depth, Span<Target> targets) { ResolvedDepth = depth; ResolvedTargets = targets; }
        }
        protected void BindRenderTargets(CSGraphics graphics, ref Context context) {
            using var marker = ProfileMarker_Bind.Auto();
            using var colorTargets = new PooledList<CSRenderTarget>();
            foreach (var item in context.ResolvedTargets) colorTargets.Add(item.Texture);
            graphics.SetRenderTargets(colorTargets, context.ResolvedDepth.Texture);
            if (context.Viewport.Width > 0) graphics.SetViewport(context.Viewport);
        }
        public virtual void PrepareRender(CSGraphics graphics) {
        }
        public virtual void Render(CSGraphics graphics, ref Context context) {
            BindRenderTargets(graphics, ref context);
            OverrideMaterial.SetValue(RootMaterial.iRes, (Vector2)context.Viewport.Size);
        }
        protected Material RequireBlitMaterial(BlendMode blend) {
            if (blitMaterial == null) {
                blitMaterial = new Material("./Assets/blit.hlsl");
                blitMaterial.SetDepthMode(DepthMode.MakeOff());
            }
            blitMaterial.SetBlendMode(blend);
            return blitMaterial;
        }
        unsafe protected void DrawQuad(CSGraphics graphics, CSBufferReference texture, Material material = null) {
            using var marker = ProfileMarker_DrawQuad.Auto();
            if (material == null) {
                material = RequireBlitMaterial(BlendMode.MakeOpaque());
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
                Span<uint> inds = stackalloc uint[] { 0, 1, 2, 1, 3, 2, };
                quadMesh.SetIndices(inds);
            }

            if (texture.IsValid) {
                material.SetValue("Texture", texture);
                material.SetValue("TextureSize", (Vector2)texture.GetTextureResolution());
            }
            CSBufferLayout* bindingsPtr = stackalloc CSBufferLayout[2] {
                quadMesh.IndexBuffer,
                quadMesh.VertexBuffer,
            };
            var bindings = new MemoryBlock<CSBufferLayout>(bindingsPtr, 2);
            var pso = MaterialEvaluator.ResolvePipeline(graphics, bindings, new Span<Material>(ref material));
            var resources = MaterialEvaluator.ResolveResources(graphics, pso, new Span<Material>(ref material));
            graphics.CommitResources(pso, resources);
            graphics.Draw(pso, bindings.AsCSSpan(), resources.AsCSSpan(), CSDrawConfig.Default);
        }
        public override string ToString() { return Name; }
    }
    public class ScenePass : RenderPass {
        private static ProfilerMarker ProfileMarker_Prepare = new("Prepare");
        private static ProfilerMarker ProfileMarker_Render = new("Render");
        private static ProfilerMarker ProfileMarker_CopyQueue = new("CopyQueue");

        public readonly RenderQueue RenderQueue;
        public readonly RetainedRenderer RetainedRenderer;
        public Scene Scene => RetainedRenderer.Scene;
        public Matrix4x4 View { get; protected set; } = Matrix4x4.Identity;
        public Matrix4x4 Projection { get; protected set; } = Matrix4x4.Identity;
        public Frustum Frustum { get; protected set; }

        public RenderTags TagsToInclude = RenderTags.Default;
        public RenderTags TagsToExclude = RenderTags.None;

        public Action<CSGraphics> OnPrepare;
        public Action<CSGraphics> OnRender;

        public bool Enabled => View.M44 != 0f;

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

        public void AddInstance(SceneInstance instance, Mesh mesh, Span<Material> materials) {
            RetainedRenderer.AppendInstance(mesh, materials, instance);
        }
        public void SetVisible(SceneInstance instance, bool visible) {
            RetainedRenderer.SetVisible(instance, visible);
        }
        public void RemoveInstance(SceneInstance instance) {
            RetainedRenderer.RemoveInstance(instance);
        }

        public override void PrepareRender(CSGraphics graphics) {
            base.PrepareRender(graphics);
            using (var marker = ProfileMarker_Prepare.Auto()) {
                OnPrepare?.Invoke(graphics);
            }
        }
        public override void Render(CSGraphics graphics, ref Context context) {
            using var scopedGraphics = new Graphics.Scoped(graphics, Scene.RootMaterial, RenderQueue);

            base.Render(graphics, ref context);
            RenderQueue.Clear();
            using (var marker = ProfileMarker_Render.Auto()) {
                OnRender?.Invoke(graphics);
            }
            RenderScene(graphics, ref context);
        }
        public virtual void RenderScene(CSGraphics graphics, ref Context context) {
            if (Enabled) {
                RetainedRenderer.SubmitToRenderQueue(graphics, RenderQueue, Frustum);
                if (RenderQueue.UsedTextures.Count > 0) {
                    var usedTextures = RenderQueue.UsedTextures;
                    RenderQueue.UsedTextures = new();
                    var copyQueue = Core.ActiveInstance.CreateGraphics();
                    JobHandle.Schedule(() => {
                        using var marker = ProfileMarker_CopyQueue.Auto();
                        copyQueue.Reset();
                        foreach (var tex in usedTextures) {
                            copyQueue.CommitTexture(tex);
                        }
                        copyQueue.Execute();
                        copyQueue.Dispose();
                    });
                }
                Scene.SubmitToGPU(graphics);
                RenderQueue.Render(graphics);
            }
        }

        public bool GetHasSceneChanges() {
            return RetainedRenderer.GetHasSceneChanges();
        }

        public void MoveInstance(SceneInstance instance, Int2 oldPos, Int2 newPos) {
            RetainedRenderer.MoveInstance(instance, oldPos, newPos);
        }

        public void SetMeshLOD(Mesh mesh, Mesh hull, Span<Material> hullMaterials) {
            RetainedRenderer.SetMeshLOD(mesh, hull, hullMaterials);
        }
    }

    public class ShadowPass : ScenePass, ICustomOutputTextures {
        public Action OnPostRender;
        public Vector3 LightDirection = new Vector3(-4f, -5f, 7f);

        public CSRenderTarget shadowBuffer;

        private ConvexHull activeArea = new();
        public Material ShadowReceiverMaterial;

        public float ShadowPerspective = 0.5f;

        public ShadowPass(Scene scene) : base(scene, "Shadows") {
            Outputs = new[] { new PassOutput("ShadowMap").SetTargetDesc(new TextureDesc() { Size = 512, Format = BufferFormat.FORMAT_D16_UNORM, }) };
            OverrideMaterial.SetRenderPassOverride("ShadowCast");
            TagsToInclude.Add(RenderTag.ShadowCast);
            ShadowReceiverMaterial = new();

            shadowBuffer = CSRenderTarget.Create("Shadows");
            shadowBuffer.SetSize(512);
            shadowBuffer.SetFormat(BufferFormat.FORMAT_D16_UNORM);
        }
        public bool FillTextures(CSGraphics graphics, ref RenderGraph.CustomTexturesContext context) {
            context.OverwriteOutput(context.Outputs[0], shadowBuffer);
            return true;
        }
        public bool UpdateShadowFrustum(Frustum frustum, BoundingBox relevantArea = default) {
            // Create shadow projection based on frustum near/far corners

            // Get bounding hull corners
            if (relevantArea.Min.Y < relevantArea.Max.Y) {
                activeArea.FromFrustum(frustum);
                activeArea.Slice(relevantArea);
            }
            Span<Vector3> activeCorners = stackalloc Vector3[relevantArea.Min.Y < relevantArea.Max.Y ? activeArea.CornerCount : 8];
            if (relevantArea.Min.Y < relevantArea.Max.Y) {
                activeArea.GetCorners(activeCorners);
            } else {
                frustum.GetCorners(activeCorners);
            }

            // Compute a light view matrix
            var cameraVP = frustum.CalculateViewProj();
            Matrix4x4.Invert(cameraVP, out var cameraVPInv);
            var cameraPos4 = Vector4.Transform(new Vector4(0f, 0f, -1f, 0f), cameraVPInv);
            var cameraPos = cameraPos4.toxyz() / cameraPos4.W;
            var cameraFwd = Vector3.Normalize(frustum.Forward);
            var lightDir = Vector3.Normalize(LightDirection);

            var lightViewMatrix = Matrix4x4.CreateLookAt(cameraPos, cameraPos + lightDir, cameraFwd);
            var lightProjMatrix = Matrix4x4.Identity;

            // Disable shadows
            if (activeCorners.Length <= 0) {
                lightViewMatrix.M44 = 0f;
                return SetViewProjection(lightViewMatrix, Matrix4x4.Identity);
            }

            if (!Input.GetKeyDown(KeyCode.P)) {
                float PerspScale = MathF.Pow(ShadowPerspective, 5f) * (1f - MathF.Abs(Vector3.Dot(cameraFwd, lightDir)));
                float OrthoBias = 1f - PerspScale;

                // Find the hull bounds in projection space
                Vector3 projMin = new Vector3(float.MaxValue), projMax = new Vector3(float.MinValue);
                foreach (ref var corner in activeCorners) {
                    var lightCorner = Vector3.Transform(corner, lightViewMatrix);
                    var div = lightCorner.Y * PerspScale - OrthoBias;
                    var projCorner = new Vector3(lightCorner.X / div, lightCorner.Y, lightCorner.Z / div);
                    projMin = Vector3.Min(projMin, projCorner);
                    projMax = Vector3.Max(projMax, projCorner);
                }
                projMin.Z += projMin.Z - projMax.Z; // 0-1 range remap
                projMin.Z -= 50f * OrthoBias;       // Casters bias

                // Construct optimized projection matrix
                var po = (projMax + projMin) / 2f;
                var ps = (projMax - projMin) / 2f;
                var n = projMax.Y;
                var f = projMin.Y;
                var zScale = ((f + n) * PerspScale - 2 * OrthoBias) / (f - n);
                lightProjMatrix = new Matrix4x4(
                    -1f / ps.X, 0f, 0f, 0f,

                    po.X / ps.X * PerspScale,
                    zScale,
                    po.Z / ps.Z * PerspScale,
                    -PerspScale,

                    0f, 0f, -1f / ps.Z, 0f,

                    -po.X / ps.X * OrthoBias,
                    f * (PerspScale - zScale) - OrthoBias,
                    -po.Z / ps.Z * OrthoBias,
                    OrthoBias
                );
                //var nearCS = Vector4.Transform(new Vector4(0f, projMax.Y, 0f, 1f), lightProj2);
                //var farCS = Vector4.Transform(new Vector4(0f, projMin.Y, 0f, 1f), lightProj2);
                //nearCS /= nearCS.W;
                //farCS /= farCS.W;

                return SetViewProjection(
                    lightViewMatrix,
                    lightProjMatrix
                );
            }

            // Find hull bounds in view space
            var lightMin = new Vector3(float.MaxValue);
            var lightMax = new Vector3(float.MinValue);
            foreach (ref var corner in activeCorners) {
                corner = Vector3.Transform(corner, lightViewMatrix);
                lightMin = Vector3.Min(lightMin, corner);
                lightMax = Vector3.Max(lightMax, corner);
            }

            // Max is actually min
            lightMax.Z += 20.0f;

            var lightSize = Vector3.Max(lightMax - lightMin, Vector3.One * 10.0f);
            lightViewMatrix.Translation = lightViewMatrix.Translation - (lightMin + lightMax) / 2.0f;
            lightProjMatrix = Matrix4x4.CreateOrthographic(-lightSize.X, lightSize.Y, -lightSize.Z / 2.0f, lightSize.Z / 2.0f);

            return SetViewProjection(
                lightViewMatrix,
                lightProjMatrix
            );
        }
        public override void RenderScene(CSGraphics graphics, ref Context context) {
            graphics.Clear();
            base.RenderScene(graphics, ref context);
            OnPostRender?.Invoke();
        }
        public void DrawVolume() {
            activeArea.DrawGizmos();
        }
        public void ApplyParameters(Matrix4x4 view, Material basePassMat, float sunIntensity = 6.0f) {
            bool noShadows = !this.Enabled;
            var shadowView = this.View;
            shadowView.M44 = 1.0f;
            shadowView.M43 += 0.1f;     // Shadow bias
            var shadowPassViewProj = noShadows ? Matrix4x4.Identity : shadowView * this.Projection;
            //var shadowPassViewProj = noShadows ? Matrix4x4.Identity : OtherView * OtherProjection;
            Matrix4x4.Invert(shadowView, out var shadowPassInvView);
            Matrix4x4.Invert(view, out var basePassInvView);
            basePassMat.SetValue("ShadowViewProjection", shadowPassViewProj);
            basePassMat.SetValue("ShadowIVViewProjection", basePassInvView * shadowPassViewProj);
            basePassMat.SetValue("_WorldSpaceLightDir0", Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, shadowPassInvView)));
            basePassMat.SetValue("_LightColor0", new Vector3(1.0f, 0.8f, 0.6f) * sunIntensity);
        }
    }
    public class MainPass : ScenePass {
        public MainPass(Scene scene, string name) : base(scene, name) {
        }
        public void UpdateShadowParameters(ShadowPass shadowPass) {
            shadowPass.ApplyParameters(View, OverrideMaterial);
        }
    }
    public class ClearPass : RenderPass {
        public ClearPass() : base("Clear") {
            Outputs = new[] {
                new PassOutput("SceneDepth").SetTargetDesc(new TextureDesc() { Size = -1, Format = BufferFormat.FORMAT_D24_UNORM_S8_UINT, }),
                new PassOutput("SceneColor").SetTargetDesc(new TextureDesc() { Size = -1, Format = BufferFormat.FORMAT_R8G8B8A8_UNORM, }),
                new PassOutput("SceneVelId").SetTargetDesc(new TextureDesc() { Size = -1, Format = BufferFormat.FORMAT_R8G8B8A8_SNORM, }),
                new PassOutput("SceneAttri").SetTargetDesc(new TextureDesc() { Size = -1, Format = BufferFormat.FORMAT_R8G8B8A8_UNORM, }),
            };
        }
        public override void Render(CSGraphics graphics, ref Context context) {
            base.Render(graphics, ref context);
            graphics.SetViewport(context.Viewport);
            graphics.Clear(new(Vector4.Zero, 1f) {
                ClearStencil = 0x0
            });
        }
    }
    public class BasePass : MainPass {
        public BasePass(Scene scene) : base(scene, "BasePass") {
            Inputs = new[] {
                new PassInput("SceneDepth", false),
                new PassInput("SceneColor", false),
                new PassInput("SceneVelId", false),
                new PassInput("SceneAttri", false),
                new PassInput("ShadowMap"),
            };
            Outputs = new[] {
                new PassOutput("SceneDepth", 0),
                new PassOutput("SceneColor", 1),
                new PassOutput("SceneVelId", 2),
                new PassOutput("SceneAttri", 3),
            };
            TagsToInclude.Add(scene.TagManager.RequireTag("MainPass"));
            TagsToInclude.Add(scene.TagManager.RequireTag("Terrain"));
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
                new PassOutput("SceneDepth", 0),
                new PassOutput("SceneColor", 1),
            };
            TagsToInclude.Clear();
            TagsToInclude.Add(scene.TagManager.RequireTag("Transparent"));
            TagsToInclude.Add(scene.TagManager.RequireTag("MainPass"));
            TagsToInclude.Add(scene.TagManager.RequireTag("Terrain"));
            GetPassMaterial().SetBlendMode(BlendMode.MakeAlphaBlend());
            GetPassMaterial().SetDepthMode(DepthMode.MakeReadOnly());
        }
    }
    public class DeferredPass : RenderPass {
        public ScenePassManager ScenePasses;
        Material deferredMaterial;
        public DeferredPass(ScenePassManager scene) : base("DeferredLit") {
            ScenePasses = scene;
            Inputs = new[] {
                new PassInput("SceneDepth"),
                new PassInput("SceneColor"),
                new PassInput("SceneAttri"),
                new PassInput("SceneAO", defaultTexture: DefaultTexture.White),
                new PassInput("ShadowMap"),
            };
            Outputs = new[] {
                new PassOutput("SceneDepth", 0),
                new PassOutput("SceneColor", writeChannels: PassOutput.Channels.Data).SetTargetDesc(new TextureDesc() { Size = -1, Format = BufferFormat.FORMAT_R10G10B10A2_UNORM, MipCount = 1, }),
            };
            deferredMaterial = new Material("./Assets/deferred.hlsl", GetPassMaterial());
            deferredMaterial.SetBlendMode(BlendMode.MakeOpaque());
            deferredMaterial.SetDepthMode(DepthMode.MakeReadOnly());
        }
        public override void Render(CSGraphics graphics, ref Context context) {
            base.Render(graphics, ref context);
            var proj = ScenePasses.JitteredProjection;
            float near = Math.Abs(proj.M43 / proj.M33);
            float far = Math.Abs(proj.M43 / (proj.M33 - 1));
            Vector2 ZBufferParams = new(1.0f / far - 1.0f / near, 1.0f / near);
            deferredMaterial.SetValue("ZBufferParams", ZBufferParams);
            deferredMaterial.SetValue("ViewToProj", new Vector4(
                +2.0f / proj.M11,
                +2.0f / proj.M22,
                -(1.0f + proj.M31) / proj.M11,
                -(1.0f + proj.M32) / proj.M22
            ));
            deferredMaterial.SetValue("View", ScenePasses.View);
            deferredMaterial.SetValue("Projection", proj);
            DrawQuad(graphics, default, deferredMaterial);
        }
        public void SetAOEnabled(bool enable) {
            deferredMaterial.SetMacro("ENABLEAO", enable ? "1" : null!);
        }
        public void SetFogEnabled(bool enable) {
            deferredMaterial.SetMacro("ENABLEFOG", enable ? "1" : null!);
        }
        public void UpdateShadowParameters(ShadowPass shadowPass) {
            shadowPass.ApplyParameters(ScenePasses.View, deferredMaterial);
        }
    }
    public class SkyboxPass : RenderPass {
        public ScenePassManager ScenePasses;
        protected Material skyboxMat;
        public SkyboxPass(ScenePassManager scene) : base("Skybox") {
            ScenePasses = scene;
            Inputs = new[] {
                new PassInput("SceneDepth", false),
                new PassInput("SceneColor", false),
            };
            Outputs = new[] {
                new PassOutput("SceneDepth", 0),
                new PassOutput("SceneColor", 1),
            };
            skyboxMat = new Material("./Assets/skybox.hlsl", GetPassMaterial());
            skyboxMat.SetDepthMode(DepthMode.MakeReadOnly());
            skyboxMat.SetBlendMode(BlendMode.MakeAlphaBlend());
        }
        public void UpdateShadowParameters(ShadowPass shadowPass) {
            shadowPass.ApplyParameters(Matrix4x4.Identity, OverrideMaterial);
        }
        unsafe public override void Render(CSGraphics graphics, ref Context context) {
            base.Render(graphics, ref context);
            skyboxMat.SetValue("View", ScenePasses.View);
            skyboxMat.SetValue("Projection", ScenePasses.Projection);
            DrawQuad(graphics, default, skyboxMat);
        }
    }
    public class VolumetricGatherPass : RenderPass {
        public static CSIdentifier VolumetricPassName = "Volumetric";
        public ScenePassManager ScenePasses;
        public ParticleSystemManager ParticleSystem;
        public VolumetricGatherPass(ScenePassManager scene) : base("VolumetricGather") {
            ScenePasses = scene;
            Inputs = new[] {
                new PassInput("SceneDepth", false),
                new PassInput("ShadowMap"),
            };
            Outputs = new[] {
                new PassOutput("SceneDepth", 0),
                new PassOutput("VolAlbedo").SetTargetDesc(new() { Format = BufferFormat.FORMAT_R8G8B8A8_UNORM, }),
                new PassOutput("VolAttr").SetTargetDesc(new() { Format = BufferFormat.FORMAT_R11G11B10_FLOAT, }),
            };
            OverrideMaterial.SetBlendMode(BlendMode.MakeAlphaBlend());
            OverrideMaterial.SetDepthMode(DepthMode.MakeOff());
        }
        public void SetParticleSystem(ParticleSystemManager particleSystem) {
            ParticleSystem = particleSystem;
        }
        public override void Render(CSGraphics graphics, ref Context context) {
            base.Render(graphics, ref context);
            CSClearConfig clear = new(new Vector4(0f, 0f, 0f, 0f));
            graphics.Clear(clear);
            OverrideMaterial.SetValue("View", ScenePasses.View);
            OverrideMaterial.SetValue("Projection", ScenePasses.Projection);
            var tag = ScenePasses.Scene.TagManager.RequireTag(VolumetricPassName);
            ParticleSystem.Draw(graphics, tag);
        }
    }
    public class VolumetricFogPass : RenderPass {
        public ScenePassManager ScenePasses;
        Material fogMaterial;
        public VolumetricFogPass(ScenePassManager scene) : base("VolumetricFog") {
            ScenePasses = scene;
            Inputs = new[] {
                new PassInput("SceneDepth", false),
                new PassInput("SceneColor", false),
                new PassInput("ShadowMap"),
                //new PassInput("VolAlbedo"),
                //new PassInput("VolAttr"),
            };
            Outputs = new[] {
                new PassOutput("SceneColor", 1),
            };
            fogMaterial = new Material("./Assets/volumetricfog.hlsl", GetPassMaterial());
            fogMaterial.SetBlendMode(BlendMode.MakePremultiplied());
            fogMaterial.SetDepthMode(DepthMode.MakeOff());
            var volumeTexGenerator = new NoiseTexture3D();
            var volumeTex = volumeTexGenerator.Require();
            OverrideMaterial.SetTexture("FogDensity", volumeTex);
            var noiseTexGenerator = new NoiseTexture2D();
            var noiseTex = noiseTexGenerator.Require();
            OverrideMaterial.SetTexture("Noise2D", noiseTex);
        }
        public override void Render(CSGraphics graphics, ref Context context) {
            base.Render(graphics, ref context);
            fogMaterial.SetValue("View", ScenePasses.View);
            fogMaterial.SetValue("Projection", ScenePasses.Projection);
            var proj = ScenePasses.Projection;
            float near = Math.Abs(proj.M43 / proj.M33);
            float far = Math.Abs(proj.M43 / (proj.M33 - 1));
            Vector2 ZBufferParams = new(1.0f / far - 1.0f / near, 1.0f / near);
            fogMaterial.SetValue("ZBufferParams", ZBufferParams);
            DrawQuad(graphics, default, fogMaterial);
        }

        public void UpdateShadowParameters(ShadowPass shadowPass) {
            shadowPass.ApplyParameters(Matrix4x4.Identity, OverrideMaterial);
        }
    }
    public class GTAOPass : RenderPass {
        public ScenePassManager ScenePasses;
        Material gtaoMaterial;
        public GTAOPass(ScenePassManager scene) : base("GTAO") {
            ScenePasses = scene;
            Inputs = new[] {
                new PassInput("SceneDepth", true),
                new PassInput("SceneAttri", true),
                new PassInput("HighZ", true),
            };
            Outputs = new[] {
                new PassOutput("SceneDepth", 0),
                new PassOutput("SceneAO"),
            };
            gtaoMaterial = new Material("./Assets/Shader/GTAO.hlsl", GetPassMaterial());
            gtaoMaterial.SetBlendMode(BlendMode.MakeOpaque());
            gtaoMaterial.SetDepthMode(new DepthMode(DepthMode.Comparisons.Greater, false));
        }
        public override void Render(CSGraphics graphics, ref Context context) {
            base.Render(graphics, ref context);
            var proj = ScenePasses.Projection;
            Matrix4x4.Invert(proj, out var invProj);
            var res = (Vector2)context.Viewport.Size;
            gtaoMaterial.SetValue(RootMaterial.iRes, res);
            gtaoMaterial.SetValue("View", ScenePasses.View);
            gtaoMaterial.SetValue("InvProjection", invProj);
            gtaoMaterial.SetValue("TexelSize", new Vector2(1.0f) / (Vector2)context.Viewport.Size);
            gtaoMaterial.SetValue("ViewToProj", new Vector4(
                +2.0f / proj.M11,
                +2.0f / proj.M22,
                -1.0f / proj.M11,
                -1.0f / proj.M22
            ));
            float near = Math.Abs(proj.M43 / proj.M33);
            float far = Math.Abs(proj.M43 / (proj.M33 - 1));
            Vector2 ZBufferParams = new(1.0f / far - 1.0f / near, 1.0f / near);
            gtaoMaterial.SetValue("ZBufferParams", ZBufferParams);
            gtaoMaterial.SetValue("HalfProjScale", (res.Y / proj.M22) * 4.0f);
            //gtaoMaterial.SetValue("NearFar", ScenePasses.)
            DrawQuad(graphics, default, gtaoMaterial);
        }
    }
    public class TemporalJitter : RenderPass, ICustomOutputTextures {
        public ScenePassManager ScenePasses;
        public Action<Int2>? OnBegin;
        public DelegatePass.RenderPassEvaluator? OnRender;
        private Material temporalMaterial;
        CSRenderTarget[] targets = new CSRenderTarget[2];
        int frame = 0;
        private Matrix4x4 previousViewProj;
        Vector2[] offsets = new Vector2[] { new Vector2(-0.8f, -0.266f), new Vector2(0.8f, 0.266f), new Vector2(-0.266f, 0.8f), new Vector2(0.266f, -0.8f), };
        public Vector2 TemporalOffset => offsets[frame % offsets.Length];
        public Material TemporalMaterial => temporalMaterial;
        public TemporalJitter(string name) : base(name) {
            Inputs = new[] { new RenderPass.PassInput("SceneDepth"), new RenderPass.PassInput("SceneColor", true), new RenderPass.PassInput("SceneVelId", true), };
            Outputs = new[] { new RenderPass.PassOutput("SceneColor", writeChannels: PassOutput.Channels.Data).SetTargetDesc(new TextureDesc() { Size = -1, }), };
            temporalMaterial = new Material("./Assets/temporalpass.hlsl", GetPassMaterial());
            temporalMaterial.SetDepthMode(DepthMode.MakeReadOnly());
        }
        public bool FillTextures(CSGraphics graphics, ref RenderGraph.CustomTexturesContext context) {
            ++frame;
            int targetId = frame % 2;
            if (targets[targetId].IsValid && targets[targetId].GetSize() != context.Viewport.Size) {
                for (int i = 0; i < targets.Length; i++) if (targets[i].IsValid) targets[i].Dispose();
            }
            if (!targets[targetId].IsValid) {
                targets[targetId] = CSRenderTarget.Create("Temporal " + targetId);
                targets[targetId].SetSize(context.Viewport.Size);
                targets[targetId].SetFormat(BufferFormat.FORMAT_R16G16B16A16_FLOAT);
            }
            context.OverwriteOutput(context.Outputs[0], targets[targetId]);
            ScenePasses.SetupRender(context.Viewport.Size, TemporalOffset, frame % 12);
            OnBegin?.Invoke(context.Viewport.Size);
            return true;
        }
        public override void Render(CSGraphics graphics, ref Context context) {
            base.Render(graphics, ref context);
            var viewProj = ScenePasses.View * ScenePasses.Projection;
            Matrix4x4.Invert(previousViewProj, out var invPrevVP);
            Matrix4x4.Invert(viewProj, out var invVP);
            //temporalMaterial.SetValue("PreviousVP", previousViewProj);
            //temporalMaterial.SetValue("CurrentVP", invVP);
            temporalMaterial.SetValue("CurToPrevVP", invVP * previousViewProj);
            Matrix4x4.Invert(invVP * previousViewProj, out var invCTP);
            temporalMaterial.SetValue("PrevToCurVP", invCTP);
            temporalMaterial.SetValue("TemporalJitter", TemporalOffset * 0.5f);
            temporalMaterial.SetValue("TemporalFrame", (float)frame);
            OnRender?.Invoke(graphics, ref context);
            var sceneColor = GetPassMaterial().GetUniformRenderTarget("SceneColor");
            var prevTargetId = (frame - 1) % 2;
            if (targets[prevTargetId].IsValid) {
                temporalMaterial.SetTexture("CurrentFrame", sceneColor);
                temporalMaterial.SetTexture("PreviousFrame", targets[prevTargetId]);
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
                new PassOutput("HighZ").SetTargetDesc(new TextureDesc() { Size = -2, Format = BufferFormat.FORMAT_R16G16_UNORM, MipCount = 0, }),
            };
            firstPassMaterial = new Material("./Assets/highz.hlsl", GetPassMaterial());
            firstPassMaterial.SetPixelShader(Resources.LoadShader("./Assets/highz.hlsl", "FirstPassPS"));
            firstPassMaterial.SetBlendMode(BlendMode.MakeOpaque());
            //firstPassMaterial.SetDepthMode(new DepthMode(DepthMode.Comparisons.Always, true));
            firstPassMaterial.SetDepthMode(DepthMode.MakeReadOnly());
            highZMaterial = new Material(firstPassMaterial);
            highZMaterial.SetPixelShader(Resources.LoadShader("./Assets/highz.hlsl", "HighZPassPS"));
        }
        public bool FillTextures(CSGraphics graphics, ref RenderGraph.CustomTexturesContext context) {
            var inputSize = context.FindInputSize(0);
            if (inputSize.X <= 0) return false;
            var outputDesc = context.Outputs[0].Output.TargetDesc;
            outputDesc.Size = inputSize >> 1;
            context.Outputs[0].Output.SetTargetDesc(outputDesc);
            return true;
        }
        unsafe public override void Render(CSGraphics graphics, ref Context context) {
            var sceneDepth = GetPassMaterial().GetUniformRenderTarget("SceneDepth");
            var highZ = context.ResolvedTargets[0];
            var size = highZ.Texture.GetSize();
            for (int m = 0; m < highZ.Texture.GetMipCount(); ++m) {
                bool isOdd = ((size.X | size.Y) & 0x01) != 0;
                var mipSize = Int2.Max(size >> m, Int2.One);
                graphics.SetRenderTargets(new CSRenderTargetBinding(highZ.Texture.mRenderTarget, m, 0), default(CSRenderTargetBinding));
                var material = m == 0 ? firstPassMaterial : highZMaterial;
                graphics.SetViewport(new RectI(Int2.Zero, mipSize));
                //material.SetMacro("ODD", isOdd ? "1" : "0");
                DrawQuad(graphics, m == 0 ? sceneDepth : new CSBufferReference(highZ.Texture) {
                    mSubresourceId = (short)(m - 1),
                    mSubresourceCount = 1,
                }, material);
            }
        }
    }
    public class AmbientOcclusionPass : RenderPass {
        protected Material aoPass;
        public AmbientOcclusionPass() : base("Ambient Occlusion") {
            Inputs = new[] {
                new PassInput("SceneDepth", true),
                new PassInput("SceneColor", false),
                new PassInput("HighZ", true),
            };
            Outputs = new[] {
                new PassOutput("SceneColor", 1),
            };
            aoPass = new Material("./Assets/ambientocclusion.hlsl", GetPassMaterial());
            aoPass.SetDepthMode(DepthMode.MakeReadOnly());
            aoPass.SetBlendMode(BlendMode.MakeAlphaBlend());
        }
        unsafe public override void Render(CSGraphics graphics, ref Context context) {
            context.ResolvedDepth = default;
            base.Render(graphics, ref context);
            DrawQuad(graphics, default, aoPass);
        }
    }
    public class BloomPass : RenderPass {
        protected Material thresholdMat;
        protected Material downsampleMat;
        protected Material upsampleMat;
        public BloomPass() : base("Bloom") {
            Inputs = new[] {
                new PassInput("SceneColor"),
            };
            Outputs = new[] {
                new PassOutput("BloomChain").SetTargetDesc(new TextureDesc() { Size = -1, Format = BufferFormat.FORMAT_R11G11B10_FLOAT, MipCount = 8, }),
            };
            var path = "./Assets/bloomchain.hlsl";
            thresholdMat = new(Resources.LoadShader(path, "VSMain"), Resources.LoadShader(path, "PSThreshold"), OverrideMaterial);
            downsampleMat = new(Resources.LoadShader(path, "VSMain"), Resources.LoadShader(path, "PSDownsample"), OverrideMaterial);
            upsampleMat = new(Resources.LoadShader(path, "VSMain"), Resources.LoadShader(path, "PSUpsample"), OverrideMaterial);
            OverrideMaterial.SetBlendMode(BlendMode.MakeOpaque());
            OverrideMaterial.SetDepthMode(DepthMode.MakeOff());
            upsampleMat.SetBlendMode(BlendMode.MakePremultiplied());
        }
        unsafe public override void Render(CSGraphics graphics, ref Context context) {
            var sceneColor = GetPassMaterial().GetUniformRenderTarget("SceneColor");
            var bloomChain = context.ResolvedTargets[0];
            graphics.SetRenderTargets(new CSRenderTargetBinding(bloomChain.Texture.mRenderTarget, 0, 0), default);
            DrawQuad(graphics, sceneColor, thresholdMat);
            var size = bloomChain.Texture.GetSize();
            for (int m = 1; m < bloomChain.Texture.GetMipCount(); ++m) {
                var mipSize = Int2.Max(size >> m, Int2.One);
                graphics.SetRenderTargets(new CSRenderTargetBinding(bloomChain.Texture.mRenderTarget, m, 0), default);
                graphics.SetViewport(new RectI(Int2.Zero, mipSize));
                downsampleMat.SetValue("TextureMipTexel", Vector2.One / (Vector2)Int2.Max(size >> (m - 1), Int2.One));
                DrawQuad(graphics, new CSBufferReference(bloomChain.Texture) {
                    mSubresourceId = (short)(m - 1),
                    mSubresourceCount = 1,
                }, downsampleMat);
            }
            // The final sample is handled by postprocessing.hlsl
            for (int m = bloomChain.Texture.GetMipCount() - 2; m >= 1; --m) {
                var mipSize = Int2.Max(size >> m, Int2.One);
                graphics.SetRenderTargets(new CSRenderTargetBinding(bloomChain.Texture.mRenderTarget, m, 0), default);
                graphics.SetViewport(new RectI(Int2.Zero, mipSize));
                upsampleMat.SetValue("TextureMipTexel", Vector2.One / (Vector2)Int2.Max(size >> (m + 1), Int2.One));
                DrawQuad(graphics, new CSBufferReference(bloomChain.Texture) {
                    mSubresourceId = (short)(m + 1),
                    mSubresourceCount = 1,
                }, upsampleMat);
            }
        }
    }
    public class PostProcessPass : RenderPass {
        protected Material postMaterial;
        public PostProcessPass() : base("PostProcess") {
            Inputs = new[] {
                new PassInput("SceneColor"),
                //new PassInput("SceneColorInput"),
                new PassInput("BloomChain", defaultTexture: DefaultTexture.Black),
            };
            Outputs = new[] {
                new PassOutput("SceneColor", writeChannels: PassOutput.Channels.Data).SetTargetDesc(new TextureDesc() { Size = -1, }),
            };
            postMaterial = new Material("./Assets/postprocess.hlsl", OverrideMaterial);
            postMaterial.SetBlendMode(BlendMode.MakePremultiplied());
            postMaterial.SetDepthMode(DepthMode.MakeOff());
        }
        public void SetBloomEnabled(bool enableBloom) {
            postMaterial.SetMacro("ENABLEBLOOM", enableBloom ? "1" : CSIdentifier.Invalid);
        }
        public override void Render(CSGraphics graphics, ref Context context) {
            base.Render(graphics, ref context);
            graphics.Clear(new(Color.Black));
            DrawQuad(graphics, default, postMaterial);
        }
    }
    public class PresentPass : RenderPass {
        public PresentPass() : base("Present") { }
        public override void Render(CSGraphics graphics, ref Context context) {
            base.Render(graphics, ref context);
        }
    }

    public class DelegatePass : RenderPass {
        public delegate void RenderPassEvaluator(CSGraphics graphics, ref RenderPass.Context context);
        public RenderPassEvaluator OnRender;
        public DelegatePass(string name, PassInput[]? inputs, PassOutput[]? outputs, RenderPassEvaluator onRender) : base(name) {
            if (inputs != null) Inputs = inputs;
            if (outputs != null) Outputs = outputs;
            OnRender = onRender;
        }
        public override void Render(CSGraphics graphics, ref Context context) {
            base.Render(graphics, ref context);
            OnRender(graphics, ref context);
        }
    }
    public class FinalPass : RenderPass, ICustomOutputTextures {
        public delegate bool RenderPassEvaluator(CSGraphics graphics, ref RenderGraph.CustomTexturesContext context);
        public RenderPassEvaluator OnFillTextures;
        public FinalPass(string name, PassInput[]? inputs, PassOutput[]? outputs, RenderPassEvaluator onFillTextures) : base(name) {
            if (inputs != null) Inputs = inputs;
            if (outputs != null) Outputs = outputs;
            OnFillTextures = onFillTextures;
        }
        public bool FillTextures(CSGraphics graphics, ref RenderGraph.CustomTexturesContext context) {
            return OnFillTextures(graphics, ref context);
        }
    }

    public class ScenePassManager {
        public readonly Scene Scene;
        private Matrix4x4 view, projection;
        private List<ScenePass> scenePasses = new();
        private List<SceneInstance> dynamicInstances = new();
        private uint[] instancePasses = Array.Empty<uint>();
        private int dynamicDrawHash = 0;
        private Material mainSceneMaterial;

        public Matrix4x4 View => view;
        public Matrix4x4 Projection => projection;
        public Frustum Frustum => new Frustum(view * projection);
        Matrix4x4 previousView, previousProj;
        public Matrix4x4 JitteredProjection { get; private set; }

        public IReadOnlyList<ScenePass> ScenePasses => scenePasses;
        public Material MainSceneMaterial => mainSceneMaterial;

        public ScenePassManager(Scene scene) {
            Scene = scene;
            mainSceneMaterial = new();
            //mainSceneMaterial.InheritProperties(Scene.RootMaterial);
        }

        public void AddInstance(SceneInstance instance, Mesh mesh) {
            AddInstance(instance, mesh, null, RenderTags.Default);
        }
        public void AddInstance(SceneInstance instance, Mesh mesh, Material? material, RenderTags tags) {
            if (instance >= instancePasses.Length) {
                Array.Resize(ref instancePasses, (int)BitOperations.RoundUpToPowerOf2((uint)instance.GetInstanceId() + 16));
            }
            for (int p = 0; p < scenePasses.Count; p++) {
                var pass = scenePasses[p];
                if (!pass.TagsToInclude.HasAny(tags)) continue;
                if (pass.TagsToExclude.HasAny(tags)) continue;
                using var materials = new PooledList<Material>();
                if (material != null) materials.Add(material);
                if (mesh.Material != null) materials.Add(mesh.Material);
                if (pass.OverrideMaterial != null) materials.Add(pass.OverrideMaterial);
                materials.Add(pass.RetainedRenderer.Scene.RootMaterial);
                pass.AddInstance(instance, mesh, materials);
                instancePasses[instance] |= 1u << p;
            }
        }
        public void RemoveInstance(SceneInstance instance) {
            foreach (var bit in new BitEnumerator(instancePasses[instance])) {
                var pass = scenePasses[bit];
                pass.RemoveInstance(instance);
            }
            instancePasses[instance] = default;
        }

        public void AddPass(ScenePass pass) {
            scenePasses.Add(pass);
            pass.OverrideMaterial.InheritProperties(mainSceneMaterial);
        }

        public bool SetViewProjection(Matrix4x4 view, Matrix4x4 projection) {
            if (this.view == view && this.projection == projection) return false;
            this.view = view;
            this.projection = projection;
            mainSceneMaterial.SetValue(RootMaterial.iVMat, View);
            return true;
        }

        public int GetRenderHash() {
            return /*Scene.GetGPURevision() + */dynamicDrawHash;
        }

        public void SetupRender(Int2 viewportSize, Vector2 jitter = default, int jitterFrame = default) {
            var jitteredProjection = projection;
            var jitteredPrevProj = previousProj;
            var scale = projection.M34;
            jitteredProjection.M31 += jitter.X / viewportSize.X * scale;
            jitteredProjection.M32 += jitter.Y / viewportSize.Y * scale;
            jitteredPrevProj.M31 += jitter.X / viewportSize.X * scale;
            jitteredPrevProj.M32 += jitter.Y / viewportSize.Y * scale;
            JitteredProjection = jitteredPrevProj;
            var previousVP = previousView * jitteredPrevProj;
            mainSceneMaterial.SetValue("PreviousViewProjection", previousVP);
            mainSceneMaterial.SetValue("TemporalJitter", Input.GetKeyDown(KeyCode.Q) ? default : jitter * 0.5f);
            mainSceneMaterial.SetValue("TemporalFrame", (float)jitterFrame);
            mainSceneMaterial.SetValue(RootMaterial.iPMat, Projection);
            foreach (var pass in scenePasses) {
                if (pass.TagsToInclude.Has(pass.Scene.TagManager.RequireTag("MainPass"))) {
                    //Scene.RootMaterial.SetValue("PreviousViewProjection", previousVP);
                    //Scene.RootMaterial.SetValue("TemporalJitter", Input.GetKeyDown(KeyCode.Q) ? default : jitter * 0.5f);
                    //Scene.RootMaterial.SetValue("TemporalFrame", (float)jitterFrame);
                    pass.SetViewProjection(view, jitteredProjection);
                }
            }
            previousView = View;
            previousProj = Projection;
        }
        public void ClearDynamicDraws() {
            foreach (var instance in dynamicInstances) {
                RemoveInstance(instance);
                Scene.RemoveInstance(instance);
            }
            dynamicInstances.Clear();
            dynamicDrawHash = 0;
        }
        public SceneInstance DrawDynamicMesh(Mesh mesh, Matrix4x4 transform, Material material) {
            var instance = Scene.CreateInstance(mesh.BoundingBox);
            dynamicInstances.Add(instance);
            Scene.SetTransform(instance, transform);
            AddInstance(instance, mesh, material, RenderTags.Default);
            dynamicDrawHash += HashCode.Combine(mesh.Revision, material.GetHashCode());
            return instance;
        }

        unsafe public void CommitMotion() {
            var movedInstances = Scene.GetMovedInstances();
            foreach (var instance in movedInstances) {
                var data = Scene.GetInstanceData(instance);
                var matrices = (Matrix4x4*)data.Data;
                var oldPos = RetainedRenderer.GetPosition(matrices[1]);
                var newPos = RetainedRenderer.GetPosition(matrices[0]);
                foreach (var bit in new BitEnumerator(instancePasses[instance])) {
                    var pass = scenePasses[bit];
                    pass.MoveInstance(instance, oldPos, newPos);
                }
            }
        }

        public void SetMeshLOD(Mesh mesh, Mesh hull, Material material) {
            foreach (var pass in scenePasses) {
                using var materials = new PooledList<Material>();
                if (material != null) materials.Add(material);
                if (mesh.Material != null) materials.Add(mesh.Material);
                if (pass.OverrideMaterial != null) materials.Add(pass.OverrideMaterial);
                materials.Add(pass.RetainedRenderer.Scene.RootMaterial);

                pass.SetMeshLOD(mesh, hull, materials);
            }
        }
    }
}
