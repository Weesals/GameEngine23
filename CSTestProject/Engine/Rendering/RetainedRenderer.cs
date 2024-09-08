using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Weesals.ECS;
using Weesals.Engine.Jobs;
using Weesals.Engine.Profiling;
using Weesals.Geometry;
using Weesals.Utility;

namespace Weesals.Engine {
    public struct RetainedMaterialSet {
        // Set of materials not including render pass override
        public Material[] mMaterials;
        public int mReferenceCount = 0;
        public RetainedMaterialSet(Span<Material> materials) {
            mMaterials = materials.ToArray();
        }
    }
    public class RetainedMaterialCollection {
        SparseArray<RetainedMaterialSet> mMaterialSets = new();
        Dictionary<ulong, int> mSetIDByHash = new();
        public Span<Material> GetMaterials(int id) { return mMaterialSets[id].mMaterials; }
        public void AddRef(int id, int count = 1) { mMaterialSets[id].mReferenceCount += count; }
        public void DeRef(int id, int count = 1) { if ((mMaterialSets[id].mReferenceCount -= count) == 0) Remove(id); }
        public void Remove(int id) {
            ulong hash = ArrayHash(GetMaterials(id));
            mMaterialSets.Return(id);
            mSetIDByHash.Remove(hash);
        }
        public int Require(Span<Material> materials) {
            ulong hash = ArrayHash(materials);
            if (!mSetIDByHash.TryGetValue(hash, out var id)) {
                id = mMaterialSets.Add(new RetainedMaterialSet(materials));
                mSetIDByHash.Add(hash, id);
            }
            return id;
        }
        public static ulong ArrayHash(Span<Material> materials) {
            ulong hash = 0;
            foreach (var mat in materials) {
                if (mat == null) continue;
                hash = hash * 0x9E3779B97F4A7C15uL + (ulong)mat.GetIdentifier();
            }
            return hash;
        }
    }
    public class ResolvedMaterialSets {
        RetainedMaterialCollection mMatCollection;
        public struct ResolvedMaterialSet {
            public MaterialEvaluator mEvaluator;
            public ulong mSourceHash;
            public int mMaterialSet;
        };
        public ResolvedMaterialSets(RetainedMaterialCollection matCollection) {
            mMatCollection = matCollection;
        }
        protected Dictionary<ulong, int> mResolvedByHash = new();
        protected ArrayList<ResolvedMaterialSet> mResolved = new();
        protected MaterialCollector mMaterialCollector = new();
        unsafe protected ulong GenerateHash(CSGraphics cmdBuffer, ulong valueHash, int matSetId) {
            ulong hash = (ulong)cmdBuffer.GetNativeGraphics();
            // TODO: Find subregions of CBs
            hash = hash * 0x9E3779B97F4A7C15uL + valueHash;
            hash = hash * 0x9E3779B97F4A7C15uL + (ulong)matSetId;
            return hash;
        }
        public int RequireResolved(CSGraphics graphics, Span<CSUniformValue> values, int matSetId) {
            ulong valueHash = 0;
            foreach (var value in values) valueHash += (ulong)value.GetHashCode() * 1234567;
            ulong hash = valueHash + (ulong)graphics.GetHashCode() + (ulong)matSetId * 0x9E3779B97F4A7C15uL;
            if (!mResolvedByHash.TryGetValue(hash, out var item)) {
                mResolvedByHash.Add(hash, item = mResolved.Count);
                mResolved.Add(new ResolvedMaterialSet() { mMaterialSet = matSetId });
            }
            ref var resolved = ref mResolved[item];
            if (resolved.mEvaluator == null) {
                mMaterialCollector.Clear();
                var materials = mMatCollection.GetMaterials(matSetId);
                var context = new MaterialCollectorContext(materials, mMaterialCollector);
                for (int v = 0; v < values.Length; ++v)
                    context.GetUniformSource(values[v].mName);

                // Force the correct output layout
                mMaterialCollector.FinalizeAndClearOutputOffsets();
                for (int v = 0; v < values.Length; ++v)
                    mMaterialCollector.SetItemOutputOffset(values[v].mName, values[v].mOffset, values[v].mSize);
                mMaterialCollector.RepairOutputOffsets();

                // Add the resource evaluator
                resolved.mEvaluator = new();
                mMaterialCollector.BuildEvaluator(resolved.mEvaluator);

                resolved.mSourceHash = mMaterialCollector.GenerateSourceHash() * 0x9E3779B97F4A7C15uL + valueHash;
            }
            return item;
        }
        public ref ResolvedMaterialSet GetResolved(int id) {
            return ref mResolved[id];
        }
    }

    public struct RenderTag {
        public readonly int Id;
        public RenderTag(int id) { Id = id; }
        public static RenderTag Default = new RenderTag(0);
        public static RenderTag Transparent = new RenderTag(1);
        public static RenderTag ShadowCast = new RenderTag(2);
        public static RenderTags operator |(RenderTag t1, RenderTag t2) { RenderTags t = new(); t.Add(t1); t.Add(t2); return t; }
    }
    public struct RenderTags {
        public uint Mask;
        public RenderTags(uint mask = 0) { Mask = mask; }
        public void Add(RenderTag tag) { Mask |= 1u << tag.Id; }
        public void Remove(RenderTag tag) { Mask &= ~(1u << tag.Id); }
        public void Clear() { Mask = 0; }
        public bool Has(RenderTag tag) { return (Mask & (1u << tag.Id)) != 0; }
        public bool HasAny(RenderTags tags) { return (Mask & tags.Mask) != 0; }
        public static implicit operator RenderTags(RenderTag tag) { var tags = new RenderTags(); tags.Add(tag); return tags; }
        public static RenderTags operator |(RenderTags tags, RenderTag tag) { tags.Add(tag); return tags; }
        public static RenderTags operator &(RenderTags tags, RenderTags tag) { tags.Mask &= tag.Mask; return tags; }
        public static RenderTags Default = new RenderTags(0b01);
        public static RenderTags None = new RenderTags(0b00);
    }
    public class RenderTagManager {
        private List<CSIdentifier> identifiers = new();
        public RenderTagManager() {
            identifiers.Add("Default");
            identifiers.Add("Transparent");
            identifiers.Add("ShadowCast");
        }
        public RenderTag RequireTag(CSIdentifier identifier) {
            var id = identifiers.IndexOf(identifier);
            if (id == -1) { id = identifiers.Count; identifiers.Add(identifier); }
            return new RenderTag(id);
        }
    }
    public partial struct SceneInstance : IEquatable<SceneInstance> {
        private int mInstanceId;
        public SceneInstance(int instanceId) { mInstanceId = instanceId; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetInstanceId() => mInstanceId;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator int(SceneInstance instance) { return instance.mInstanceId; }
        public override string ToString() { return GetInstanceId().ToString(); }
        public bool Equals(SceneInstance other) { return mInstanceId == other.mInstanceId; }
    }
    unsafe public class Scene {
        private static ProfilerMarker ProfileMarker_SubmitToGPU = new("SubmitToGPU");

        const uint DefaultListenerMask = 1;     // Why is this 1?
        public struct Instance {
            public RangeInt Data;
        }
        private BufferLayoutPersistent gpuScene;
        private Vector4* gpuSceneDataCache;
        private SparseIndices gpuSceneFree = new();
        private SparseIndices gpuSceneDirty = new();
        private SparseArray<Instance> instances = new();
        private uint[] instanceListeners = Array.Empty<uint>();
        private ulong[] instanceVisibility = Array.Empty<ulong>();
        private BoundingBox[] instanceBounds = Array.Empty<BoundingBox>();
        private uint listenerMask = DefaultListenerMask;
        private uint listenerFlags = 0;
        private BoundingBox activeBounds = BoundingBox.Invalid;

        public RootMaterial RootMaterial = new();
        public RetainedMaterialCollection MaterialCollection = new();
        public ResolvedMaterialSets ResolvedMaterials;
        public RenderTagManager TagManager = new();
        private HashSet<SceneInstance> movedInstances = new();

        public bool DrawSceneBVH;

        public Scene() {
            ResolvedMaterials = new(MaterialCollection);
            gpuScene = new(BufferLayoutPersistent.Usages.Instance);
            gpuScene.AppendElement(new CSBufferElement("Data", BufferFormat.FORMAT_R32G32B32A32_FLOAT));
        }

        public int AllocateChangeListener() {
            int id = BitOperations.TrailingZeroCount(~listenerMask);
            listenerMask |= 1u << id;
            return id;
        }
        public void ReturnChangeListener(int id) {
            Debug.Assert((listenerMask & (1u << id)) != 0, "Listener was not registered");
            listenerMask &= ~(1u << id);
        }
        public void ClearListener(int id) {
            listenerFlags &= 1u << id;
        }
        // Allows instances to notify passes when their data has changed (and the pass needs reevaluation)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MarkListener(int id, int index) => MarkListener(1u << id, index);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MarkListener(uint id, int index) => instanceListeners[index] |= id;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool GetListenerState(int id) {
            return (listenerFlags & (1u << id)) != 0;
        }

        public SceneInstance CreateInstance() {
            return CreateInstance(new BoundingBox(Vector3.Zero, new Vector3(10f)));
        }
        public SceneInstance CreateInstance(BoundingBox bounds) {
            int size = 10;
            var start = gpuSceneFree.Take(size);
            if (start == -1) {
                start = gpuScene.BufferLayout.mCount;
                var range = new RangeInt(start, size);
                if (range.End > gpuScene.BufferCapacityCount) {
                    int resize = (int)BitOperations.RoundUpToPowerOf2((ulong)range.End + 1500);
                    gpuScene.AllocResize(resize);
                    gpuSceneDataCache = (Vector4*)gpuScene.Elements[0].mData;
                    new TypedBufferView<Vector4>(gpuScene.Elements[0], RangeInt.FromBeginEnd(range.Start, resize))
                        .Set(Vector4.Zero);
                }
                gpuScene.BufferLayout.mCount += size;
            }
            var id = instances.Add(new Instance() { Data = new(start, size), });
            if (id >= instanceListeners.Length) Array.Resize(ref instanceListeners, instances.Items.Length);
            if (id >= instanceVisibility.Length) Array.Resize(ref instanceVisibility, instances.Items.Length);
            if (id >= instanceBounds.Length) Array.Resize(ref instanceBounds, instances.Items.Length);
            instanceVisibility[id] = 1;
            instanceBounds[id] = bounds;
            var data = GetInstanceData(new SceneInstance(id));
            data.AsSpan().Fill(Vector4.Zero);
            ref var instanceData = ref instances[id];
            var stride = gpuScene.Elements[0].mBufferStride;
            gpuSceneDirty.Add(instanceData.Data.Start * stride, instanceData.Data.Length * stride);
            return new SceneInstance(id);
        }
        public unsafe bool UpdateInstanceData<T>(SceneInstance instance, int offset, T value) where T: unmanaged {
            return UpdateInstanceData(instance, offset, &value, sizeof(T));
        }
        public unsafe bool UpdateInstanceData(SceneInstance instance, int offset, void* data, int dataLen) {
            var stride = gpuScene.Elements[0].mBufferStride;
            ref var instanceData = ref instances[instance];
            var srcData = new Span<byte>((byte*)data, dataLen);
            var dstData = new Span<byte>((byte*)gpuScene.Elements[0].mData + stride * instanceData.Data.Start + offset, dataLen);
            if (srcData.SequenceEqual(dstData)) return false;
            srcData.CopyTo(dstData);
            gpuScene.BufferLayout.revision++;
            const uint RoundMask = 0xfffffff0;
            int offsetBegin = stride * instanceData.Data.Start + offset;
            int offsetEnd = offsetBegin + dataLen;
            offsetBegin = (int)((uint)offsetBegin & RoundMask);
            offsetEnd = (int)((uint)(offsetEnd + ~RoundMask) & RoundMask);
            gpuSceneDirty.Add(offsetBegin, offsetEnd - offsetBegin);
            //gpuSceneDirty.Add(stride * instanceData.Data.Start, stride * instanceData.Data.Length);
            listenerFlags |= instanceListeners[instance];
            instanceListeners[instance] = DefaultListenerMask;
            return true;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe MemoryBlock<Vector4> GetInstanceData(SceneInstance instance) {
            var instanceData = instances[instance];
            return new MemoryBlock<Vector4>(gpuSceneDataCache + instanceData.Data.Start, instanceData.Data.Length);
        }
        public void RemoveInstance(SceneInstance instance) {
            ref var instanceData = ref instances[instance];
            listenerFlags |= instanceListeners[instance];
            gpuSceneFree.Add(ref instanceData.Data);
            instances.Return(instance.GetInstanceId());
        }

        public CSBufferLayout GetGPUBuffer() { return gpuScene.BufferLayout; }
        public int GetGPURevision() { return gpuScene.Revision; }

        public BoundingBox GetActiveBounds() {
            return activeBounds;
        }

        public HashSet<SceneInstance> GetMovedInstances() => movedInstances;
        unsafe public void CommitMotion(SceneInstance instance) {
            var data = GetInstanceData(instance);
            Matrix4x4 mat = *(Matrix4x4*)data.Data;
            UpdateInstanceData(instance, sizeof(Matrix4x4), &mat, sizeof(Matrix4x4));
        }
        unsafe public void CommitMotion() {
            foreach (var instance in movedInstances) CommitMotion(instance);
            movedInstances.Clear();
        }

        unsafe public Matrix4x4 GetTransform(SceneInstance instance) {
            var data = GetInstanceData(instance);
            return *((Matrix4x4*)data.Data);
        }
        unsafe public void SetTransform(SceneInstance instance, Matrix4x4 mat) {
            if (!UpdateInstanceData(instance, 0, &mat, sizeof(Matrix4x4))) return;
            var instanceId = instance.GetInstanceId();
            var aabb = TransformBounds(mat, instanceBounds[instance.GetInstanceId()]);
            activeBounds.Min = Vector3.Min(activeBounds.Min, aabb.Min);
            activeBounds.Max = Vector3.Max(activeBounds.Max, aabb.Max);
            instanceVisibility[instanceId] = GenerateCellMask(aabb.Min, aabb.Max);
            if (instanceListeners[instanceId] != 0) {
                movedInstances.Add(instance);
            }
        }
        unsafe public BoundingBox GetInstanceAABB(SceneInstance instance) {
            return TransformBounds(*(Matrix4x4*)GetInstanceData(instance).Data, instanceBounds[instance]);
        }

        public static BoundingBox TransformBounds(Matrix4x4 mat, BoundingBox boundingBox) {
            var pos = Vector3.Transform(boundingBox.Centre, mat);
            var ext = boundingBox.Extents;
            var mx = Vector3.Abs(new Vector3(mat.M11, mat.M12, mat.M13));
            var my = Vector3.Abs(new Vector3(mat.M21, mat.M22, mat.M23));
            var mz = Vector3.Abs(new Vector3(mat.M31, mat.M32, mat.M33));
            ext = Vector3.Max(Vector3.Max(ext.X * mx, ext.Y * my), ext.Z * mz);
            var aabbMin = pos - ext;
            var aabbMax = pos + ext;
            return BoundingBox.FromMinMax(aabbMin, aabbMax);
        }

        unsafe public void SetHighlight(SceneInstance instance, Color color) {
            Vector4 col = color;
            UpdateInstanceData(instance, sizeof(Matrix4x4) * 2, &col, sizeof(Vector4));
        }

        public void SubmitToGPU(CSGraphics graphics) {
            if (gpuSceneDirty.Ranges.Count == 0) return;
            using var marker = ProfileMarker_SubmitToGPU.Auto();
            const int MaxRanges = 50;
            if (gpuSceneDirty.Ranges.Count > MaxRanges) {
                using var items = new PooledList<ValueTuple<int, int>>();
                for (int i = 1; i < gpuSceneDirty.Ranges.Count; i++) {
                    var range0 = gpuSceneDirty.Ranges[i - 1];
                    var range1 = gpuSceneDirty.Ranges[i];
                    items.Add(new ValueTuple<int, int>(range1.Start - range0.End, i));
                }
                items.AsSpan().Sort();
                items.RemoveRange(items.Count - MaxRanges, MaxRanges);
                for (int i = 0; i < items.Count; i++) {
                    ref var item = ref items[i];
                    item = new ValueTuple<int, int>(item.Item2, item.Item1);
                }
                items.AsSpan().Sort();
                var ranges = (List<RangeInt>)gpuSceneDirty.Ranges;
                for (var i = items.Count - 1; i >= 0; i--) {
                    var index = items[i].Item1;
                    var range0 = ranges[index - 1];
                    range0.End = ranges[index].End;
                    ranges.RemoveAt(index);
                    ranges[index - 1] = range0;
                }
            }
            graphics.CopyBufferData(GetGPUBuffer(), gpuSceneDirty.RangesAsSpan());
            gpuSceneDirty.Clear();
        }

        public ulong GenerateCellMask(Vector3 min, Vector3 max) {
            const float CellSize = 60.0f;
            const int BitsX = 8;
            const int BitsY = 1;
            const int BitsZ = 8;
            Int3 cellMin = Int3.FloorToInt(min / CellSize);
            Int3 cellMax = Int3.FloorToInt(max / CellSize);
            ulong cellMaskX = 0;
            for (int x = cellMin.X; x <= cellMax.X; ++x) cellMaskX |= 1ul << (x & (BitsX - 1));
            ulong cellMaskZ = 0;
            for (int z = cellMin.Z; z <= cellMax.Z; ++z) cellMaskZ |= cellMaskX << ((z & (BitsZ - 1)) * BitsX);
            ulong cellMask = 0;
            for (int y = cellMin.Y; y <= cellMax.Y; ++y) cellMask |= cellMaskZ << ((y & (BitsY - 1)) * (BitsX * BitsY));
            return cellMask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong GetInstanceVisibilityMask(SceneInstance instance) {
            return instanceVisibility[instance.GetInstanceId()];
        }
    }

    unsafe public class RenderQueue {
        private static ProfilerMarker ProfileMarker_SubmitDraw = new("SubmitDraw");
        private static ProfilerMarker ProfileMarker_AppendMesh = new("AppendMesh");
        struct DrawBatch {
            public string Name;
            public CSPipeline PipelineLayout;
            public MemoryBlock<CSBufferLayout> BufferLayouts;
            public MemoryBlock<nint> Resources;
            public int InstanceCount;
            public int RenderOrder;
            public override string ToString() { return Name; }
        }

        // Data which is erased each frame
        List<DrawBatch> drawBatches = new();
        int drawHash = 0;
        SparseIndices instanceDirty = new();

        public HashSet<CSTexture> UsedTextures = new();

        // Passes the typed instance buffer to a CommandList
        public BufferLayoutPersistent InstanceBufferLayout;
        public int InstanceCount => InstanceBufferLayout.Count;
        public int DrawHash => drawHash;

        public RenderQueue() {
            InstanceBufferLayout = new BufferLayoutPersistent(BufferLayoutPersistent.Usages.Instance);
            InstanceBufferLayout.AppendElement(
                new CSBufferElement("INSTANCE", BufferFormat.FORMAT_R32_UINT)
            );
            // Typecast to SceneInstance often
            Debug.Assert(sizeof(SceneInstance) == sizeof(int));
        }

        public void Clear() {
            // Clear previous data
            InstanceBufferLayout.Clear();
            drawBatches.Clear();
            UsedTextures.Clear();
        }
        public void AppendUsedTexture(CSTexture texture) {
            if (texture.IsValid) UsedTextures.Add(texture);
        }
        public void AppendUsedTextures(Span<CSBufferReference> resources) {
            for (int i = 0; i < resources.Length; i++) {
                if (resources[i].mType == CSBufferReference.BufferTypes.Texture) {
                    AppendUsedTexture(new CSTexture((NativeTexture*)resources[i].mBuffer));
                }
            }
        }
        public MemoryBlock<nint> RequireMaterialResources(CSGraphics graphics, CSPipeline pipeline, Material material) {
            return MaterialEvaluator.ResolveResources(graphics, pipeline, new Span<Material>(ref material));
        }
        unsafe public static MemoryBlock<CSBufferLayout> ImmortalizeBufferLayout(CSGraphics graphics, Span<CSBufferLayout> bindings) {
            // Copy buffer contents
            var renBufferLayouts = graphics.RequireFrameData<CSBufferLayout>(bindings.Length);
            for (int b = 0; b < renBufferLayouts.Length; ++b) {
                ref var buffer = ref renBufferLayouts[b];
                buffer = bindings[b];
                // Copy elements from each buffer
                buffer.mElements = graphics.RequireFrameData(buffer.GetElements()).Data;
            }
            return renBufferLayouts;
        }
        public int AppendMesh(string name, CSPipeline pipeline, MemoryBlock<CSBufferLayout> buffers, MemoryBlock<nint> resources, int instances = 1, int renderOrder = 0) {
            using var marker = ProfileMarker_AppendMesh.Auto();
            int hash = pipeline.GetHashCode();
            foreach (var buffer in buffers) hash = hash * 668265263 + buffer.GetHashCode();
            foreach (var resource in resources) hash = hash * 668265263 + resource.GetHashCode();
            return AppendMesh(name, pipeline, buffers, resources, instances, renderOrder, hash);
        }
        public int AppendMesh(string name, CSPipeline pipeline, MemoryBlock<CSBufferLayout> buffers, MemoryBlock<nint> resources, int instances, int renderOrder, int hash) {
            drawHash = drawHash * 668265263 + hash;
            int min = 0;
            int max = drawBatches.Count;
            while (min < max) {
                var mid = (min + max) / 2;
                var batch = drawBatches[mid];
                if (renderOrder >= batch.RenderOrder) min = mid + 1;
                else max = mid;
            }
            drawBatches.Insert(min, new DrawBatch() {
                Name = name,
                PipelineLayout = pipeline,
                BufferLayouts = buffers,
                Resources = resources,
                InstanceCount = instances,
            });
            return min;
        }

        unsafe public bool AppendInstance(SceneInstance instance) {
            if (InstanceCount >= InstanceBufferLayout.BufferCapacityCount) {
                InstanceBufferLayout.AllocResize(Math.Max(16, InstanceBufferLayout.BufferCapacityCount * 2));
            }
            var instIndex = InstanceBufferLayout.BufferLayout.mCount++;
            ref int instId = ref ((int*)InstanceBufferLayout.Elements[0].mData)[instIndex];
            if (instId == instance) return false;
            instId = instance;
            instanceDirty.Add(instIndex * sizeof(uint), sizeof(uint));
            return true;
        }
        public bool AppendInstances(Span<SceneInstance> instances) {
            if (InstanceCount + instances.Length > InstanceBufferLayout.BufferCapacityCount) {
                InstanceBufferLayout.AllocResize((int)BitOperations.RoundUpToPowerOf2((uint)(InstanceCount + instances.Length + 16)));
            }
            var instIndex = InstanceBufferLayout.BufferLayout.mCount;
            InstanceBufferLayout.BufferLayout.mCount += instances.Length;
            Debug.Assert(sizeof(SceneInstance) == sizeof(int));
            var instData = new Span<SceneInstance>((SceneInstance*)InstanceBufferLayout.Elements[0].mData + instIndex, instances.Length);
            if (instances.SequenceEqual(instData)) return false;
            instances.CopyTo(instData);
            instanceDirty.Add(instIndex * sizeof(uint), instances.Length * sizeof(uint));
            return true;
        }
        public ref struct Appender {
            public RenderQueue Queue;
            private Span<SceneInstance> instances;
            private int iterator = 0;
            private bool changes = false;
            public Appender(RenderQueue queue, int count) {
                Queue = queue;
                ref var layout = ref Queue.InstanceBufferLayout;
                if (Queue.InstanceCount + count > layout.BufferCapacityCount) {
                    layout.AllocResize((int)BitOperations.RoundUpToPowerOf2((uint)(Queue.InstanceCount + count + 16)));
                }
                instances = new Span<SceneInstance>((SceneInstance*)layout.Elements[0].mData + layout.BufferLayout.mCount, count);
            }
            public void Add(SceneInstance instance) {
                if (instances[iterator] != instance) {
                    instances[iterator] = instance;
                    changes = true;
                }
                ++iterator;
            }
            public void Dispose() {
                Debug.Assert(iterator == instances.Length);
                ref var layout = ref Queue.InstanceBufferLayout;
                var beginIndex = layout.BufferLayout.mCount;
                layout.BufferLayout.mCount += instances.Length;
                if (changes) {
                    Queue.instanceDirty.Add(beginIndex * sizeof(uint),
                        instances.Length * sizeof(uint));
                }
            }
        }

        public void Render(CSGraphics graphics) {
            using var marker = ProfileMarker_SubmitDraw.Auto();
            if (instanceDirty.Ranges.Count > 0) {
                graphics.CopyBufferData(InstanceBufferLayout, instanceDirty.RangesAsSpan());
                instanceDirty.Clear();
            }
            // Submit daw calls
            foreach (var draw in drawBatches) {
                // Dont need to update buffer, because data is held in Elements (by pointer)

                // Submit
                CSDrawConfig config = CSDrawConfig.Default;
                var pipeline = draw.PipelineLayout;
                graphics.Draw(
                    pipeline,
                    draw.BufferLayouts.AsCSSpan(),
                    draw.Resources.AsCSSpan(),
                    config,
                    draw.InstanceCount
                );
            }
        }
    }

    public class RetainedRenderer : IDisposable {

        private static ProfilerMarker ProfileMarker_SubmitToRQ = new ProfilerMarker("Submit To RQ");
        private static ProfilerMarker ProfileMarker_ComputeHull = new ProfilerMarker("Hull");
        private static ProfilerMarker ProfileMarker_FrustumCull = new ProfilerMarker("FrustumCull");
        private static ProfilerMarker ProfileMarker_SortInstances = new ProfilerMarker("Sort");
        private static ProfilerMarker ProfileMarker_AppendInstances = new ProfilerMarker("Append");
        private static ProfilerMarker ProfileMarker_FrustumJobCull = new ProfilerMarker("FrustumCullJob");
        private static ProfilerMarker ProfileMarker_Batches = new ProfilerMarker("Compute Batches");
        private static ProfilerMarker ProfileMarker_EvalBatches = new ProfilerMarker("Eval Batch");
        private static ProfilerMarker ProfileMarker_ComputeResources = new ProfilerMarker("ComputeResources");
        private static ProfilerMarker ProfileMarker_FrustumFast = new ProfilerMarker("Fast");
        private static ProfilerMarker ProfileMarker_FrustumSlow = new ProfilerMarker("Slow");

        public struct StateKey : IEquatable<StateKey>, IComparable<StateKey> {
            public int MeshHash;
            public int MaterialSet;
            public StateKey(int meshHash, int matSet) {
                MeshHash = meshHash;
                MaterialSet = matSet;
            }
            public bool Equals(StateKey o) {
                return MeshHash.Equals(MeshHash) && MaterialSet == o.MaterialSet;
            }
            public int CompareTo(StateKey o) {
                int compare = MeshHash.CompareTo(o.MeshHash);
                if (compare == 0) compare = MaterialSet - o.MaterialSet;
                return compare;
            }
            public override int GetHashCode() { return HashCode.Combine(MeshHash, MaterialSet); }
        }
        public class Batch {
            public StateKey StateKey;
            public Mesh Mesh;
            public int MaterialSet => StateKey.MaterialSet;
            public int LODBatch = -1;
            public Batch(Scene scene, Mesh mesh, StateKey stateKey) {
                Mesh = mesh;
                StateKey = stateKey;
            }
        }
        public class ResolvedPipeline {
            public CSPipeline Pipeline;
            public List<int> ResolvedCBs = new();
            public int ResolvedResources;
            public int TotalResourceCount;
        };
        public struct InstanceCache {
            public int BatchIndex;
            public override string ToString() => BatchIndex.ToString();
            public static readonly InstanceCache None = new() { BatchIndex = -1, };
        }

        public readonly Scene Scene;

        // All batches that currently exist
        private List<Batch> batches = new();
        private Dictionary<StateKey, int> batchesByState = new();
        private PooledHashMap<int, int> instanceBatches = new(32);
        // Stores cached PSOs per mesh/matset/graphics
        Dictionary<int, ResolvedPipeline> mPipelineCache = new();
        private int generation;
        public BoundingVolumeHierarchy BVH;

        // Passes the typed instance buffer to a CommandList
        BufferLayoutPersistent mInstanceBufferLayout;
        // Material to inject GPU Scene buffer as 'instanceData'
        private Material instanceMaterial;
        // Stores per-instance data
        // Listen for scene data changes
        private int changeListenerId;

        public RetainedRenderer(Scene scene) {
            Scene = scene;
            BVH = new(Scene);
            BVH.SetInstanceMetaType<InstanceCache>();
            instanceMaterial = new();
            mInstanceBufferLayout = new BufferLayoutPersistent(BufferLayoutPersistent.Usages.Instance);
            mInstanceBufferLayout.MarkInvalid();
            mInstanceBufferLayout.AppendElement(new CSBufferElement("INSTANCE", BufferFormat.FORMAT_R32_UINT));
            instanceMaterial.SetBuffer("instanceData", Scene.GetGPUBuffer());
            changeListenerId = Scene.AllocateChangeListener();
        }
        public void Dispose() {
            Scene.ReturnChangeListener(changeListenerId);
            mInstanceBufferLayout.Dispose();
            //instanceMaterial.Dispose();
        }

        public bool GetHasSceneChanges() {
            return Scene.GetListenerState(changeListenerId);
        }
        private int RequireBatchIndex(Mesh mesh, int matSetId) {
            var key = new StateKey((int)mesh.IndexBuffer.identifier, matSetId);
            if (batchesByState.TryGetValue(key, out var batchId)) {
                return batchId;
            }
            batchId = batches.Count;
            batches.Add(new Batch(Scene, mesh, key));
            batchesByState.Add(key, batchId);
            return batchId;
        }

        unsafe public Int2 GetPosition(SceneInstance instance) {
            var instanceData = Scene.GetInstanceData(instance);
            return GetPosition(*(Matrix4x4*)instanceData.Data);
        }
        public static Int2 GetPosition(in Matrix4x4 matrix) {
            return new Int2((int)matrix.Translation.X, (int)matrix.Translation.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Permute(int instance) {
            return instance ^ (instance >> 6);
        }

        // Add an instance to be drawn each frame
        unsafe public int AppendInstance(Mesh mesh, Span<Material> materials, SceneInstance instance) {
            using var mats = new PooledArray<Material>(materials, materials.Length + 1);
            mats[^1] = instanceMaterial;

            int matSetId = Scene.MaterialCollection.Require(mats);
            var batchIndex = RequireBatchIndex(mesh, matSetId);
            var batch = batches[batchIndex];
            var mut = BVH.Add(GetPosition(instance), instance);
            instanceBatches.Add(Permute(instance), batchIndex);
            BVH.GetInstanceMeta<InstanceCache>()[mut.NewIndex] = new InstanceCache() { BatchIndex = batchIndex, };
            return instance;
        }
        // Update the BVH
        public void MoveInstance(SceneInstance instance, Int2 oldPos, Int2 newPos) {
            Debug.Assert(instanceBatches.TryGetValue(Permute(instance), out _));
            if (BVH.Move(oldPos, newPos, instance, out var remMut, out var addMut)) {
            }
        }
        // Set visibility
        public void SetVisible(SceneInstance instance, bool visible) {
            throw new NotImplementedException();
            Debug.Assert(instanceBatches.TryGetValue(Permute(instance), out _));
            if (visible) {
                var mut = BVH.Add(GetPosition(instance), instance);
            } else {
                var mut = BVH.Remove(GetPosition(instance), instance);
            }
        }
        // Remove an instance from rendering
        public void RemoveInstance(SceneInstance instance) {
            Debug.Assert(instanceBatches.TryGetValue(Permute(instance), out _));
            var mut = BVH.Remove(GetPosition(instance), instance);
            instanceBatches.Remove(Permute(instance));
        }

        private ConvexHull hull = new();

        private RangeInt AppendInstances(RenderQueue queue, BoundingVolumeHierarchy vbh, in Frustum frustum) {
            var change = false;
            var instBegin = queue.InstanceCount;
            // Calculate visible instances
            using (var frustumMarker = ProfileMarker_FrustumCull.Auto()) {
                var en = BVH.CreateFrustumEnumerator(frustum);
                while (en.MoveNext()) {
                    using var frustMarker = (en.IsFullyInFrustum ? ProfileMarker_FrustumFast : ProfileMarker_FrustumSlow).Auto();
                    var instances = en.GetInstances();
                    if (Scene.DrawSceneBVH && !en.IsParentFullyInFrustum) {
                        var activeBounds = en.ActiveBoundingBox;
                        Gizmos.DrawWireCube(activeBounds.Centre, activeBounds.Size, en.IsFullyInFrustum ? Color.Blue : Color.Red);
                    }
                    if (en.IsFullyInFrustum) {
                        change |= queue.AppendInstances(instances);
                        foreach (var instance in instances) {
                            Scene.MarkListener(changeListenerId, instance);
                        }
                    } else {
                        BoundingBox bounds = BoundingBox.Invalid;
                        foreach (var instance in instances) {
                            var aabb = Scene.GetInstanceAABB(instance);
                            bounds = BoundingBox.Union(bounds, aabb);
                            if (!frustum.GetIsVisible(aabb)) continue;
                            change |= queue.AppendInstance(instance);
                            Scene.MarkListener(changeListenerId, instance);
                        }
                        en.OverwriteBoundingBox(bounds);
                    }
                }
            }
            return new(instBegin, queue.InstanceCount - instBegin);
        }
        unsafe private RangeInt AppendInstances(ref PooledList<int> queue, ref PooledList<int> batchIds, BoundingVolumeHierarchy bvh, in Frustum frustum) {
            var fwdNrmL = frustum.NearPlane.Normal.Length();
            var fwdNrm = frustum.NearPlane.Normal / fwdNrmL;
            var fwdPlaneLOD = -frustum.NearPlane.D / fwdNrmL + 400f;
            //var fwdPlaneCull = fwdPlaneLOD + 500f;

            var instBegin = queue.Count;
            // Calculate visible instances
            using (var frustumMarker = ProfileMarker_FrustumCull.Auto()) {
                var en = BVH.CreateFrustumEnumerator(frustum);
                while (en.MoveNext()) {
                    using var frustMarker = (en.IsFullyInFrustum ? ProfileMarker_FrustumFast : ProfileMarker_FrustumSlow).Auto();
                    var indexRange = en.GetInstanceRange();
                    if (Scene.DrawSceneBVH && !en.IsParentFullyInFrustum) {
                        var activeBounds = en.ActiveBoundingBox;
                        Gizmos.DrawWireCube(activeBounds.Centre, activeBounds.Size, en.IsFullyInFrustum ? Color.Blue : Color.Red);
                    }
                    /*var bounds = en.ActiveBoundingBox;
                    var nearest = new Vector3(
                        frustum.NearPlane.Normal.X < 0f ? bounds.Max.X : bounds.Min.X,
                        frustum.NearPlane.Normal.Y < 0f ? bounds.Max.Y : bounds.Min.Y,
                        frustum.NearPlane.Normal.Z < 0f ? bounds.Max.Z : bounds.Min.Z
                    );
                    var bbDp = Vector3.Dot(fwdNrm, nearest);
                    if (bbDp > fwdPlaneCull) continue;*/

                    int indexBegin = queue.Count;
                    if (en.IsFullyInFrustum) {
                        var range = queue.AddCount(indexRange.Length);
                        var delta = indexRange.Start - range.Start;
                        var end = range.End;
                        for (int i = range.Start; i < end; i++) {
                            queue[i] = i + delta;
                        }
                    } else {
                        var bounds = BoundingBox.Invalid;
                        foreach (var index in indexRange) {
                            var instance = en.GetInstance(index);
                            var aabb = Scene.GetInstanceAABB(instance);
                            bounds = BoundingBox.Union(bounds, aabb);
                            if (!frustum.GetIsVisible(aabb)) continue;
                            queue.Add(index);
                        }
                        en.OverwriteBoundingBox(bounds);
                    }
                    /*{
                        var instanceMeta = BVH.GetInstanceMeta<InstanceCache>();
                        batchIds.AddCount(queue.Count - indexBegin);
                        for (int i = indexBegin; i < queue.Count; i++) {
                            batchIds[i] = instanceMeta[queue[i]].BatchIndex;
                        }
                        if (bbDp > fwdPlaneLOD) {
                            for (int i = indexBegin; i < queue.Count; i++) {
                                var batchId = batchIds[i];
                                var lodBatch = batches[batchId].LODBatch;
                                if (lodBatch != -1) batchIds[i] = lodBatch;
                            }
                        } else {
                            for (int i = indexBegin; i < queue.Count; i++) {
                                var batchId = instanceMeta[queue[i]].BatchIndex;
                                var lodBatch = batches[batchId].LODBatch;
                                if (lodBatch >= 0) {
                                    var instanceData = Scene.GetInstanceData(BVH.GetInstance(queue[i]));
                                    var pos = *(Vector3*)&instanceData.Data[3];
                                    if (Vector3.Dot(fwdNrm, pos) > fwdPlaneLOD) {
                                        batchId = lodBatch;
                                    }
                                }
                                batchIds[i] = batchId;
                            }
                        }
                    }*/
                }
            }
            using (var frustumMarker = ProfileMarker_Batches.Auto()) {
                var instanceMeta = BVH.GetInstanceMeta<InstanceCache>();
                Trace.Assert(batchIds.AddCount(queue.Count - instBegin).Start == instBegin);
                var queueArr = queue.Data;
                var batchArr = batchIds.Data;
                var batchRange = RangeInt.FromBeginEnd(instBegin, queue.Count);
                JobHandle.ScheduleBatch((range) => {
                    using var marker = ProfileMarker_EvalBatches.Auto();
                    int end = range.End;
                    for (int i = range.Start; i < end; i++) {
                        var batchId = instanceMeta[queueArr[i]].BatchIndex;
                        var lodBatch = batches[batchId].LODBatch;
                        if (lodBatch >= 0) {
                            var instanceData = Scene.GetInstanceData(BVH.GetInstance(queueArr[i]));
                            var pos = *(Vector3*)&instanceData.Data[3];
                            if (Vector3.Dot(fwdNrm, pos) > fwdPlaneLOD) {
                                batchId = lodBatch;
                            }
                        }
                        batchArr[i] = batchId;
                    }
                }, batchRange).Complete();
            }
            return new(instBegin, queue.Count - instBegin);
        }

        private int frame = 0;
        // Generate a drawlist for rendering currently visible objects
        unsafe public void SubmitToRenderQueue(CSGraphics graphics, RenderQueue queue, in Frustum frustum) {
            using var marker = ProfileMarker_SubmitToRQ.Auto();
            Scene.ClearListener(changeListenerId);
            bool change = false;

            // Clear PSO cache if resources were reloaded
            if (Resources.Generation != generation) {
                mPipelineCache.Clear();
                generation = Resources.Generation;
            }

            // No longer using this acceleration
            /*ulong visMask;
            bool enableFrustum = true;
            {       // Generate a mask representing the cells intersecting the frustum
                using var hullMarker = ProfileMarker_ComputeHull.Auto();
                var activeRange = Scene.GetActiveBounds();
                if (activeRange.Extents.X <= 0) return;
                hull.FromBox(activeRange);
                enableFrustum = hull.Slice(frustum);
                var aabb = hull.GetAABB();
                if (aabb.Size.X <= 0) return;
                visMask = Scene.GenerateCellMask(aabb.Min, aabb.Max);
            }*/

            // Collect all visible instances
            var instOffset = queue.InstanceCount;
            var indices = new PooledList<int>(1 << 18);
            var batchIds = new PooledList<int>(1 << 18);
            AppendInstances(ref indices, ref batchIds, BVH, frustum);
            Span<int> instanceSortKeys = batchIds;// stackalloc int[indices.Count];
            /*for (int i = 0; i < indices.Count; i++) {
                instanceSortKeys[i] = batchIds[i];
            }*/
            // Sort them into batches
            using (var frustumSort = ProfileMarker_SortInstances.Auto()) {
                MemoryExtensions.Sort(instanceSortKeys, indices.AsSpan());
            }
            using (var frustumSort = ProfileMarker_SortInstances.Auto()) {
                MemoryExtensions.Sort(instanceSortKeys, indices.AsSpan());
            }
            // Preallocate buffer space
            using (var frustumSort = ProfileMarker_AppendInstances.Auto()) {
                new RenderQueue.Appender(queue, indices.Count);
                var rawInstances = BVH.RawInstances;
                var listenerMask = 1u << changeListenerId;
                // Push them into the buffer (64 batches to reduce overheads)
                for (int i = 0; i < indices.Count; i += 64) {
                    int count = Math.Min(64, indices.Count - i);
                    using (var appender = new RenderQueue.Appender(queue, count)) {
                        foreach (var index in indices.AsSpan(i, count)) {
                            var instance = rawInstances[index];
                            Scene.MarkListener(listenerMask, instance);
                            appender.Add(instance);
                        }
                    }
                }
                indices.Dispose();
            }

            for (int instIt = 0; instIt < instanceSortKeys.Length; ) {
                // Get the instance range for this batch
                var instBegin = instIt;
                var b = instanceSortKeys[instIt];
                for (; instIt < instanceSortKeys.Length; ++instIt) {
                    if (b != instanceSortKeys[instIt]) break;
                }
                int instCount = instOffset + instIt - instBegin;
                var batch = batches[b];
                var mesh = batch.Mesh;

                // Need to force this to use queues instance buffer
                // TODO: A more generic approach
                var bindings = graphics.RequireFrameData<CSBufferLayout>(3);
                bindings[0] = mesh.IndexBuffer;
                bindings[1] = mesh.VertexBuffer;
                bindings[^1] = queue.InstanceBufferLayout;
                bindings[^1].mOffset = instOffset + instBegin;
                bindings[^1].mCount = instCount;

                // Compute and cache CB and resource data
                var resolved = RequirePipeline(graphics, batch, bindings);

                var resources = graphics.RequireFrameData<nint>(resolved.TotalResourceCount);
                using (var resourceMarker = ProfileMarker_ComputeResources.Auto()) {
                    int r = 0;
                    // Get constant buffer data for this batch
                    for (int i = 0; i < resolved.ResolvedCBs.Count; ++i) {
                        var cbid = resolved.ResolvedCBs[i];
                        nint resource = 0;
                        if (cbid >= 0) {
                            ref var resolvedMat = ref Scene.ResolvedMaterials.GetResolved(cbid);
                            resource = RequireConstantBuffer(graphics, resolvedMat.mEvaluator);
                        }
                        resources[r++] = resource;
                    }
                    // Get other resource data for this batch
                    if (r > 0) {
                        ref var resCB = ref Scene.ResolvedMaterials.GetResolved(resolved.ResolvedResources);
                        resCB.mEvaluator.EvaluateSafe(resources.Slice(r).Reinterpret<byte>());
                        queue.AppendUsedTextures(resources.Slice(r).Reinterpret<CSBufferReference>());
                    }
                }

                // Add the draw command
                queue.AppendMesh(
                    mesh.Name,
                    resolved.Pipeline,
                    bindings,
                    resources,
                    instances: instCount,
                    renderOrder: 0,
                    // TODO: instCount may not capture all changes
                    hash: HashCode.Combine(mesh.Revision, instCount)
                );
            }
            batchIds.Dispose();
        }

        private unsafe ResolvedPipeline RequirePipeline(CSGraphics graphics, Batch batch, Span<CSBufferLayout> buffers) {
            var meshMatHash = HashCode.Combine(batch.StateKey.GetHashCode(), graphics.GetHashCode());
            if (!mPipelineCache.TryGetValue(meshMatHash, out var resolved)) {
                var materials = Scene.MaterialCollection.GetMaterials(batch.MaterialSet);
                var pso = MaterialEvaluator.ResolvePipeline(graphics, buffers, materials);
                resolved = new ResolvedPipeline() {
                    Pipeline = pso,
                };
                var cbuffers = pso.GetConstantBuffers();
                foreach (var cb in cbuffers) {
                    var resolvedId = Scene.ResolvedMaterials.RequireResolved(graphics, cb.GetValues(), batch.MaterialSet);
                    resolved.ResolvedCBs.Add(resolvedId);
                }
                var resources = pso.GetResources();
                if (resources.Length > 0) {
                    Span<CSUniformValue> tresources = stackalloc CSUniformValue[resources.Length];
                    for (int i = 0; i < resources.Length; ++i) {
                        var res = resources[i];
                        tresources[i] = new CSUniformValue {
                            mName = res.mName,
                            mOffset = (int)(i * sizeof(CSBufferReference)),
                            mSize = (int)sizeof(CSBufferReference),
                        };
                    }
                    resolved.ResolvedResources = Scene.ResolvedMaterials.RequireResolved(graphics, tresources, batch.MaterialSet);
                }
                resolved.TotalResourceCount = resolved.ResolvedCBs.Count + pso.GetResourceCount() * 2;
                mPipelineCache.Add(meshMatHash, resolved);
            }
            return resolved;
        }

        unsafe private nint RequireConstantBuffer(CSGraphics graphics, MaterialEvaluator evaluator) {
            var outdataPtr = stackalloc byte[evaluator.mDataSize];
            var outdata = new MemoryBlock<byte>(outdataPtr, evaluator.mDataSize);
            evaluator.Evaluate(outdata);
            return graphics.RequireConstantBuffer(outdata);
        }

        public void SetMeshLOD(Mesh mesh, Mesh hull, Span<Material> hullMaterials) {
            using var mats = new PooledArray<Material>(hullMaterials, hullMaterials.Length + 1);
            mats[^1] = instanceMaterial;

            int matSetId = Scene.MaterialCollection.Require(mats);
            var hullBatchIndex = RequireBatchIndex(hull, matSetId);

            foreach (var batch in batches) {
                if (batch.Mesh == mesh) batch.LODBatch = hullBatchIndex;
            }
        }
    }
}
