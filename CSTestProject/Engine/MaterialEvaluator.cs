using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
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
        public struct Value {
            public ushort OutputOffset;	// Offset in the output data array
            public ushort ValueOffset;  // Offset within the material
            public byte DataSize;       // How big the data type is
            public sbyte SourceId;      // Which material it comes from
        }
        internal Material[] sources;
        internal Value[] values;
        internal byte[] parameters;
        internal byte valueCount = 255;
        internal ushort mDataSize = 0;

        public bool IsValid() {
			return valueCount != 255;
        }
        public Span<Material> GetSources() { return sources; }
        public Span<byte> GetParameters() { return parameters; }
        public Span<Value> GetAllValues() { return values; }
        public Span<Value> GetValueArray() { return values.AsSpan().Slice(0, valueCount); }
        public Span<Value> GetComputedValueArray() { return values.AsSpan().Slice(valueCount); }
        public void Evaluate(Span<byte> data) {
			var sources = GetSources();
			Debug.Assert(data.Length >= mDataSize);
			foreach (var value in GetValueArray()) {
				var srcData = sources[value.SourceId].GetParametersRaw().GetDataRaw();
				srcData.Slice(value.ValueOffset, value.DataSize).CopyTo(data.Slice(value.OutputOffset));
			}
			var context = new MaterialEvaluatorContext(this, 0, data);
			foreach (var value in GetComputedValueArray()) {
				var par = sources[value.SourceId].GetComputedByIndex(value.ValueOffset);
				par.EvaluateValue(data.Slice(value.OutputOffset, value.DataSize), ref context);
			}
		}
		public void EvaluateSafe(Span<byte> data) {
			if (data.Length >= mDataSize) {
				Evaluate(data);
			}
			else {
				Span<byte> tmpData = stackalloc byte[mDataSize];
				Evaluate(tmpData);
				tmpData.Slice(0, data.Length).CopyTo(data);
			}
		}
		static void ResolveConstantBuffer(CSConstantBuffer cb, Span<Material> materialStack, Span<byte> buffer) {
			foreach (var val in cb.GetValues()) {
				var data = Material.GetUniformBinaryData(val.mName, materialStack);
                if (data.Length > val.mSize)
                    data = data.Slice(0, val.mSize);
                data.CopyTo(buffer.Slice(val.mOffset));
			}
		}
        public static MemoryBlock<nint> ResolveResources(CSGraphics graphics, CSPipeline pipeline, List<Material> materialStack) {
            return ResolveResources(graphics, pipeline, CollectionsMarshal.AsSpan(materialStack));
        }
		public static MemoryBlock<nint> ResolveResources(CSGraphics graphics, CSPipeline pipeline, Span<Material> materialStack) {
			var resources = graphics.RequireFrameData<nint>(pipeline.GetConstantBufferCount() + pipeline.GetResourceCount());
			ResolveResources(graphics, pipeline, materialStack, resources);
			return resources;
		}
		unsafe public static void ResolveResources(CSGraphics graphics, CSPipeline pipeline, Span<Material> materialStack, Span<nint> outResources) {
			int r = 0;
			// Get constant buffer data for this batch
			foreach (var cb in pipeline.GetConstantBuffers()) {
				Span<byte> tmpData = stackalloc byte[cb.mSize];
                ResolveConstantBuffer(cb, materialStack, tmpData);
				outResources[r++] = graphics.RequireConstantBuffer(tmpData.Slice(0, cb.mSize));
			}
			// Get other resource data for this batch
			{
				foreach (var rb in pipeline.GetResources()) {
					var data = Material.GetUniformTexture(rb.mName, materialStack);
					outResources[r++] = (nint)data.mTexture;
				}
			}
		}
        private static void MergeState(ref Material.StateData state, Material mat) {
            state.MergeWith(mat.State);
            foreach (var inherit in mat.InheritParameters) {
                MergeState(ref state, inherit);
            }
        }
        public static Material.StateData ResolveState(Span<Material> materials) {
            Material.StateData r = default;
            foreach (var mat in materials) MergeState(ref r, mat);
            r.MergeWith(Material.StateData.Default);
            return r;
        }
        public static int ResolveMacros(Span<KeyValuePair<CSIdentifier, CSIdentifier>> macros, Span<Material> materials) {
            int count = 0;
            foreach (var mat in materials) {
                ref var matmacros = ref mat.GetMacrosRaw();
                foreach (var itemKV in matmacros.GetItemsRaw()) {
                    int i = 0;
                    for (; i < count; ++i) {
                        if (macros[i].Key == itemKV.Key) break;
                    }
                    if (i >= count) {
                        macros[count] = new KeyValuePair<CSIdentifier, CSIdentifier>(
                            itemKV.Key,
                            MemoryMarshal.Cast<byte, CSIdentifier>(matmacros.GetItemData(itemKV.Value))[0]
                        );
                        ++count;
                    }
                }
            }
            return count;
        }

        unsafe public static CSPipeline ResolvePipeline(CSGraphics graphics, List<CSBufferLayout> pbuffLayout, List<Material> materials) {
            return ResolvePipeline(graphics, CollectionsMarshal.AsSpan(pbuffLayout), CollectionsMarshal.AsSpan(materials));
        }
        unsafe public static CSPipeline ResolvePipeline(CSGraphics graphics, Span<CSBufferLayout> pbuffLayout, Span<Material> materials) {
            var macros = stackalloc KeyValuePair<CSIdentifier, CSIdentifier>[32];
            int count = MaterialEvaluator.ResolveMacros(new Span<KeyValuePair<CSIdentifier, CSIdentifier>>(macros, 32), materials);
            var materialState = ResolveState(materials);
            var pipeline = graphics.RequirePipeline(pbuffLayout,
                materialState.VertexShader, materialState.PixelShader,
                &materialState.BlendMode,
                new Span<KeyValuePair<CSIdentifier, CSIdentifier>>(macros, count),
                materialState.RenderPass
            );
            return pipeline;
        }
    }
    public ref struct MaterialCollectorContext {
        public readonly MaterialCollector Collector;
        public readonly Span<Material> Materials;
        public MaterialCollectorContext(Span<Material> materials, MaterialCollector collector) {
            Materials = materials;
            Collector = collector;
        }

        public Span<byte> GetUniformSource(CSIdentifier name) {
            foreach (var mat in Materials) {
                var data = Collector.GetUniformSource(mat, name, ref this);
                if (!data.IsEmpty) return data;
            }
            return Collector.GetUniformSourceNull(name, this);
        }
    }
    public class MaterialCollector {
        public const ushort InvalidOffset = 0xffff;
        public struct Value {
            public MaterialEvaluator.Value EvalValue;
            public sbyte SourceId => EvalValue.SourceId;
            public ushort OutputOffset => EvalValue.OutputOffset;
            public ushort ValueOffset => EvalValue.ValueOffset;
            public byte DataSize => EvalValue.DataSize;
            public CSIdentifier Name;
            public sbyte ParamOffset;
            public sbyte ParamCount;
        }
        List<Material> sources = new();
        List<Value> values = new();
        List<byte> parameterIds = new();
        List<byte> parameterStack = new();
        ArrayList<byte> outputData = new();
        int valueCount;
        int dataSize;

        public void Clear() {
            valueCount = 0;
            sources.Clear();
            values.Clear();
            parameterIds.Clear();
            outputData.Clear();
        }
        unsafe public Span<byte> GetUniformSource(Material material, CSIdentifier name, scoped ref MaterialCollectorContext context) {
            for (int i = 0; i < values.Count; ++i) {
                var value = values[i];
                if (value.Name != name) continue;
                if (parameterStack.Count > 0) parameterIds.Add((byte)i);
                Span<byte> srcData = value.ParamOffset >= 0
                    ? outputData.AsSpan(value.OutputOffset, value.DataSize)
                    : sources[value.SourceId].GetParametersRaw().GetDataRaw().Slice(value.ValueOffset, value.DataSize);
                return srcData;
            }
            return GetUniformSourceIntl(material, name, ref context);
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
            var valueData = material.GetParametersRaw().GetValueData("NullMat");
            ObserveValue(material, name, valueData);
            return valueData;
        }
        public void SetItemOutputOffset(CSIdentifier name, int offset, int byteSize = -1) {
            for (int v = 0; v < values.Count; ++v) {
                var value = values[v];
                if (value.Name != name) continue;
                value.EvalValue.OutputOffset = (ushort)offset;
                if (byteSize >= 0) value.EvalValue.DataSize = (byte)byteSize;
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
            Clear();
        }
	    // At this point, the parameter definitely does not yet exist in our list
	    unsafe Span<byte> GetUniformSourceIntl(Material material, CSIdentifier name, scoped ref MaterialCollectorContext context) {
            var computedI = material.FindComputedIndex(name);
		    if (computedI != -1) {
			    BeginComputed(material, name);
			    ComputedParameterBase computed = material.GetComputedByIndex(computedI)!;
			    var outData = ConsumeTempData(computed.GetDataSize());
			    computed.SourceValue(outData, ref context);
			    EndComputed(material, computedI, outData);
			    return outData;
		    }
		    // Check if the value has been set explicitly
		    var data = material.GetParametersRaw().GetValueData(name);
		    if (!data.IsEmpty) {
			    ObserveValue(material, name);
			    return data;
		    }
		    // Check if it exists in inherited material properties
		    foreach (var mat in material.InheritParameters) {
			    data = GetUniformSourceIntl(mat, name, ref context);
			    if (!data.IsEmpty) return data;
		    }
		    return data;
	    }
	    int RequireSource(Material material) {
		    for (int i = 0; i < sources.Count; ++i) if (sources[i] == material) return i;
		    //if (mSources.capacity() < 4) mSources.reserve(4);
		    int id = sources.Count;
            sources.Add(material);
		    return id;
	    }
	    void ObserveValue(Material material, CSIdentifier name) {
		    //if (mValues.capacity() < 8) mValues.reserve(8);
		    var valueData = material.GetParametersRaw().GetValueData(name);
		    ObserveValue(material, name, valueData);
	    }
	    void ObserveValue(Material material, CSIdentifier name, Span<byte> valueData) {
            int dataOffset = (int)Unsafe.ByteOffset(
                ref MemoryMarshal.GetReference(material.GetParametersRaw().GetDataRaw()),
                ref MemoryMarshal.GetReference(valueData)
            );
            var v = new Value() {
                EvalValue = new MaterialEvaluator.Value() {
		            OutputOffset = InvalidOffset,
                    ValueOffset = (ushort)dataOffset,
		            DataSize = (byte)(valueData.Length),
		            SourceId = (sbyte)RequireSource(material),
                },
                Name = name,
		        ParamOffset = -1,
		        ParamCount = -1,
            };
		    values.Add(v);
		    if (parameterStack.Count != 0) parameterIds.Add((byte)(values.Count - 1));
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
                    DataSize = (byte)parameter.GetDataSize(),
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
}
