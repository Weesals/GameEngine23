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
        public Int2 Size;
        public BufferFormat Format;
    }
    public class RenderPass {
        public class PassInput {
            public readonly CSIdentifier Name;
            public readonly bool IsWriteOnly;
            public PassInput(CSIdentifier name, bool writeOnly) { Name = name; IsWriteOnly = writeOnly; }
        }
        public class PassOutput {
            public readonly CSIdentifier Name;
            public readonly int PassthroughInput;
            public TextureDesc TargetDesc { get; private set; }
            public PassOutput(CSIdentifier name, int passthroughInput = -1) {
                Name = name; PassthroughInput = passthroughInput;
            }
            public PassOutput SetTargetDesc(TextureDesc desc) { TargetDesc = desc; return this; }
        }

        public readonly string Name;
        public PassInput[] Inputs { get; protected set; } = Array.Empty<PassInput>();
        // First item is always Depth
        public PassOutput[] Outputs { get; protected set; } = Array.Empty<PassOutput>();

        //public CSRenderTarget RenderTarget { get; protected set; }
        public Material OverrideMaterial { get; protected set; }

        private List<RenderPass> dependencies = new();

        public RenderPass(string name) {
            Name = name;
            OverrideMaterial = new();
            OverrideMaterial.SetValue("View", Matrix4x4.Identity);
            OverrideMaterial.SetValue("Projection", Matrix4x4.Identity);
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

        public ref struct Context {
            public readonly Span<CSRenderTarget> ResolvedTargets;
            public Context(Span<CSRenderTarget> targets) { ResolvedTargets = targets; }
        }
        public virtual void Bind(CSGraphics graphics, ref Context context) {
            var colorTargets = new PooledList<CSRenderTarget>();
            CSRenderTarget depthTarget = context.ResolvedTargets[0];
            foreach (var item in context.ResolvedTargets.Slice(1)) {
                colorTargets.Add(item);
            }
            graphics.SetRenderTargets(colorTargets, depthTarget);
        }
        public virtual void Render(CSGraphics graphics) {
        }

    }
    public class RenderTargetPool {
        private List<CSRenderTarget> pool = new();
        public CSRenderTarget RequireTarget(BufferFormat fmt, Int2 size) {
            for (int i = pool.Count - 1; i >= 0; --i) {
                var item = pool[i];
                if (item.GetSize() == size && item.GetFormat() == fmt) {
                    pool.RemoveAt(i);
                    return item;
                }
            }
            var target = CSRenderTarget.Create();
            target.SetSize(size);
            target.SetFormat(fmt);
            return target;
        }
        public void Return(CSRenderTarget target) {
            pool.Add(target);
        }
    }
    public class RenderGraph {
        public delegate void RenderPassEvaluator(CSGraphics graphics, ref RenderPass.Context context);
        public struct Pass {
            public RenderPass RenderPass;
            public RangeInt Dependents;
            public RenderPassEvaluator Evaluator;
            public override string ToString() { return $"{RenderPass}[{Dependents}]"; }
        }
        public struct Dependency {
            public RenderPass OtherPass;
            public int OtherOutput;
            public override string ToString() { return $"{OtherPass}[{OtherOutput}]"; }
        }
        public struct Builder {
            public readonly RenderGraph Graph;
            public readonly RenderPass Pass;
            public Builder(RenderGraph graph, RenderPass pass) {
                Graph = graph;
                Pass = pass;
                Graph.passes[pass] = new Pass() {
                    RenderPass = Pass,
                    Dependents = Graph.dependencies.Allocate(Pass.Inputs.Length),
                };
            }
            public Builder SetEvaluator(RenderPassEvaluator evaluator) {
                if (!Graph.passes.TryGetValue(Pass, out var pass)) {
                    Debug.Fail("Invalid state");
                }
                pass.Evaluator = evaluator;
                Graph.passes[Pass] = pass;
                return this;
            }
            public Builder SetDependency(CSIdentifier identifier, RenderPass other, int outputId) {
                if (!Graph.passes.TryGetValue(Pass, out var pass)) {
                    Debug.Fail("Invalid state");
                }
                int input = Pass.Inputs.Length - 1;
                for (; input >= 0; --input) if (Pass.Inputs[input].Name == identifier) break;
                if (input != -1) {
                    var deps = Graph.dependencies.Slice(pass.Dependents).AsSpan();
                    deps[input].OtherPass = other;
                    deps[input].OtherOutput = outputId;
                    Graph.passes[Pass] = pass;
                }
                return this;
            }
        }
        private Dictionary<RenderPass, Pass> passes = new();
        private HashSet<RenderPass> rendered = new();
        private SparseArray<Dependency> dependencies = new();
        private RenderTargetPool rtPool = new();
        public void Clear() {
            passes.Clear();
            rendered.Clear();
            dependencies.Clear();
        }
        public Builder BeginPass(RenderPass pass) {
            return new Builder(this, pass);
        }

        public struct ExecuteItem {
            public RenderPass Pass;
            public RangeInt ResolvedTargets;
            public uint SRVRequiredMask;
            public override string ToString() { return Pass?.ToString(); }
        }
        private SparseArray<CSRenderTarget> resolvedTargets = new();
        public void Execute(RenderPass pass, CSGraphics graphics) {
            using var executeList = new PooledList<ExecuteItem>();
            using var tempTargets = new PooledList<CSRenderTarget>();
            executeList.Add(new ExecuteItem() { Pass = pass, ResolvedTargets = resolvedTargets.Allocate(pass.Outputs.Length), });
            // Build executeList based on dependencies
            // Mark any non-write-only dependencies as Required
            for (int l = 0; l < executeList.Count; ++l) {
                var item = executeList[l];
                var deps = dependencies.Slice(passes[item.Pass].Dependents);
                for (int i = 0; i < item.Pass.Inputs.Length; ++i) {
                    var input = item.Pass.Inputs[i];
                    var dep = deps[i];
                    var opass = dep.OtherPass;
                    if (opass == null) continue;
                    // Check that the item doesnt already exist
                    int index = 0;
                    for (; index < executeList.Count; ++index) if (executeList[index].Pass == opass) break;
                    if (index >= executeList.Count) {
                        // Add it and resolve its targets
                        executeList.Add(new ExecuteItem() { Pass = opass, ResolvedTargets = resolvedTargets.Allocate(opass.Outputs.Length), });
                    }
                    ref var execItem = ref executeList[index];
                    if (!input.IsWriteOnly) {
                        execItem.SRVRequiredMask |= 1u << dep.OtherOutput;
                    }
                }
            }
            for (int l = 0; l < executeList.Count; ++l) {
                var item = executeList[l];
                for (int o = 0; o < item.Pass.Outputs.Length; ++o) {
                    if ((item.SRVRequiredMask & (1u << o)) != 0) {

                    }
                }
            }
            // Create temporary RTs for any required RTs
            for (int l = executeList.Count - 1; l >= 0; --l) {
                var item = executeList[l];
                var deps = dependencies.Slice(passes[item.Pass].Dependents);
                var targets = resolvedTargets.Slice(item.ResolvedTargets);
                for (int o = 0; o < item.Pass.Outputs.Length; ++o) {
                    var output = item.Pass.Outputs[o];
                    if (output.PassthroughInput >= 0) {
                        var input = item.Pass.Inputs[output.PassthroughInput];
                        var dep = deps[output.PassthroughInput];
                        int index = 0;
                        for (; index < executeList.Count; ++index) if (executeList[index].Pass == dep.OtherPass) break;
                        var otherTarget = resolvedTargets.Slice(executeList[index].ResolvedTargets)[dep.OtherOutput];
                        targets[o] = otherTarget;
                    }
                    if ((item.SRVRequiredMask & (1u << o)) != 0) {
                        var size = output.TargetDesc.Size;
                        if (size.X == -1) size = graphics.GetResolution();
                        targets[o] = rtPool.RequireTarget(output.TargetDesc.Format, size);
                        tempTargets.Add(targets[o]);
                    }
                }
            }
            // Set resolved RTs for any passes that require them
            for (int l = 0; l < executeList.Count; ++l) {
                var item = executeList[l];
                var deps = dependencies.Slice(passes[item.Pass].Dependents);
                for (int i = 0; i < item.Pass.Inputs.Length; ++i) {
                    var input = item.Pass.Inputs[i];
                    var dep = deps[i];
                    int index = 0;
                    for (; index < executeList.Count; ++index) if (executeList[index].Pass == dep.OtherPass) break;
                    if (index >= executeList.Count) continue;
                    var otherTarget = resolvedTargets.Slice(executeList[index].ResolvedTargets)[dep.OtherOutput];
                    item.Pass.GetPassMaterial().SetTexture(input.Name, otherTarget);
                }
            }
            // Invoke render passes
            for (int r = executeList.Count - 1; r >= 0; --r) {
                var execItem = executeList[r];
                var tpass = execItem.Pass;
                if (!rendered.Add(tpass)) continue;
                RenderPass.Context context = new RenderPass.Context(resolvedTargets.Slice(execItem.ResolvedTargets));
                var tpassData = passes[tpass];
                if (tpassData.Evaluator != null)
                    tpassData.Evaluator(graphics, ref context);
                else {
                    tpassData.RenderPass.Bind(graphics, ref context);
                    tpassData.RenderPass.Render(graphics);
                }
            }
            // Clean up temporary RTs
            foreach (var item in tempTargets) {
                if (item.IsValid()) rtPool.Return(item);
            }
            resolvedTargets.Clear();
        }
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

        public override void Bind(CSGraphics graphics, ref RenderPass.Context context) {
            base.Bind(graphics, ref context);
            RenderQueue.Clear();
        }
        public override void Render(CSGraphics graphics) {
            base.Render(graphics);
            RetainedRenderer.SubmitToRenderQueue(graphics, RenderQueue, Frustum);
            RenderQueue.Render(graphics);
        }
    }

    public class ShadowPass : ScenePass {
        public ShadowPass(Scene scene) : base(scene, "Shadows") {
            Outputs = new[] { new PassOutput("ShadowMap").SetTargetDesc(new TextureDesc() { Size = 1024, Format = BufferFormat.FORMAT_D24_UNORM_S8_UINT, }) };
            OverrideMaterial.SetRenderPassOverride("ShadowCast");
        }
        public bool UpdateShadowFrustum(ScenePass basePass) {
            // Create shadow projection based on frustum near/far corners
            var frustum = basePass.GetFrustum();
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
    }
    public class BasePass : ScenePass {
        public BasePass(Scene scene) : base(scene, "BasePass") {
            Outputs = new[] {
                new PassOutput("SceneDepth").SetTargetDesc(new TextureDesc() { Size = -1, Format = BufferFormat.FORMAT_D24_UNORM_S8_UINT, }),
                new PassOutput("BasePass").SetTargetDesc(new TextureDesc() { Size = -1, Format = BufferFormat.FORMAT_R11G11B10_FLOAT, }),
            };
            Inputs = new[] {
                new PassInput("ShadowMap", false),
            };
        }
        public void UpdateShadowParameters(ScenePass shadowPass) {
            var basePassMat = OverrideMaterial;
            var shadowPassViewProj = shadowPass.View * shadowPass.Projection;
            Matrix4x4.Invert(View, out var basePassInvView);
            basePassMat.SetValue("ShadowViewProjection", shadowPassViewProj);
            basePassMat.SetValue("ShadowIVViewProjection", basePassInvView * shadowPassViewProj);
            basePassMat.SetValue("_WorldSpaceLightDir0", Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ, basePassInvView)));
            basePassMat.SetValue("_LightColor0", 2 * new Vector3(1.0f, 0.98f, 0.95f));
        }
    }
    public class TransparentPass : ScenePass {
        public TransparentPass(Scene scene) : base(scene, "BasePass") {
            Outputs = new[] {
                new PassOutput("SceneDepth", 0).SetTargetDesc(new TextureDesc() { Size = -1, Format = BufferFormat.FORMAT_D24_UNORM_S8_UINT, }),
                new PassOutput("BasePass", 1).SetTargetDesc(new TextureDesc() { Size = -1, Format = BufferFormat.FORMAT_R11G11B10_FLOAT, }),
            };
            Inputs = new[] {
                new PassInput("SceneDepth", false),
                new PassInput("BasePass", true),
                new PassInput("ShadowMap", false),
            };
            TagsToInclude.Clear();
            TagsToInclude.Add(scene.TagManager.RequireTag("Transparent"));
            GetPassMaterial().SetBlendMode(BlendMode.MakeAlphaBlend());
            GetPassMaterial().SetDepthMode(DepthMode.MakeReadOnly());
        }
        public void UpdateShadowParameters(ScenePass shadowPass) {
            var basePassMat = OverrideMaterial;
            var shadowPassViewProj = shadowPass.View * shadowPass.Projection;
            Matrix4x4.Invert(View, out var basePassInvView);
            basePassMat.SetValue("ShadowViewProjection", shadowPassViewProj);
            basePassMat.SetValue("ShadowIVViewProjection", basePassInvView * shadowPassViewProj);
            basePassMat.SetValue("_WorldSpaceLightDir0", Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ, basePassInvView)));
            basePassMat.SetValue("_LightColor0", 2 * new Vector3(1.0f, 0.98f, 0.95f));
        }
    }
    public class PresentPass : RenderPass {
        public PresentPass() : base("Present") { }
        public void UpdateWithBasePass(BasePass basePass) {
            //SetDependency("BaseColor", basePass);
        }
        public new void Render(CSGraphics graphics) {
            base.Render(graphics);
        }
    }

    public class ScenePassManager {
        private List<ScenePass> scenePasses = new();
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

        public void AddPass(ScenePass pass) {
            scenePasses.Add(pass);
        }
    }
}
