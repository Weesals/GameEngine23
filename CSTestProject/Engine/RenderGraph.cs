﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Utility;

namespace Weesals.Engine {
    public class RenderTargetPool {
        private List<CSRenderTarget> pool = new();
        public CSRenderTarget RequireTarget(TextureDesc desc) {
            if (desc.MipCount == 0) desc.MipCount = 31 - BitOperations.LeadingZeroCount((uint)Math.Max(desc.Size.X, desc.Size.Y));
            for (int i = pool.Count - 1; i >= 0; --i) {
                var item = pool[i];
                if (item.GetSize() == desc.Size && item.GetFormat() == desc.Format && item.GetMipCount() == desc.MipCount) {
                    pool.RemoveAt(i);
                    return item;
                }
            }
            var target = CSRenderTarget.Create("RT Temp");
            target.SetSize(desc.Size);
            target.SetFormat(desc.Format);
            target.SetMipCount(desc.MipCount);
            return target;
        }
        public void Return(CSRenderTarget target) {
            pool.Add(target);
        }
    }
    public class RenderGraph {
        public struct PassData {
            public RenderPass RenderPass;
            public RangeInt InputsRange;
            public RangeInt OutputsRange;
            public RectI Viewport;
            public Int2 OutputSize;
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
                Graph.passes.Add(new PassData() { RenderPass = Pass, });
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
            public void SetOutputSize(Int2 size) {
                var pass = Graph.passes[passId];
                pass.OutputSize = size;
                Graph.passes[passId] = pass;
            }
        }
        public ref struct CustomTexturesContext {
            public readonly RenderGraph Graph;
            public readonly RectI Viewport;
            public readonly Span<RPInput> Inputs;
            public readonly Span<RPOutput> Outputs;
            public readonly Span<BufferItem> Buffers;
            public CustomTexturesContext(RenderGraph graph, RectI viewport, Span<RPInput> inputs, Span<RPOutput> outputs, Span<BufferItem> buffers) {
                Graph = graph;
                Viewport = viewport;
                Inputs = inputs;
                Outputs = outputs;
                Buffers = buffers;
            }
            public void OverwriteInput(in RPInput input, CSRenderTarget target) {
                var outputId = Graph.passes[input.OtherPassId].OutputsRange.Start + input.OtherOutput;
                Buffers[Graph.outputs[outputId].TargetId].Target = new RenderPass.Target(target);
            }
            public void OverwriteOutput(in RPOutput output, CSRenderTarget target) {
                OverwriteOutput(output, new RenderPass.Target(target));
            }
            public void OverwriteOutput(in RPOutput output, RenderPass.Target target) {
                Buffers[output.TargetId].Target = target;
            }
            public Int2 FindInputSize(int index) {
                var input = Inputs[index];
                var otherPassData = Graph.passes[input.OtherPassId];
                return otherPassData.OutputSize;
            }
        }
        private List<PassData> passes = new();
        private SparseArray<RPInput> dependencies = new();
        private SparseArray<RPOutput> outputs = new();
        private RenderTargetPool rtPool = new();
        public void Clear() {
            passes.Clear();
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
            public override string ToString() {
                return $"{Target}: {Description}";
            }
        }
        public struct ExecuteItem {
            public int PassId;
            public override string ToString() { return PassId.ToString(); }
        }
        private void RequireIO(int passId) {
            var selfPassData = passes[passId];
            var selfPass = selfPassData.RenderPass;
            var iocontext = new RenderPass.IOContext(this, passId);
            selfPass.GetInputOutput(ref iocontext);
            var inputs = iocontext.GetInputs();
            var selfOutputs = iocontext.GetOutputs();
            var deps = dependencies.Allocate(inputs.Length);
            var outs = outputs.Allocate(selfOutputs.Length);
            dependencies.Slice(deps).AsSpan().Fill(RPInput.Invalid);
            outputs.Slice(outs).AsSpan().Fill(RPOutput.Invalid);
            for (int i = 0; i < inputs.Length; ++i) dependencies[deps.Start + i].Input = inputs[i];
            for (int i = 0; i < selfOutputs.Length; ++i) outputs[outs.Start + i].Output = selfOutputs[i];
            foreach (var input in inputs) Debug.Assert(input.Name.IsValid());
            foreach (var output in selfOutputs) Debug.Assert(output.Name.IsValid());
            selfPassData.InputsRange = deps;
            selfPassData.OutputsRange = outs;
            passes[passId] = selfPassData;
        }
        private void FillDependencies(int passId) {
            RequireIO(passId);
            var selfPassData = passes[passId];
            var selfPass = selfPassData.RenderPass;
            var selfInputs = dependencies.AsSpan().Slice(selfPassData.InputsRange);
            for (int i = 0; i < selfInputs.Length; i++) {
                ref var selfDep = ref selfInputs[i];
                var selfInput = selfDep.Input;
                if (selfDep.OtherPassId == -1) {
                    for (int p = passId - 1; p >= 0; --p) {
                        var pass = passes[p];
                        var outId = pass.RenderPass.FindOutputI(selfInput.Name);
                        if (outId >= 0) {
                            selfDep.OtherPassId = p;
                            selfDep.OtherOutput = outId;
                            break;
                        }
                    }
                    Debug.Assert(selfDep.OtherPassId != -1, "Could not find pass for input buffer");
                }
                if (selfDep.OtherOutput == -1) {
                    selfDep.OtherOutput = passes[selfDep.OtherPassId].RenderPass.FindOutputI(selfInput.Name);
                }
                Debug.Assert(selfDep.OtherOutput >= -1, "Could not find dependency for " + selfPass);
            }
        }
        public struct Evaluator : IDisposable {
            public readonly RenderGraph Graph;
            public readonly RenderPass RootPass;
            public readonly CSGraphics Graphics;
            private PooledList<BufferItem> buffers;
            private PooledList<ExecuteItem> executeList;
            private PooledList<CSRenderTarget> tempTargets;
            private readonly List<PassData> passes => Graph.passes;
            private readonly SparseArray<RPInput> dependencies => Graph.dependencies;
            private readonly SparseArray<RPOutput> outputs => Graph.outputs;
            private readonly RenderTargetPool rtPool => Graph.rtPool;
            public Evaluator(RenderGraph graph, RenderPass pass, CSGraphics graphics) {
                Graph = graph;
                RootPass = pass;
                Graphics = graphics;
                buffers = new();
                executeList = new();
                tempTargets = new();
                executeList.Add(new ExecuteItem() { PassId = FindPass(RootPass), });
            }
            private int FindPass(RenderPass pass) {
                for (var p = passes.Count - 1; p >= 0; --p) if (passes[p].RenderPass == pass) return p; ;
                return -1;
            }
            private int FindEvaluator(int passId) {
                for (int o = executeList.Count - 1; o >= 0; --o) if (executeList[o].PassId == passId) return o;
                return -1;
            }
            private void SetSize(int evalId, Int2 newSize) {
                var selfExec = executeList[evalId];
                var selfPassData = passes[selfExec.PassId];
                var selfOutputs = outputs.Slice(selfPassData.OutputsRange).AsSpan();
                if (selfPassData.OutputSize == newSize) return;
                selfPassData.OutputSize = newSize;
                for (int i = 0; i < selfOutputs.Length; i++) {
                    ref var buffer = ref buffers[selfOutputs[i].TargetId];
                    buffer.Description.Size = selfPassData.OutputSize;
                }
                passes[selfExec.PassId] = selfPassData;
            }
            public void CollectDependencies() {
                // Collect dependencies
                int processedTo = 0;
                for (int l = 0; l < executeList.Count; ++l) {
                    var selfExec = executeList[l];
                    if (l <= processedTo) {
                        Graph.FillDependencies(selfExec.PassId);
                        ++processedTo;
                    }
                    var selfPassData = passes[selfExec.PassId];
                    var selfPass = selfPassData.RenderPass;
                    Debug.Assert(selfPassData.OutputsRange.Length == selfPass.Outputs.Length);
                    var selfInputs = dependencies.Slice(passes[selfExec.PassId].InputsRange).AsSpan();
                    var selfOutputs = outputs.Slice(selfPassData.OutputsRange).AsSpan();
                    uint passthroughMask = 0;
                    for (int o = 0; o < selfOutputs.Length; o++)
                        if (selfOutputs[o].Output.PassthroughInput >= 0)
                            passthroughMask |= 1u << selfOutputs[o].Output.PassthroughInput;
                    for (int i = 0; i < selfInputs.Length; i++) {
                        var selfInput = selfInputs[i];
                        // Mark any non-write-only dependencies as Required
                        if ((passthroughMask & (1u << i)) != 0) {
                            // If connected output node specifies size -1, fill its size
                            var otherPassData = passes[selfInput.OtherPassId];
                            otherPassData.Viewport = selfPassData.Viewport;
                            passes[selfInput.OtherPassId] = otherPassData;
                        }

                        // Require added to executeList and following self
                        int otherI = FindEvaluator(selfInput.OtherPassId);
                        if (otherI == -1) {
                            executeList.Add(new ExecuteItem() { PassId = selfInput.OtherPassId, });
                        } else if (otherI < l) {
                            var otherExec = executeList[otherI];
                            executeList.RemoveAt(otherI);
                            executeList.Insert(l, otherExec);
                            --l;
                        }

                    }
                }
            }
            public void CollectAndMergeDescriptors() {
                // Collect and merge descriptors
                for (int l = executeList.Count - 1; l >= 0; --l) {
                    var selfExec = executeList[l];
                    var selfPassData = passes[selfExec.PassId];
                    var selfInputs = dependencies.Slice(selfPassData.InputsRange).AsSpan();
                    var selfOutputs = outputs.Slice(selfPassData.OutputsRange).AsSpan();
                    // Fill selfTargets
                    for (int o = 0; o < selfOutputs.Length; ++o) {
                        var selfOutput = selfOutputs[o];
                        Debug.Assert(selfOutputs[o].TargetId == -1);
                        // First based on passthrough inputs (reuse buffer of input)
                        if (selfOutput.Output.PassthroughInput != -1) {
                            var selfDep = selfInputs[selfOutput.Output.PassthroughInput];
                            int otherI = FindEvaluator(selfDep.OtherPassId);
                            Debug.Assert(otherI >= 0);
                            ref var otherExec = ref executeList[otherI];
                            var otherTargetIds = outputs.AsSpan().Slice(passes[otherExec.PassId].OutputsRange);
                            selfOutputs[o].TargetId = otherTargetIds[selfDep.OtherOutput].TargetId;
                        }
                        // Otherwise allocate a new buffer
                        if (selfOutputs[o].TargetId == -1) {
                            selfOutputs[o].TargetId = buffers.Count;
                            buffers.Add(new BufferItem() { Description = selfOutput.Output.TargetDesc, });
                        }
                        // Populate the buffer with our specifications
                        ref var buffer = ref buffers[selfOutputs[o].TargetId];
                        if (buffer.Description.Format == BufferFormat.FORMAT_UNKNOWN)
                            buffer.Description.Format = selfOutput.Output.TargetDesc.Format;
                        if (buffer.Description.Size == 0)
                            buffer.Description.Size = selfOutput.Output.TargetDesc.Size;
                        if (buffer.Description.MipCount == 1)
                            buffer.Description.MipCount = selfOutput.Output.TargetDesc.MipCount;
                    }
                }
            }
            public void MarkAttachments() {
                for (int l = executeList.Count - 1; l >= 0; --l) {
                    var selfExec = executeList[l];
                    var selfInputs = dependencies.Slice(passes[selfExec.PassId].InputsRange).AsSpan();
                    for (int i = 0; i < selfInputs.Length; i++) {
                        var selfDep = selfInputs[i];
                        var selfInput = selfDep.Input;
                        if (selfInput.RequireAttachment) {
                            int otherI = FindEvaluator(selfDep.OtherPassId);
                            Debug.Assert(otherI >= 0);
                            ref var otherExec = ref executeList[otherI];
                            var otherOutputs = outputs.AsSpan().Slice(passes[otherExec.PassId].OutputsRange);
                            Debug.Assert(otherOutputs[selfDep.OtherOutput].TargetId >= 0);
                            buffers[otherOutputs[selfDep.OtherOutput].TargetId].RequireAttachment |= selfInput.RequireAttachment;
                        }
                    }
                }
            }
            public void OptimizeOrder() {
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
                        refCounter[selfDeps[i].OtherPassId]--;
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
                        if (score <= bestScore) continue;
                        bestScore = score;
                        best = l2;
                    }
                    Debug.Assert(best >= 0, "Could not find valid pass");
                    if (best != l + 1) {
                        var tmp = executeList[l + 1];
                        executeList[l + 1] = executeList[best];
                        executeList[best] = tmp;
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
            }
            public void ReconcileSizing() {
                uint customUpdatedMask = 0;
                for (int t = 0; t < 10; ++t) {
                    bool changes = false;
                    // Fill any RPs with sizing information from their outputs
                    // Continue until no more changes
                    for (int l = 0; l < executeList.Count; ++l) {
                        var selfExec = executeList[l];
                        var selfPassData = passes[selfExec.PassId];
                        var selfOutputs = outputs.Slice(selfPassData.OutputsRange).AsSpan();
                        if (selfPassData.OutputSize.X > 0) continue;
                        Int2 newSize = -1;
                        // Match size with any explicit or resolved output sizes
                        for (int i = 0; i < selfOutputs.Length; i++) {
                            var buffer = buffers[selfOutputs[i].TargetId];
                            if (buffer.Target.IsValid()) { newSize = buffer.Target.Texture.GetSize(); break; }
                            if (buffer.Description.Size.X > 0) { newSize = buffer.Description.Size; break; }
                        }
                        if (newSize.X <= 0) continue;
                        SetSize(l, newSize);
                        changes = true;
                    }
                    if (changes) continue;
                    // Spread sizing information to inputs
                    for (int l = 0; l < executeList.Count; ++l) {
                        var selfExec = executeList[l];
                        var selfPassData = passes[selfExec.PassId];
                        var selfInputs = dependencies.Slice(passes[selfExec.PassId].InputsRange).AsSpan();
                        Int2 inputSize = selfPassData.Viewport.Size;
                        if (inputSize.X == 0) inputSize = selfPassData.OutputSize;
                        if (inputSize.X == 0) continue;
                        for (int i = 0; i < selfInputs.Length; i++) {
                            var otherPass = passes[selfInputs[i].OtherPassId];
                            var otherOutputs = outputs.AsSpan().Slice(otherPass.OutputsRange);
                            var otherOutput = otherOutputs[selfInputs[i].OtherOutput];
                            ref var buffer = ref buffers[otherOutput.TargetId];
                            if (buffer.Description.Size.X < 0) {
                                buffer.Description.Size = inputSize / -buffer.Description.Size.X;
                                changes = true;
                            }
                        }
                    }
                    if (changes) continue;
                    // Allow RPs to provide their own RTs
                    for (int l = 0; l < executeList.Count; l++) {
                        var selfExec = executeList[l];
                        var selfPassData = passes[selfExec.PassId];
                        if ((customUpdatedMask & (1u << l)) != 0) continue;
                        if (selfPassData.RenderPass is ICustomOutputTextures custom) {
                            // Collect outputs
                            var selfInputs = dependencies.AsSpan().Slice(selfPassData.InputsRange);
                            var selfOutputs = outputs.Slice(selfPassData.OutputsRange).AsSpan();
                            var viewport = selfPassData.Viewport;
                            if (viewport.Width <= 0) viewport = new RectI(Int2.Zero, selfPassData.OutputSize);
                            if (viewport.Width <= 0) viewport = new RectI(Int2.Zero, Graphics.GetResolution());
                            var context = new CustomTexturesContext(Graph,
                                viewport,
                                selfInputs,
                                selfOutputs,
                                buffers);
                            if (!custom.FillTextures(ref context)) continue;
                            changes = true;
                        }
                        customUpdatedMask |= (1u << l);
                    }
                    if (changes) continue;
                    // Spread sizing information to inputs
                    for (int l = 0; l < executeList.Count; ++l) {
                        var selfExec = executeList[l];
                        var selfPassData = passes[selfExec.PassId];
                        var selfInputs = dependencies.Slice(passes[selfExec.PassId].InputsRange).AsSpan();
                        Int2 inputSize = selfPassData.Viewport.Size;
                        if (inputSize.X == 0) inputSize = selfPassData.OutputSize;
                        if (inputSize.X == 0) continue;
                        for (int i = 0; i < selfInputs.Length; i++) {
                            var otherPass = passes[selfInputs[i].OtherPassId];
                            var otherOutputs = outputs.AsSpan().Slice(otherPass.OutputsRange);
                            ref var buffer = ref buffers[otherOutputs[selfInputs[i].OtherOutput].TargetId];
                            if (buffer.Description.Size.X <= 0) {
                                buffer.Description.Size = inputSize;
                                changes = true;
                            }
                        }
                    }
                    if (!changes) break;
                }
            }
            public void DebugLogState() {
                string output = "";
                for (int l = 0; l < executeList.Count; l++) {
                    var selfExec = executeList[l];
                    var selfPassData = passes[selfExec.PassId];
                    var selfInputs = dependencies.Slice(passes[selfExec.PassId].InputsRange).AsSpan();
                    var selfOutputs = outputs.Slice(selfPassData.OutputsRange).AsSpan();
                    output += $"Pass[{selfPassData.RenderPass.Name}]\n";
                    output += $"  Inputs(";
                    for (int i = 0; i < selfInputs.Length; i++) {
                        var otherPass = passes[selfInputs[i].OtherPassId];
                        var otherOutputs = outputs.AsSpan().Slice(otherPass.OutputsRange);
                        if (i > 0) output += ", ";
                        output += $"{selfInputs[i].Input.Name}:{otherOutputs[selfInputs[i].OtherOutput].TargetId}";
                    }
                    output += $")\n";
                    output += $"  Outputs(";
                    for (int i = 0; i < selfOutputs.Length; i++) {
                        if (i > 0) output += ", ";
                        output += $"{selfOutputs[i].Output.Name}:{selfOutputs[i].TargetId}";
                    }
                    output += $")\n";
                }
                Debug.WriteLine(output);
            }
            public void AllocateTemporaryBuffers() {
                foreach (ref var buffer in buffers) {
                    if (buffer.Target.IsValid()) continue;
                    if (buffer.RequireAttachment) {
                        var desc = buffer.Description;
                        if (desc.Size.X == -1) desc.Size = Graphics.GetResolution();
                        if (desc.Format == BufferFormat.FORMAT_UNKNOWN) desc.Format = BufferFormat.FORMAT_R8G8B8A8_UNORM;
                        buffer.Target.Texture = rtPool.RequireTarget(desc);
                        tempTargets.Add(buffer.Target.Texture);
                    }
                }
            }
            public void RenderPasses() {
                using var targetsSpan = new PooledList<RenderPass.Target>(16);
                // Invoke render passes
                for (int r = executeList.Count - 1; r >= 0; --r) {
                    var selfExec = executeList[r];
                    var selfPassData = passes[selfExec.PassId];
                    var selfPass = selfPassData.RenderPass;

                    // Set resolved RTs to pass material
                    var selfInputs = dependencies.Slice(passes[selfExec.PassId].InputsRange).AsSpan();
                    for (int i = 0; i < selfInputs.Length; ++i) {
                        var input = selfInputs[i].Input;
                        var dep = selfInputs[i];
                        int index = FindEvaluator(dep.OtherPassId);
                        if (index < 0) continue;
                        var otherTarget = outputs.AsSpan().Slice(passes[executeList[index].PassId].OutputsRange)[dep.OtherOutput];
                        selfPass.GetPassMaterial().SetTexture(input.Name, buffers[otherTarget.TargetId].Target.Texture);
                    }

                    // Collect outputs
                    var selfOutputs = outputs.Slice(selfPassData.OutputsRange).AsSpan();
                    RenderPass.Target depth = default;
                    for (int i = 0; i < selfOutputs.Length; i++) {
                        var target = buffers[selfOutputs[i].TargetId].Target;
                        var fmt = target.Texture.IsValid() ? target.Texture.GetFormat() : selfOutputs[i].Output.TargetDesc.Format;
                        if (BufferFormatType.GetIsDepthBuffer(fmt)) depth = target;
                        else targetsSpan.Add(target);
                    }
                    RenderPass.Context context = new RenderPass.Context(depth, targetsSpan);
                    context.Viewport = selfPassData.Viewport;
                    if (context.Viewport.Width == 0) context.Viewport = new RectI(Int2.Zero, selfPassData.OutputSize);
                    if (context.Viewport.Width == 0) context.Viewport = new RectI(Int2.Zero, Graphics.GetResolution());
                    selfPassData.RenderPass.Render(Graphics, ref context);
                    targetsSpan.Clear();
                }
            }
            public void Dispose() {
                // Clean up temporary RTs
                foreach (var item in tempTargets) {
                    if (item.IsValid()) rtPool.Return(item);
                }
                buffers.Dispose();
                executeList.Dispose();
                tempTargets.Dispose();
            }
        }
        public void Execute(RenderPass pass, CSGraphics graphics) {
            using var evaluator = new Evaluator(this, pass, graphics);
            evaluator.CollectDependencies();
            evaluator.CollectAndMergeDescriptors();
            evaluator.MarkAttachments();
            evaluator.ReconcileSizing();
            evaluator.AllocateTemporaryBuffers();
            //evaluator.DebugLogState();
            evaluator.OptimizeOrder();
            evaluator.RenderPasses();
        }

        internal PassData GetPassData(int passId) {
            return passes[passId];
        }
    }
}