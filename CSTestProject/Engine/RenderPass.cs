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
        public bool MipMapped;
    }
    public class RenderPass {
        protected static Material blitMaterial;
        protected static Mesh quadMesh;

        public struct PassInput {
            public readonly CSIdentifier Name;
            public readonly bool IsWriteOnly;
            public PassInput(CSIdentifier name, bool writeOnly) { Name = name; IsWriteOnly = writeOnly; }
            public override string ToString() { return Name.ToString(); }
        }
        public struct PassOutput {
            public readonly CSIdentifier Name;
            public readonly int PassthroughInput;
            public TextureDesc TargetDesc { get; private set; }
            public PassOutput(CSIdentifier name, int passthroughInput = -1) {
                Name = name; PassthroughInput = passthroughInput;
            }
            public PassOutput SetTargetDesc(TextureDesc desc) { TargetDesc = desc; return this; }
            public override string ToString() { return Name.ToString(); }
        }
        public struct IOContext : IDisposable {
            private PooledList<PassInput> inputs;
            private PooledList<PassOutput> outputs;
            public IOContext() {
                inputs = new();
                outputs = new();
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
        protected PassInput[] Inputs { get; set; } = Array.Empty<PassInput>();
        // First item is always Depth
        protected PassOutput[] Outputs { get; set; } = Array.Empty<PassOutput>();

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
            public bool IsValid() { return Texture.IsValid(); }
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

            material.SetTexture("Texture", texture);
            material.SetValue("TextureSize", (Vector2)texture.GetSize());
            Span<CSBufferLayout> bindings = stackalloc CSBufferLayout[2];
            bindings[0] = quadMesh.IndexBuffer;
            bindings[1] = quadMesh.VertexBuffer;
            var pso = MaterialEvaluator.ResolvePipeline(graphics, bindings, new Span<Material>(ref material));
            var resources = MaterialEvaluator.ResolveResources(graphics, pso, new Span<Material>(ref material));
            fixed (CSBufferLayout* bindingsPtr = bindings) {
                graphics.Draw(pso, new MemoryBlock<CSBufferLayout>(bindingsPtr, bindings.Length), resources, CSDrawConfig.MakeDefault());
            }
        }
    }
    public class RenderTargetPool {
        private List<CSRenderTarget> pool = new();
        public CSRenderTarget RequireTarget(TextureDesc desc) {
            for (int i = pool.Count - 1; i >= 0; --i) {
                var item = pool[i];
                if (item.GetSize() == desc.Size && item.GetFormat() == desc.Format && (item.GetMipCount() > 1) == desc.MipMapped) {
                    pool.RemoveAt(i);
                    return item;
                }
            }
            var target = CSRenderTarget.Create();
            target.SetSize(desc.Size);
            target.SetFormat(desc.Format);
            if(desc.MipMapped) target.SetMipCount(
                31 - BitOperations.LeadingZeroCount((uint)Math.Max(desc.Size.X, desc.Size.Y))
            );
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
            public RangeInt InputsRange;
            public RangeInt OutputsRange;
            public RenderPassEvaluator Evaluator;
            public RectI Viewport;
            public override string ToString() { return $"{RenderPass}[{InputsRange}]"; }
        }
        public struct RPInput {
            public RenderPass.PassInput Input;
            public int OtherPassId;
            public int OtherOutput;
            public override string ToString() { return $"{Input}: {OtherPassId}[{OtherOutput}]"; }
            public static readonly RPInput Invalid = new RPInput() { OtherPassId = -1, OtherOutput = -1, };
        }
        public struct RPOutput {
            public RenderPass.PassOutput Output;
            public int TargetId;
            public override string ToString() { return $"{Output}: {TargetId}"; }
            public static readonly RPOutput Invalid = new RPOutput() { Output = default, TargetId = -1, };
        }
        public struct Builder {
            public readonly RenderGraph Graph;
            public readonly RenderPass Pass;
            private readonly int passId;
            public Builder(RenderGraph graph, RenderPass pass) {
                Graph = graph;
                Pass = pass;
                Debug.Assert(pass != null, "Pass is null");
                passId = Graph.passes.Count;
                var iocontext = new RenderPass.IOContext();
                Pass.GetInputOutput(ref iocontext);
                var inputs = iocontext.GetInputs();
                var outputs = iocontext.GetOutputs();
                var deps = Graph.dependencies.Allocate(inputs.Length);
                var outs = Graph.outputs.Allocate(outputs.Length);
                Graph.dependencies.Slice(deps).AsSpan().Fill(RPInput.Invalid);
                Graph.outputs.Slice(outs).AsSpan().Fill(RPOutput.Invalid);
                for (int i = 0; i < inputs.Length; ++i) Graph.dependencies[deps.Start + i].Input = inputs[i];
                for (int i = 0; i < outputs.Length; ++i) Graph.outputs[outs.Start + i].Output = outputs[i];
                foreach (var input in inputs) Debug.Assert(input.Name.IsValid());
                foreach (var output in outputs) Debug.Assert(output.Name.IsValid());
                Graph.passes.Add(new Pass() {
                    RenderPass = Pass,
                    InputsRange = deps,
                    OutputsRange = outs,
                });
            }
            public Builder SetEvaluator(RenderPassEvaluator evaluator) {
                var pass = Graph.passes[passId];
                pass.Evaluator = evaluator;
                Graph.passes[passId] = pass;
                return this;
            }
            public Builder SetDependency(CSIdentifier identifier, RenderPass other, int outputId = -1) {
                int otherPassId;
                if (other != null) {
                    for (otherPassId = passId - 1; otherPassId >= 0; --otherPassId) {
                        if (Graph.passes[otherPassId].RenderPass == other) break;
                    }
                } else {
                    Debug.Assert(outputId == -1, "Cannot specify an output id without pass");
                    for (otherPassId = passId - 1; otherPassId >= 0; --otherPassId) {
                        other = Graph.passes[otherPassId].RenderPass;
                        outputId = other.FindOutputI(identifier);
                        if (outputId >= 0) break;
                    }
                    Debug.Assert(outputId >= 0, "Failed to find pass for input " + identifier);
                }
                return SetDependency(identifier, otherPassId, outputId);
            }
            private Builder SetDependency(CSIdentifier identifier, int otherPassId, int outputId = -1) {
                var pass = Graph.passes[passId];
                //if (outputId == -1) outputId = Graph.passes[otherPassId].RenderPass.FindOutputI(identifier);
                var deps = Graph.dependencies.Slice(pass.InputsRange).AsSpan();
                int input = deps.Length - 1;
                for (; input >= 0; --input) if (deps[input].Input.Name == identifier) break;
                if (input != -1) {
                    deps[input].OtherPassId = otherPassId;
                    deps[input].OtherOutput = outputId;
                    Graph.passes[passId] = pass;
                }
                return this;
            }
            public void SetViewport(RectI viewport) {
                var pass = Graph.passes[passId];
                pass.Viewport = viewport;
                Graph.passes[passId] = pass;
            }
        }
        private List<Pass> passes = new();
        private HashSet<RenderPass> rendered = new();
        private SparseArray<RPInput> dependencies = new();
        private SparseArray<RPOutput> outputs = new();
        private RenderTargetPool rtPool = new();
        public void Clear() {
            passes.Clear();
            rendered.Clear();
            dependencies.Clear();
            outputs.Clear();
        }
        public Builder BeginPass(RenderPass pass) {
            return new Builder(this, pass);
        }

        public struct BufferItem {
            public TextureDesc Description;
            public bool RequireAttachment;
            public RenderPass.Target Target;
        }
        public struct ExecuteItem {
            public int PassId;
            //public RangeInt ResolvedTargets;
            public override string ToString() { return PassId.ToString(); }
        }
        private void FillDependencies(int passId) {
            var passData = passes[passId];
            var selfPass = passData.RenderPass;
            var selfDeps = dependencies.AsSpan().Slice(passData.InputsRange);
            for (int i = 0; i < selfDeps.Length; i++) {
                ref var dep = ref selfDeps[i];
                var input = dep.Input;
                if (dep.OtherPassId == -1) {
                    for (int p = passId - 1; p >= 0; --p) {
                        var pass = passes[p];
                        var outId = pass.RenderPass.FindOutputI(input.Name);
                        if (outId >= 0) {
                            dep.OtherPassId = p;
                            dep.OtherOutput = outId;
                            break;
                        }
                    }
                    Debug.Assert(dep.OtherPassId != -1, "Could not find pass for input buffer");
                }
                if (dep.OtherOutput == -1) {
                    dep.OtherOutput = passes[dep.OtherPassId].RenderPass.FindOutputI(input.Name);
                }
                Debug.Assert(dep.OtherOutput >= -1, "Could not find dependency for " + selfPass);
            }
        }
        public void Execute(RenderPass pass, CSGraphics graphics) {
            using var buffers = new PooledList<BufferItem>();
            //using var outputBufferIds = new PooledList<int>();
            //outputBufferIds.Add(-1, outputs.Items.Length);
            using var inputBufferIds = new PooledArray<int>(dependencies.Items.Length);
            Array.Fill(inputBufferIds.Data, -1);
            using var executeList = new PooledList<ExecuteItem>();
            using var tempTargets = new PooledList<CSRenderTarget>();

            {
                var passId = passes.Count - 1;
                for (; passId >= 0; --passId) if (passes[passId].RenderPass == pass) break;
                executeList.Add(new ExecuteItem() { PassId = passId, });
            }
            // Collect dependencies
            for (int l = 0; l < executeList.Count; ++l) {
                var selfExec = executeList[l];
                FillDependencies(selfExec.PassId);
                var selfDeps = dependencies.Slice(passes[selfExec.PassId].InputsRange);
                for (int i = 0; i < selfDeps.Count; i++) {
                    var selfDep = selfDeps[i];
                    int otherI = 0;
                    for (; otherI < executeList.Count; ++otherI) if (executeList[otherI].PassId == selfDep.OtherPassId) break;
                    if (otherI >= executeList.Count) {
                        var otherPass = passes[selfDep.OtherPassId].RenderPass;
                        // Add it and resolve its targets
                        executeList.Add(new ExecuteItem() { PassId = selfDep.OtherPassId, });
                    }
                }
            }
            // Build executeList based on dependencies
            // Mark any non-write-only dependencies as Required
            for (int l = 0; l < executeList.Count; ++l) {
                var selfExec = executeList[l];
                var selfPassData = passes[selfExec.PassId];
                var selfPass = selfPassData.RenderPass;
                var selfDeps = dependencies.Slice(passes[selfExec.PassId].InputsRange).AsSpan();
                var selfOutputs = outputs.Slice(selfPassData.OutputsRange).AsSpan();
                uint passthroughMask = 0;
                for (int o = 0; o < selfOutputs.Length; o++)
                    if (selfOutputs[o].Output.PassthroughInput >= 0)
                        passthroughMask |= 1u << selfOutputs[o].Output.PassthroughInput;
                for (int i = 0; i < selfDeps.Length; ++i) {
                    var selfInput = selfDeps[i];
                    var selfDep = selfDeps[i];
                    var otherOutputI = selfDep.OtherOutput;
                    // Check that the item doesnt already exist
                    int otherI = 0;
                    for (; otherI < executeList.Count; ++otherI) if (executeList[otherI].PassId == selfDep.OtherPassId) break;
                    Debug.Assert(otherI < executeList.Count);
                    if ((passthroughMask & (1u << i)) != 0) {
                        var otherPassData = passes[selfDep.OtherPassId];
                        otherPassData.Viewport = selfPassData.Viewport;
                        passes[selfDep.OtherPassId] = otherPassData;
                    }
                }
            }
            // Collect and merge descriptors
            for (int l = executeList.Count - 1; l >= 0; --l) {
                var selfExec = executeList[l];
                var selfPassData = passes[selfExec.PassId];
                var selfPass = selfPassData.RenderPass;
                var selfDeps = dependencies.Slice(selfPassData.InputsRange).AsSpan();
                var selfOutputIds = outputs.Slice(selfPassData.OutputsRange).AsSpan();
                var selfInputIds = inputBufferIds.AsSpan().Slice(selfPassData.InputsRange);
                // Grab input buffers from dependencies
                for (int i = 0; i < selfDeps.Length; i++) {
                    var selfDep = selfDeps[i];
                    int otherI = 0;
                    for (; otherI < executeList.Count; ++otherI) if (executeList[otherI].PassId == selfDep.OtherPassId) break;
                    Debug.Assert(otherI < executeList.Count);
                    ref var otherExec = ref executeList[otherI];
                    var otherTargetIds = outputs.AsSpan().Slice(passes[otherExec.PassId].OutputsRange);
                    selfInputIds[i] = otherTargetIds[selfDep.OtherOutput].TargetId;
                }
                // Fill selfTargets
                for (int o = 0; o < selfOutputIds.Length; ++o) {
                    var output = selfOutputIds[o];
                    Debug.Assert(selfOutputIds[o].TargetId == -1);
                    // First based on passthrough inputs (reuse buffer of input)
                    if (output.Output.PassthroughInput != -1) selfOutputIds[o].TargetId = selfInputIds[output.Output.PassthroughInput];
                    // Otherwise allocate a new buffer
                    if (selfOutputIds[o].TargetId == -1) {
                        selfOutputIds[o].TargetId = buffers.Count;
                        buffers.Add(new BufferItem() { Description = output.Output.TargetDesc, });
                    }
                    // Populate the buffer with our specifications
                    ref var buffer = ref buffers[selfOutputIds[o].TargetId];
                    if (buffer.Description.Format == BufferFormat.FORMAT_UNKNOWN)
                        buffer.Description.Format = output.Output.TargetDesc.Format;
                    if (buffer.Description.Size == 0)
                        buffer.Description.Size = output.Output.TargetDesc.Size;
                    buffer.Description.MipMapped |= output.Output.TargetDesc.MipMapped;
                }
            }
            // Mark input buffers as requiring attachment
            for (int l = executeList.Count - 1; l >= 0; --l) {
                var selfExec = executeList[l];
                var selfDeps = dependencies.Slice(passes[selfExec.PassId].InputsRange).AsSpan();
                var selfPassData = passes[selfExec.PassId];
                var selfPass = selfPassData.RenderPass;
                var selfOutputIds = outputs.Slice(selfPassData.OutputsRange).AsSpan();
                for (int i = 0; i < selfDeps.Length; i++) {
                    var selfInput = selfDeps[i].Input;
                    var selfDep = selfDeps[i];
                    if (!selfInput.IsWriteOnly) {
                        int otherI = 0;
                        for (; otherI < executeList.Count; ++otherI) if (executeList[otherI].PassId == selfDep.OtherPassId) break;
                        Debug.Assert(otherI < executeList.Count);
                        ref var otherExec = ref executeList[otherI];
                        var otherTargetIds = outputs.AsSpan().Slice(passes[otherExec.PassId].OutputsRange);
                        Debug.Assert(otherTargetIds[selfDep.OtherOutput].TargetId >= 0);
                        buffers[otherTargetIds[selfDep.OtherOutput].TargetId].RequireAttachment |= !selfInput.IsWriteOnly;
                    }
                }
            }
            // Reorder for performance
            {
                using var refCounter = new PooledArray<int>(passes.Count);
                refCounter.AsSpan().Fill(0);
                for (int l = 0; l < executeList.Count; ++l) {
                    var selfExec = executeList[l];
                    var selfDeps = dependencies.Slice(passes[selfExec.PassId].InputsRange);
                    for (int i = 0; i < selfDeps.Count; i++) {
                        var selfDep = selfDeps[i];
                        refCounter[selfDep.OtherPassId]++;
                    }
                }
                // Reorder to preferred order
                for (int l = 0; l < executeList.Count - 1; l++) {
                    var selfExec = executeList[l];
                    var selfOutputs = outputs.Slice(passes[selfExec.PassId].OutputsRange);
                    Debug.Assert(refCounter[selfExec.PassId] == 0, "Pass should have no dependencies by now");
                    var selfDeps = dependencies.Slice(passes[selfExec.PassId].InputsRange);
                    for (int i = 0; i < selfDeps.Count; i++) {
                        var selfDep = selfDeps[i];
                        refCounter[selfDep.OtherPassId]--;
                    }
                    int best = -1;
                    int bestScore = -1;
                    for (int l2 = l + 1; l2 < executeList.Count; l2++) {
                        var otherExec = executeList[l2];
                        if (refCounter[otherExec.PassId] != 0) continue;
                        var otherOutputs = outputs.Slice(passes[otherExec.PassId].OutputsRange);
                        int score = 0;
                        int min = Math.Min(otherOutputs.Count, selfOutputs.Count);
                        for (int i = 0; i < min; i++) {
                            if (selfOutputs[i].TargetId == otherOutputs[i].TargetId) ++score;
                        }
                        if (score > bestScore) {
                            bestScore = score;
                            best = l2;
                        }
                    }
                    Debug.Assert(best >= 0, "Could not find valid pass");
                    if (best != l + 1) {
                        var tmp = executeList[l + 1];
                        executeList[l + 1] = executeList[best];
                        executeList[best] = tmp;
                    }
                }
            }
            /*for (int i = 0; i < executeList.Count; i++) {
                var selfExec = executeList[i];
                var selfOutputIds = outputs.AsSpan().Slice(passes[selfExec.PassId].OutputsRange);
                var selfInputIds = inputBufferIds.AsSpan().Slice(passes[selfExec.PassId].InputsRange);
                uint idsMask = 0;
                for (int i2 = i + 1; i2 < executeList.Count; i2++) {
                    var otherExec = executeList[i2];
                    var otherOutputIds = outputs.AsSpan().Slice(passes[otherExec.PassId].OutputsRange);
                    var otherInputIds = inputBufferIds.AsSpan().Slice(passes[otherExec.PassId].InputsRange);
                    bool sharesRT = false;
                    uint otherIdsMask = 0;
                    for (int t = 0; t < otherOutputIds.Length; t++) {
                        foreach (var item in selfOutputIds) if (otherOutputIds[t].TargetId == item.TargetId) sharesRT = true;
                        if (selfInputIds.Contains(otherOutputIds[t].TargetId)) otherIdsMask = 0xffffffff;
                        otherIdsMask |= 1u << otherOutputIds[t].TargetId;
                    }
                    bool hasDependencies = (idsMask & otherIdsMask) != 0;
                    idsMask |= otherIdsMask;
                    // This item is irrelevant
                    if (!sharesRT) continue;
                    // This item is already well positioned
                    if (i2 == i + 1) break;
                    // Another dependency is blocking this move
                    if (hasDependencies) break;

                    var tmp = executeList[i + 1];
                    executeList[i + 1] = otherExec;
                    executeList[i2] = tmp;
                    break;
                }
            }*/
            // Create temporary RTs for any required RTs
            foreach (ref var buffer in buffers) {
                if (buffer.Target.IsValid()) continue;
                if (buffer.RequireAttachment) {
                    var desc = buffer.Description;
                    if (desc.Size.X == -1) desc.Size = graphics.GetResolution();
                    if (desc.Format == BufferFormat.FORMAT_UNKNOWN) desc.Format = BufferFormat.FORMAT_R8G8B8A8_UNORM;
                    buffer.Target.Texture = rtPool.RequireTarget(desc);
                    tempTargets.Add(buffer.Target.Texture);
                }
            }
            // Set resolved RTs for any passes that require them
            for (int l = 0; l < executeList.Count; ++l) {
                var selfExec = executeList[l];
                var selfPass = passes[selfExec.PassId].RenderPass;
                var selfDeps = dependencies.Slice(passes[selfExec.PassId].InputsRange).AsSpan();
                for (int i = 0; i < selfDeps.Length; ++i) {
                    var input = selfDeps[i].Input;
                    var dep = selfDeps[i];
                    int index = 0;
                    for (; index < executeList.Count; ++index) if (executeList[index].PassId == dep.OtherPassId) break;
                    if (index >= executeList.Count) continue;
                    var otherTarget = outputs.AsSpan().Slice(passes[executeList[index].PassId].OutputsRange)[dep.OtherOutput];
                    selfPass.GetPassMaterial().SetTexture(input.Name, buffers[otherTarget.TargetId].Target.Texture);
                }
            }
            using var targetsSpan = new PooledList<RenderPass.Target>(16);
            // Invoke render passes
            for (int r = executeList.Count - 1; r >= 0; --r) {
                var execItem = executeList[r];
                var passData = passes[execItem.PassId];
                var selfTargetIds = outputs.AsSpan().Slice(passes[execItem.PassId].OutputsRange);
                var selfOutputs = outputs.Slice(passData.OutputsRange).AsSpan();
                RenderPass.Target depth = default;
                for (int i = 0; i < selfTargetIds.Length; i++) {
                    var target = buffers[selfTargetIds[i].TargetId].Target;
                    var fmt = target.Texture.IsValid() ? target.Texture.GetFormat() : selfOutputs[i].Output.TargetDesc.Format;
                    if (BufferFormatType.GetIsDepthBuffer(fmt)) depth = target;
                    else targetsSpan.Add(target);
                }
                RenderPass.Context context = new RenderPass.Context(depth, targetsSpan) {
                    Viewport = passData.Viewport,
                };
                if (passData.Evaluator != null)
                    passData.Evaluator(graphics, ref context);
                else
                    passData.RenderPass.Render(graphics, ref context);
                targetsSpan.Clear();
            }
            // Clean up temporary RTs
            foreach (var item in tempTargets) {
                if (item.IsValid()) rtPool.Return(item);
            }
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

        public override void Render(CSGraphics graphics, ref Context context) {
            base.Render(graphics, ref context);
            RenderQueue.Clear();
            RenderScene(graphics, ref context);
        }
        public virtual void RenderScene(CSGraphics graphics, ref Context context) {
            PreRender?.Invoke(graphics);
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
        public override void RenderScene(CSGraphics graphics, ref Context context) {
            graphics.Clear();
            base.RenderScene(graphics, ref context);
        }
    }
    public class BasePass : ScenePass {
        public BasePass(Scene scene) : base(scene, "BasePass") {
            Outputs = new[] {
                new PassOutput("SceneDepth", 0).SetTargetDesc(new TextureDesc() { Size = -1, Format = BufferFormat.FORMAT_D24_UNORM_S8_UINT, }),
                new PassOutput("SceneColor", 1).SetTargetDesc(new TextureDesc() { Size = -1, Format = BufferFormat.FORMAT_R11G11B10_FLOAT, }),
            };
            Inputs = new[] {
                new PassInput("SceneDepth", true),
                new PassInput("SceneColor", true),
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
            basePassMat.SetValue("_LightColor0", 2 * new Vector3(1.0f, 0.98f, 0.95f) * 2.0f);
        }
    }
    public class TransparentPass : ScenePass {
        public TransparentPass(Scene scene) : base(scene, "TransparentPass") {
            Outputs = new[] {
                new PassOutput("SceneDepth", 0).SetTargetDesc(new TextureDesc() { Size = -1, Format = BufferFormat.FORMAT_D24_UNORM_S8_UINT, }),
                new PassOutput("SceneColor", 1).SetTargetDesc(new TextureDesc() { Size = -1, /*Format = BufferFormat.FORMAT_R11G11B10_FLOAT,*/ }),
            };
            Inputs = new[] {
                new PassInput("SceneDepth", false),
                new PassInput("SceneColor", true),
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
    public class HiZPass : RenderPass {
        protected Material depthBlitMaterial;
        public HiZPass() : base("HighZ") {
            Outputs = new[] {
                new PassOutput("SceneDepth", 0).SetTargetDesc(new TextureDesc() { Size = -1, Format = BufferFormat.FORMAT_D24_UNORM_S8_UINT, MipMapped = true, }),
            };
            Inputs = new[] {
                new PassInput("SceneDepth", false),
            };
            depthBlitMaterial = new Material("./assets/copydepth.hlsl");
            depthBlitMaterial.SetBlendMode(BlendMode.MakeOpaque());
            depthBlitMaterial.SetDepthMode(new DepthMode(DepthMode.Comparisons.Always, true));
        }
        unsafe public override void Render(CSGraphics graphics, ref Context context) {
            var sceneDepth = context.ResolvedDepth;
            var size = sceneDepth.Texture.GetSize();
            for (int m = 1; m < sceneDepth.Texture.GetMipCount(); ++m) {
                bool isOdd = ((size.X | size.Y) & 0x01) != 0;
                size = Int2.Max(size >> 1, Int2.One);
                graphics.SetRenderTargets(default(CSRenderTargetBinding), new CSRenderTargetBinding(sceneDepth.Texture.mRenderTarget, m, 0));
                graphics.SetViewport(new RectI(Int2.Zero, size));
                depthBlitMaterial.SetMacro("ODD", isOdd ? "1" : "0");
                DrawQuad(graphics, sceneDepth.Texture, depthBlitMaterial);
            }
        }
    }
    public class BloomPass : RenderPass {
        protected Material bloomChainMaterial;
        public BloomPass() : base("Bloom") {
            Inputs = new[] {
                new PassInput("SceneColor", false),
            };
            Outputs = new[] {
                new PassOutput("BloomChain").SetTargetDesc(new TextureDesc() { Size = -1, Format = BufferFormat.FORMAT_R11G11B10_FLOAT, MipMapped = true, }),
            };
            bloomChainMaterial = new Material("./assets/bloomchain.hlsl");
            bloomChainMaterial.SetBlendMode(BlendMode.MakeOpaque());
            bloomChainMaterial.SetDepthMode(DepthMode.MakeOff());
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
                new PassInput("SceneColor", false),
                new PassInput("BloomChain", false),
            };
            Outputs = new[] {
                new PassOutput("SceneColor"),
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
        public RenderGraph.RenderPassEvaluator OnRender;
        public DeferredPass(string name, PassInput[]? inputs, PassOutput[]? outputs, RenderGraph.RenderPassEvaluator onRender) : base(name) {
            if (inputs != null) Inputs = inputs;
            if (outputs != null) Outputs = outputs;
            OnRender = onRender;
        }
        public override void Render(CSGraphics graphics, ref Context context) {
            base.Render(graphics, ref context);
            OnRender(graphics, ref context);
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
