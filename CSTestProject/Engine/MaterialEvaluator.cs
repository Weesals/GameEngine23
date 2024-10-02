using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.Engine.Jobs;
using Weesals.Engine.Profiling;
using Weesals.Utility;

namespace Weesals.Engine {
	public ref struct MaterialEvaluatorContext {
		readonly MaterialEvaluator mCache;
		Span<byte> mOutput;
        public int mIterator;
        private Span<byte> GetAndIterateParameter(CSIdentifier name) {
            var parId = mCache.GetParameters()[mIterator++];
			var value = mCache.GetAllValues()[parId];
            return mOutput.Slice(value.OutputOffset, value.DataSize);
        }
		public MaterialEvaluatorContext(MaterialEvaluator cache, int iterator, Span<byte> output) {
			mCache = cache;
			mIterator = iterator;
			mOutput = output;
		}
		public T GetUniform<T>(CSIdentifier name) where T : unmanaged {
			return MemoryMarshal.Cast<byte, T>(GetAndIterateParameter(name))[0];
		}
	}
    public class MaterialEvaluator {
        private static ProfilerMarker ProfileMarker_GenCB = new("Gen CB");
        private static ProfilerMarker ProfileMarker_ReqCB = new("Req CB");
        public struct Value {
            public ushort OutputOffset;	// Offset in the output data array
            public ushort ValueOffset;  // Offset within the material
            public ushort DataSize;       // How big the data type is
            public sbyte SourceId;      // Which material it comes from
            public byte ItemId;      // Which material it comes from
        }
        internal MaterialPropertyBlock[] sources;
        internal Value[] values;
        internal byte[] parameters;
        internal byte valueCount = 255;
        internal ushort mDataSize = 0;

        public bool IsValid() {
			return valueCount != 255;
        }
        public Span<MaterialPropertyBlock> GetSources() { return sources; }
        public Span<byte> GetParameters() { return parameters; }
        public Span<Value> GetAllValues() { return values; }
        public Span<Value> GetValueArray() { return values.AsSpan().Slice(0, valueCount); }
        public Span<Value> GetComputedValueArray() { return values.AsSpan().Slice(valueCount); }
        public void Evaluate(Span<byte> data) {
			var sources = GetSources();
			Debug.Assert(data.Length >= mDataSize);
			foreach (var value in GetValueArray()) {
				var srcData = sources[value.SourceId].GetParametersRaw().GetDataRaw();
				srcData.Slice(value.ValueOffset, Math.Min(value.DataSize, srcData.Length)).CopyTo(data.Slice(value.OutputOffset));
                // TODO: Fill overflow with 0
			}
			var context = new MaterialEvaluatorContext(this, 0, data);
			foreach (var value in GetComputedValueArray()) {
                var parMaterial = (Material)sources[value.SourceId];
                var par = parMaterial.GetComputedByIndex(value.ValueOffset);
				par.EvaluateValue(data.Slice(value.OutputOffset, value.DataSize), ref context);
			}
		}
        [SkipLocalsInit]
        public void EvaluateSafe(Span<byte> data) {
            if (data.Length >= mDataSize) {
                Evaluate(data);
            } else {
                Span<byte> tmpData = stackalloc byte[mDataSize];
                Evaluate(tmpData);
                tmpData.Slice(0, data.Length).CopyTo(data);
            }
		}
		unsafe static void ResolveConstantBuffer(CSConstantBuffer cb, Span<Material> materialStack, Span<byte> buffer) {
            using var collector = Material.BeginGetUniforms(materialStack);
            foreach (var val in cb.GetValues()) {
				var data = collector.GetUniform(val.mName);
                if (val.mType == "half") {
                    var floats = MemoryMarshal.Cast<byte, float>(data);
                    for (int r = 0; r < val.mRows; r++) {
                        var halfArr = MemoryMarshal.Cast<byte, Half>(buffer.Slice(val.mOffset + 16 * r));
                        for (int i = 0; i < val.mColumns; ++i) halfArr[i] = floats[i + r * val.mColumns];
                    }
                } else {
                    if (data.Length > val.mSize)
                        data = data.Slice(0, val.mSize);
                    data.CopyTo(buffer.Slice(val.mOffset));
                }
            }
		}
        public static MemoryBlock<nint> ResolveResources(CSGraphics graphics, CSPipeline pipeline, List<Material> materialStack) {
            return ResolveResources(graphics, pipeline, CollectionsMarshal.AsSpan(materialStack));
        }
		public static MemoryBlock<nint> ResolveResources(CSGraphics graphics, CSPipeline pipeline, Span<Material> materialStack) {
            int resCount = pipeline.GetConstantBufferCount() + pipeline.GetResourceCount() * 2;
            if (pipeline.GetHasStencilState()) ++resCount;
            var resources = graphics.RequireFrameData<nint>(resCount);
			ResolveResources(graphics, pipeline, materialStack, resources);
			return resources;
		}
        [SkipLocalsInit]
        unsafe public static void ResolveResources(CSGraphics graphics, CSPipeline pipeline, Span<Material> materialStack, Span<nint> outResources) {
			int r = 0;
            if (pipeline.GetHasStencilState()) {
                var realtimeState = ResolveRealtimeState(materialStack);
                outResources[r++] = realtimeState.StencilRef;
            }
			// Get constant buffer data for this batch
			foreach (var cb in pipeline.GetConstantBuffers()) {
                byte* tmpDataPtr = stackalloc byte[cb.mSize];
                var tmpData = new MemoryBlock<byte>(tmpDataPtr, cb.mSize);
                using (var marker = ProfileMarker_GenCB.Auto()) {
                    ResolveConstantBuffer(cb, materialStack, tmpData);
                }
                var hash = tmpData.GenerateDataHash();
                using (var marker = ProfileMarker_ReqCB.Auto()) {
                    outResources[r++] = graphics.RequireConstantBuffer(tmpData, hash);
                }
			}
			// Get other resource data for this batch
			{
                var resourceData = MemoryMarshal.Cast<nint, CSBufferReference>(outResources.Slice(r));
                r = 0;
                foreach (var rb in pipeline.GetResources()) {
                    resourceData[r++] = Material.GetUniformBuffer(rb.mName, materialStack);
				}
			}
		}
        private static void MergePipelineState(ref Material.StateData state, Material mat) {
            state.MergeWith(mat.State);
            if (mat.InheritParameters != null) {
                foreach (var inherit in mat.InheritParameters) {
                    MergePipelineState(ref state, inherit);
                }
            }
        }
        private static void MergeRealtimeState(ref Material.RealtimeStateData state, Material mat) {
            state.MergeWith(mat.RealtimeData);
            if (mat.InheritParameters != null) {
                foreach (var inherit in mat.InheritParameters) {
                    MergeRealtimeState(ref state, inherit);
                }
            }
        }
        public static Material.StateData ResolvePipelineState(Span<Material> materials) {
            Material.StateData r = default;
            foreach (var mat in materials) MergePipelineState(ref r, mat);
            r.MergeWith(Material.StateData.Default);
            return r;
        }
        public static Material.RealtimeStateData ResolveRealtimeState(Span<Material> materials) {
            Material.RealtimeStateData r = default;
            foreach (var mat in materials) MergeRealtimeState(ref r, mat);
            r.MergeWith(Material.RealtimeStateData.Default);
            return r;
        }
        public static Span<KeyValuePair<CSIdentifier, CSIdentifier>> ResolveMacros(Span<KeyValuePair<CSIdentifier, CSIdentifier>> macros, Span<Material> materials) {
            ulong keyMask = 0;
            int count = 0;
            void AppendRecursive(Material material, Span<KeyValuePair<CSIdentifier, CSIdentifier>> macros) {
                if (material.GetMacrosRaw() is { } matMacros) {
                    foreach (var item in matMacros) {
                        var oldKeyMask = keyMask;
                        keyMask |= 1ul << (item.Key.mId & 63);
                        if (oldKeyMask == keyMask) {
                            // Item may already be added, search for it
                            int i = 0;
                            for (; i < count; ++i) if (macros[i].Key == item.Key) break;
                            if (i < count) continue;
                        }
                        macros[count++] = item;
                    }
                }
                if (material.InheritParameters != null) {
                    foreach (var inherit in material.InheritParameters) AppendRecursive(inherit, macros);
                }
            }
            foreach (var mat in materials) AppendRecursive(mat, macros);
            return macros.Slice(0, count);
        }

        unsafe public static CSPipeline ResolvePipeline(CSGraphics graphics, List<CSBufferLayout> pbuffLayout, List<Material> materials) {
            return ResolvePipeline(graphics, CollectionsMarshal.AsSpan(pbuffLayout), CollectionsMarshal.AsSpan(materials));
        }
        [SkipLocalsInit]
        unsafe public static CSPipeline ResolvePipeline(CSGraphics graphics, Span<CSBufferLayout> pbuffLayout, Span<Material> materials) {
            Span<KeyValuePair<CSIdentifier, CSIdentifier>> macros = stackalloc KeyValuePair<CSIdentifier, CSIdentifier>[32];
            macros = MaterialEvaluator.ResolveMacros(macros, materials);
            var materialState = ResolvePipelineState(materials);

            JobHandle meshHandle = default, pixelHandle = default, vertexHandle = default;

            var meshShader = materialState.MeshShader == null || !graphics.GetCapabiltiies().mMeshShaders || true ? null :
                Resources.RequireShader(graphics, materialState.MeshShader, "ms_6_5", macros, materialState.RenderPass, out meshHandle);
            if (meshShader != null) {
                var pixShader = materialState.PixelShader == null ? null :
                    Resources.RequireShader(graphics, materialState.PixelShader, "ps_6_5", macros, materialState.RenderPass, out pixelHandle);
                meshHandle.Join(pixelHandle).Complete();
                return graphics.RequireMeshPipeline(pbuffLayout,
                    meshShader.NativeShader,
                    pixShader.NativeShader,
                    &materialState.BlendMode
                );
            } else {
                var pixShader = materialState.PixelShader == null ? null :
                    Resources.RequireShader(graphics, materialState.PixelShader, "ps_6_5", macros, materialState.RenderPass, out pixelHandle);
                var vertShader = materialState.VertexShader == null ? null :
                    Resources.RequireShader(graphics, materialState.VertexShader, "vs_6_5", macros, materialState.RenderPass, out vertexHandle);
                pixelHandle.Join(vertexHandle).Complete();
                return graphics.RequirePipeline(pbuffLayout,
                    vertShader.NativeShader,
                    pixShader.NativeShader,
                    &materialState.BlendMode
                );
            }
        }
    }
    public ref struct MaterialCollectorContext {
        public readonly MaterialCollector Collector;
        public readonly Span<MaterialPropertyBlock> Properties;
        public MaterialCollectorContext(MaterialCollectorStacked collector) : this(collector, CollectionsMarshal.AsSpan(collector.PropertyBlocks)) { }
        public MaterialCollectorContext(MaterialCollector collector, Span<MaterialPropertyBlock> properties) {
            Collector = collector;
            Properties = properties;
        }
        public void Dispose() { Collector.Clear(); }
        public Span<byte> GetUniform(CSIdentifier name) {
            foreach (var properties in Properties) {
                var data = Collector.GetUniformSource(properties, name, ref this);
                if (!data.IsEmpty) return data;
            }
            return Collector.GetUniformSourceNull(name, this);
        }
        public static MaterialCollectorContext Create(MaterialCollectorStacked collector, Span<Material> materials) {
            return new(collector, collector.SetMaterials(materials));
        }
        public static MaterialCollectorContext Create(MaterialCollector collector, Span<MaterialPropertyBlock> properties) {
            return new(collector, properties);
        }
    }
    public class MaterialCollector {
        public const ushort InvalidOffset = 0xffff;
        public struct Value {
            public MaterialEvaluator.Value EvalValue;
            public sbyte SourceId => EvalValue.SourceId;
            public ushort OutputOffset => EvalValue.OutputOffset;
            public ushort ValueOffset => EvalValue.ValueOffset;
            public ushort DataSize => EvalValue.DataSize;
            public CSIdentifier Name;
            public sbyte ParamOffset;
            public sbyte ParamCount;
        }
        List<MaterialPropertyBlock> sources = new();
        List<Value> values = new();
        List<byte> parameterIds = new();
        List<byte> parameterStack = new();
        ArrayList<byte> outputData = new();
        int valueCount;
        int dataSize;
        public bool IsEmpty => valueCount == 0 && dataSize == 0;

        public void Clear() {
            valueCount = 0;
            sources.Clear();
            values.Clear();
            parameterIds.Clear();
            outputData.Clear();
        }
        unsafe public Span<byte> GetUniformSource(MaterialPropertyBlock properties, CSIdentifier name, scoped ref MaterialCollectorContext context) {
            for (int i = 0; i < values.Count; ++i) {
                var value = values[i];
                if (value.Name != name) continue;
                if (parameterStack.Count > 0) parameterIds.Add((byte)i);
                Span<byte> srcData = value.ParamOffset >= 0
                    ? outputData.AsSpan(value.OutputOffset, value.DataSize)
                    : sources[value.SourceId].GetParametersRaw().GetDataRaw().Slice(value.ValueOffset, value.DataSize);
                return srcData;
            }
            return GetUniformSourceIntl(properties, name, ref context);
        }
        // Ensure all values come before computed
        // Retain relative order of computed (not of value)
        public void FinalizeSources() {
            Debug.Assert(parameterStack.Count == 0);
            int max = (int)values.Count - 1;
            while (max >= 0 && values[max].ParamOffset >= 0) --max;
            int nxt = max;
            while (nxt >= 0) {
                while (nxt >= 0 && values[nxt].ParamOffset < 0) --nxt;
                if (nxt < 0) break;
                for (int p = 0; p < parameterIds.Count; ++p) {
                    var param = parameterIds[p];
                    if (param == nxt) parameterIds[p] = (byte)max;
                    else if (param == max) parameterIds[p] = (byte)nxt;
                }
                var t = values[nxt];
                values[nxt] = values[max];
                values[max] = t;
                --max;
                --nxt;
            }
            valueCount = max + 1;
        }
        public void FinalizeAndClearOutputOffsets() {
            FinalizeSources();
            for (int v = 0; v < values.Count; ++v) {
                var value = values[v];
                value.EvalValue.OutputOffset = InvalidOffset;
                values[v] = value;
            }
        }
        public Span<byte> GetUniformSourceNull(CSIdentifier name, MaterialCollectorContext context) {
            var material = Material.NullInstance;
            var parameters = material.GetParametersRaw();
            var itemIndex = parameters.GetItemIndex("NullMat");
            var valueIndex = ObserveValue(material, itemIndex);
            var value = values[valueIndex].EvalValue;
            return parameters.GetDataRaw().Slice(value.ValueOffset, value.DataSize);
        }
        public void SetItemOutputOffset(CSIdentifier name, int offset, int byteSize = -1) {
            for (int v = 0; v < values.Count; ++v) {
                var value = values[v];
                if (value.Name != name) continue;
                value.EvalValue.OutputOffset = (ushort)offset;
                if (byteSize >= 0) value.EvalValue.DataSize = (ushort)byteSize;
                values[v] = value;
                break;
            }
        }
        public void RepairOutputOffsets(bool allowCompacting = true) {
            if (allowCompacting) {
                for (int i = (int)values.Count - 1; i >= valueCount; --i) {
                    var value = values[i];
                    if (value.OutputOffset == InvalidOffset) continue;
                    var poff = value.ParamOffset;
                    var pcnt = value.ParamCount;
                    int bestId = -1;
                    for (int p = 0; p < pcnt; ++p) {
                        int parId = parameterIds[poff + p];
                        var other = values[parId];
                        if (other.OutputOffset != InvalidOffset) continue;
                        if (other.DataSize > value.DataSize) continue;
                        bestId = Math.Max(bestId, parId);
                    }
                    if (bestId >= 0) {
                        var bvalue = values[bestId];
                        bvalue.EvalValue.OutputOffset = bvalue.OutputOffset;
                        values[bestId] = bvalue;
                    }
                }
            }
            int maxAlloc = 0;
            foreach (var value in values) {
                if (value.OutputOffset != InvalidOffset) maxAlloc = Math.Max(maxAlloc, value.OutputOffset + value.DataSize);
            }
            for (int v = 0; v < values.Count; ++v) {
                if (values[v].OutputOffset != InvalidOffset) continue;
                var value = values[v];
                value.EvalValue.OutputOffset = (ushort)maxAlloc;
                maxAlloc += value.DataSize;
                values[v] = value;
            }
            dataSize = maxAlloc;
        }
        public ulong GenerateSourceHash() {
            ulong hash = 0;
            foreach (var source in sources) hash += (ulong)source.GetHashCode();
            return hash;
        }
        public ulong GenerateLayoutHash() {
            ulong hash = 0;
            foreach (var value in values) hash += (ulong)(((value.Name.mId << 16) ^ value.OutputOffset));
            return hash;
        }
        public void BuildEvaluator(MaterialEvaluator cache) {
            cache.sources = sources.ToArray();
            cache.values = new MaterialEvaluator.Value[values.Count];
            for (int v = 0; v < values.Count; ++v) cache.values[v] = values[v].EvalValue;
            cache.valueCount = (byte)valueCount;
            cache.parameters = parameterIds.ToArray();
            cache.mDataSize = (ushort)dataSize;
            Debug.Assert(cache.mDataSize > 0);
            Clear();
        }
	    // At this point, the parameter definitely does not yet exist in our list
	    unsafe Span<byte> GetUniformSourceIntl(MaterialPropertyBlock properties, CSIdentifier name, scoped ref MaterialCollectorContext context) {
            // Check if the value has been set explicitly
            var parameters = properties.GetParametersRaw();
            var dataIndex = parameters.GetItemIndex(name);
            if (dataIndex != -1) {
                var valueIndex = ObserveValue(properties, dataIndex);
                var value = values[valueIndex].EvalValue;
                return parameters.GetDataRaw().Slice(value.ValueOffset, value.DataSize);
            }
            if (properties is Material material) {
                var computedI = material.FindComputedIndex(name);
		        if (computedI != -1) {
			        BeginComputed(material, name);
			        ComputedParameterBase computed = material.GetComputedByIndex(computedI)!;
			        var outData = ConsumeTempData(computed.GetDataSize());
			        computed.SourceValue(outData, ref context);
			        EndComputed(material, computedI, outData);
			        return outData;
		        }
                // Check if it exists in inherited material properties
                if (material.InheritParameters != null) {
                    foreach (var mat in material.InheritParameters) {
                        var data = GetUniformSourceIntl(mat, name, ref context);
                        if (!data.IsEmpty) return data;
                    }
                }
            }
            return default;
	    }
	    int RequireSource(MaterialPropertyBlock properties) {
		    for (int i = 0; i < sources.Count; ++i) if (sources[i] == properties) return i;
		    //if (mSources.capacity() < 4) mSources.reserve(4);
		    int id = sources.Count;
            sources.Add(properties);
		    return id;
	    }
	    int ObserveValue(MaterialPropertyBlock properties, int itemId) {
            ref var item = ref properties.GetParametersRaw().GetItemsRaw()[itemId];
            var v = new Value() {
                EvalValue = new MaterialEvaluator.Value() {
		            OutputOffset = InvalidOffset,
                    ValueOffset = (ushort)item.ByteOffset,
		            DataSize = (ushort)(item.Count * Marshal.SizeOf(item.Type)),
		            SourceId = (sbyte)RequireSource(properties),
                    ItemId = (byte)itemId,
                },
                Name = item.Identifier,
		        ParamOffset = -1,
		        ParamCount = -1,
            };
		    values.Add(v);
		    if (parameterStack.Count != 0) parameterIds.Add((byte)(values.Count - 1));
            return values.Count - 1;

        }
	    Span<byte> ConsumeTempData(int dataSize) {
            return outputData.Consume(dataSize);
	    }
	    void BeginComputed(Material material, CSIdentifier name) {
		    //if (mParameterIds.capacity() < 8) mParameterIds.reserve(8);
		    parameterStack.Add((byte)parameterIds.Count);
	    }
	    void EndComputed(Material material, int parameterI, Span<byte> valueData) {
            var parameter = material.GetComputedByIndex(parameterI)!;
            int outputOffset = (int)Unsafe.ByteOffset(
                ref MemoryMarshal.GetReference(valueData),
                ref MemoryMarshal.GetReference(outputData.AsSpan())
            );
            var from = parameterStack[^1];
            parameterStack.RemoveAt(parameterStack.Count - 1);
            var v = new Value() {
                EvalValue = new MaterialEvaluator.Value() {
		            OutputOffset = (byte)(outputOffset),
		            ValueOffset = (ushort)(parameterI),
                    DataSize = (ushort)parameter.GetDataSize(),
                    SourceId = (sbyte)RequireSource(material),
                },
		        Name = parameter.GetName(),
                ParamOffset = (sbyte)from,
                ParamCount = (sbyte)(parameterIds.Count - from),
            };
		    values.Add(v);
		    if (parameterStack.Count != 0) parameterIds.Add((byte)(values.Count - 1));
	    }
    }

    public struct MaterialStack : IDisposable {
        private PooledList<Material> materialStack;
        public ref struct Scope {
            public ref MaterialStack Stack;
            public ulong Mask;
            public Scope(ref MaterialStack stack, ulong mask) {
                Stack = ref stack;
                Mask = mask;
            }
            public void Dispose() {
                Stack.Pop(Mask);
            }
        }
        public MaterialStack(Material? root) {
            materialStack = new();
            if (root != null) materialStack.Add(root);
        }
        public void Dispose() {
            materialStack.Dispose();
        }
        unsafe public Scope Push(Material mat) {
            return Push(new Span<Material>(ref mat));
        }
        unsafe public Scope Push(Span<Material> materials) {
            // TODO: Avoid excessive moves
            ulong mask = 0;
            int m = 0;
            foreach (var mat in materials) {
                if (mat == null) continue;
                mask |= 1ul << m;
                materialStack.Insert(m, mat);
            }
            return new Scope(ref this, mask);
        }
        public void Pop(ulong mask) {
            // TODO: Avoid excessive moves
            while (mask != 0) {
                int id = 63 - BitOperations.LeadingZeroCount(mask);
                mask &= ~(1ul << id);
                materialStack.RemoveAt(id);
            }
        }
        public Span<Material> GetMaterials() { return materialStack; }
        public static implicit operator Span<Material>(MaterialStack stack) { return stack.GetMaterials(); }
    }
    public class MaterialCollectorStacked : MaterialCollector {
        public List<MaterialPropertyBlock> PropertyBlocks = new();
        public Span<MaterialPropertyBlock> SetMaterials(Span<Material> materials) {
            PropertyBlocks.Clear();
            foreach (var mat in materials) {
                PropertyBlocks.Add(mat);
            }
            return CollectionsMarshal.AsSpan(PropertyBlocks);
        }
    }
}
