﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
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
                mResolved.Add(new ResolvedMaterialSet());
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
        public static RenderTags Default = new RenderTags(0b01);
        public static RenderTags None = new RenderTags(0b00);
    }
    public class RenderTagManager {
        private List<CSIdentifier> identifiers = new();
        public RenderTagManager() {
            identifiers.Add("Default");
            identifiers.Add("Transparent");
        }
        public RenderTag RequireTag(CSIdentifier identifier) {
            var id = identifiers.IndexOf(identifier);
            if (id == -1) { id = identifiers.Count; identifiers.Add(identifier); }
            return new RenderTag(id);
        }
    }
    public class Scene {
        private CSScene CSScene;

        public struct Instance {
            public RangeInt Data;
        }
        private BufferLayoutPersistent gpuScene;
        private SparseIndices gpuSceneFree = new();
        private SparseIndices gpuSceneUpdate = new();
        private SparseArray<Instance> instances = new();

        public RootMaterial RootMaterial = new();
        public RetainedMaterialCollection MaterialCollection = new();
        public ResolvedMaterialSets ResolvedMaterials;
        public RenderTagManager TagManager = new();
        private HashSet<CSInstance> movedInstances = new();
        private List<RangeInt> gpuDelta = new();
        public Scene(CSScene scene) {
            ResolvedMaterials = new(MaterialCollection);
            //CSScene = scene;
            gpuScene = new(BufferLayoutPersistent.Usages.Instance);
            gpuScene.AppendElement(new CSBufferElement("Data", BufferFormat.FORMAT_R32G32B32A32_FLOAT));
            gpuSceneFree = new();
            gpuSceneUpdate = new();//*/
        }

        public CSInstance CreateInstance() {
            //return CSScene.CreateInstance();
            int size = sizeof(float) * 4 * 10;
            var range = gpuSceneFree.Allocate(size);
            if (range.Length == -1) {
                int resize = Math.Max(gpuScene.BufferCapacityCount, 1024) * 2;
                gpuScene.AllocResize(resize);
                range = new RangeInt(gpuScene.BufferLayout.mCount, size);
                gpuScene.BufferLayout.mCount += size;
            }
            var id = instances.Add(new Instance() { Data = range, });
            return new CSInstance(id);//*/
        }
        public unsafe void UpdateInstanceData(CSInstance instance, int offset, void* data, int dataLen) {
            var instanceData = instances[instance.GetInstanceId()];
            new Span<byte>((byte*)data, dataLen).CopyTo(
                new Span<byte>((byte*)gpuScene.Elements[0].mData + instanceData.Data.Start + offset, dataLen)
            );//*/
            gpuScene.BufferLayout.revision++;
            //CSScene.UpdateInstanceData(instance, offset, data, dataLen);
            int stride = sizeof(float) * 10 * 4;
            var range = new RangeInt(instance.GetInstanceId() * stride + offset, dataLen);
            int itemI = 0, max = gpuDelta.Count;
            while (itemI < max) {
                int mid = (itemI + max) / 2;
                if (gpuDelta[mid].Start < range.Start) itemI = mid + 1;
                else max = mid;
            }
            if (itemI < gpuDelta.Count && gpuDelta[itemI].Overlaps(range)) {
                var other = gpuDelta[itemI];
                gpuDelta[itemI] = RangeInt.FromBeginEnd(Math.Min(other.Start, range.Start), Math.Max(other.End, range.End));
                return;
            }
            gpuDelta.Insert(itemI, range);
        }
        public unsafe MemoryBlock<Vector4> GetInstanceData(CSInstance instance) {
            //return CSScene.GetInstanceData(instance);
            var instanceData = instances[instance.GetInstanceId()];
            var data = (byte*)gpuScene.Elements[0].mData + instanceData.Data.Start;
            return new MemoryBlock<Vector4>((Vector4*)data, instanceData.Data.Length / sizeof(Vector4));//*/
        }
        public void RemoveInstance(CSInstance instance) {
            //CSScene.RemoveInstance(instance);
            var instanceData = instances[instance.GetInstanceId()];
            gpuSceneFree.Return(ref instanceData.Data);
            instances.Return(instance.GetInstanceId());//*/
        }

        //public CSTexture GetGPUBuffer() { return CSScene.GetGPUBuffer(); }
        //public int GetGPURevision() { return CSScene.GetGPURevision(); }
        public CSBufferLayout GetGPUBuffer() { return gpuScene.BufferLayout; }
        public int GetGPURevision() { return gpuScene.BufferLayout.revision; }

        unsafe public void PostRender() {
            foreach (var instance in movedInstances) {
                var data = GetInstanceData(instance);
                Matrix4x4 mat = *(Matrix4x4*)data.Data;
                UpdateInstanceData(instance, sizeof(Matrix4x4), &mat, sizeof(Matrix4x4));
            }
            movedInstances.Clear();
        }

        unsafe public Matrix4x4 GetTransform(WorldObject target) {
            foreach (var instance in target.Meshes) {
                var data = GetInstanceData(instance);
                return *((Matrix4x4*)data.Data);
            }
            return default;
        }
        unsafe public void SetTransform(WorldObject target, Matrix4x4 mat) {
            foreach (var instance in target.Meshes) {
                UpdateInstanceData(instance, 0, &mat, sizeof(Matrix4x4));
                movedInstances.Add(instance);
            }
        }
        unsafe public Matrix4x4 GetTransform(CSInstance instance) {
            var data = GetInstanceData(instance);
            return *((Matrix4x4*)data.Data);
        }
        unsafe public void SetTransform(CSInstance instance, Matrix4x4 mat) {
            UpdateInstanceData(instance, 0, &mat, sizeof(Matrix4x4));
            movedInstances.Add(instance);
        }

        public void SubmitToGPU(CSGraphics graphics) {
            if (gpuDelta.Count == 0) return;
            graphics.CopyBufferData(GetGPUBuffer(), gpuDelta);
            gpuDelta.Clear();
        }
    }

    unsafe public class RenderQueue {
        struct DrawBatch {
            public string mName;
            public CSPipeline mPipelineLayout;
            public MemoryBlock<CSBufferLayout> mBufferLayouts;
            public MemoryBlock<nint> mResources;
            public RangeInt mInstanceRange;
        }

        // Data which is erased each frame
        public ArrayList<byte> mFrameData = new();
        List<DrawBatch> mDraws = new();

        // Passes the typed instance buffer to a CommandList
        public BufferLayoutPersistent mInstanceBufferLayout;

        public int InstanceCount => mInstanceBufferLayout.Count;

        public RenderQueue() {
            mInstanceBufferLayout = new BufferLayoutPersistent(BufferLayoutPersistent.Usages.Instance);
            mInstanceBufferLayout.MarkInvalid();
            mInstanceBufferLayout.AppendElement(
                new CSBufferElement("INSTANCE", BufferFormat.FORMAT_R32_UINT)
            );
        }

        public void Clear() {
            // Clear previous data
            mFrameData.Clear();
            mInstanceBufferLayout.Clear();
            mDraws.Clear();
            mFrameData.Reserve(2048);
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
        public void AppendMesh(string name, CSPipeline pipeline, MemoryBlock<CSBufferLayout> buffers, MemoryBlock<nint> resources, RangeInt instances) {
            mDraws.Add(new DrawBatch() {
                mName = name,
                mPipelineLayout = pipeline,
                mBufferLayouts = buffers,
                mResources = resources,
                mInstanceRange = instances,
            });
        }

        unsafe public void AppendInstance(uint instanceId) {
            if (InstanceCount >= mInstanceBufferLayout.BufferCapacityCount) {
                mInstanceBufferLayout.AllocResize(Math.Max(16, mInstanceBufferLayout.BufferCapacityCount * 2));
            }
            ((uint*)mInstanceBufferLayout.Elements[0].mData)[mInstanceBufferLayout.BufferLayout.mCount++] = instanceId;
        }

        public void Render(CSGraphics graphics) {
            // Submit daw calls
            foreach (var draw in mDraws) {
                // Dont need to update buffer, because data is held in Elements (by pointer)

                // Submit
                CSDrawConfig config = CSDrawConfig.MakeDefault();
                var pipeline = draw.mPipelineLayout;
                graphics.Draw(
                    pipeline,
                    draw.mBufferLayouts,
                    draw.mResources,
                    config,
                    (int)draw.mInstanceRange.Length
                );
            }
        }
    }

    public class RetainedRenderer : IDisposable {
        public struct StateKey : IEquatable<StateKey>, IComparable<StateKey> {
            public Mesh Mesh;
            public int MaterialSet;
            public StateKey(Mesh mesh, int matSet) {
                Mesh = mesh;
                MaterialSet = matSet;
            }
            public bool Equals(StateKey o) {
                return Mesh.Equals(o.Mesh) && MaterialSet == o.MaterialSet;
            }
            public int CompareTo(StateKey o) {
                int compare = Mesh.VertexBuffer.identifier.CompareTo(o.Mesh.VertexBuffer.identifier);
                if (compare == 0) compare = MaterialSet - o.MaterialSet;
                return compare;
            }
        }
        public class Batch {
            public StateKey StateKey;
            public Mesh Mesh => StateKey.Mesh;
            public int MaterialSet => StateKey.MaterialSet;
            public List<uint> Instances = new();
            public CSBufferLayout[] BufferLayoutCache;
            public Batch(StateKey stateKey, CSBufferLayout[] buffers) {
                StateKey = stateKey;
                BufferLayoutCache = buffers;
            }
        }
        public class ResolvedPipeline {
            public CSPipeline mPipeline;
            public List<int> mResolvedCBs = new();
            public int mResolvedResources;
        };

        public readonly Scene Scene;

        // All batches that currently exist
        private List<Batch> batches = new();
        private Dictionary<uint, StateKey> instanceBatches = new();
        // Stores cached PSOs per mesh/matset/graphics
        Dictionary<int, ResolvedPipeline> mPipelineCache = new();

        // Passes the typed instance buffer to a CommandList
        BufferLayoutPersistent mInstanceBufferLayout;
        // Material to inject GPU Scene buffer as 'instanceData'
        private Material instanceMaterial;
        // Stores per-instance data

        public RetainedRenderer(Scene scene) {
            Scene = scene;
            instanceMaterial = new();
            mInstanceBufferLayout = new BufferLayoutPersistent(BufferLayoutPersistent.Usages.Instance);
            mInstanceBufferLayout.MarkInvalid();
            mInstanceBufferLayout.AppendElement(new CSBufferElement("INSTANCE", BufferFormat.FORMAT_R32_UINT));
            instanceMaterial.SetBuffer("instanceData", Scene.GetGPUBuffer());
        }
        public void Dispose() {
            mInstanceBufferLayout.Dispose();
            //instanceMaterial.Dispose();
        }

        // Add an instance to be drawn each frame
        unsafe public int AppendInstance(Mesh mesh, Span<Material> materials, int sceneId) {
            using var mats = new PooledArray<Material>(materials, materials.Length + 1);
            mats[^1] = instanceMaterial;

            int matSetId = Scene.MaterialCollection.Require(mats);
            var key = new StateKey(mesh, matSetId);
            int bucket = 0, max = batches.Count - 1;
            while (bucket < max) {
                int mid = (bucket + max) / 2;
                if (batches[mid].StateKey.CompareTo(key) < 0) bucket = mid + 1;
                else max = mid;
            }
            if (bucket == batches.Count || !batches[bucket].StateKey.Equals(key)) {
                var buffers = new CSBufferLayout[3];
                buffers[0] = mesh.IndexBuffer;
                buffers[1] = mesh.VertexBuffer;
                buffers[2] = mInstanceBufferLayout.BufferLayout;
                batches.Insert(bucket, new Batch(key, buffers));
            }
            var batch = batches[bucket];
            int instance = 0, imax = batch.Instances.Count - 1;
            while (instance < imax) {
                int mid = (instance + imax) / 2;
                if (batch.Instances[mid].CompareTo(sceneId) < 0) instance = mid + 1;
                else imax = mid;
            }
            batch.Instances.Insert(instance, (uint)sceneId);
            instanceBatches.Add((uint)sceneId, key);
            return sceneId;
        }
        // Set visibility
        public void SetVisible(int sceneId, bool visible) {
        }
        // Remove an instance from rendering
        public void RemoveInstance(int sceneId) {
            if (!instanceBatches.TryGetValue((uint)sceneId, out var key)) return;
            int bucket = 0, max = batches.Count - 1;
            while (bucket < max) {
                int mid = (bucket + max) / 2;
                if (batches[mid].StateKey.CompareTo(key) < 0) bucket = mid + 1;
                else max = mid;
            }
            var batch = batches[bucket];
            int instance = 0, imax = batch.Instances.Count - 1;
            while (instance < imax) {
                int mid = (instance + imax) / 2;
                if (batch.Instances[mid].CompareTo(sceneId) < 0) instance = mid + 1;
                else imax = mid;
            }
            batch.Instances.RemoveAt(instance);
            instanceBatches.Remove((uint)sceneId);
        }

        // Generate a drawlist for rendering currently visible objects
        unsafe public void SubmitToRenderQueue(CSGraphics graphics, RenderQueue queue, in Frustum frustum) {
            queue.mInstanceBufferLayout.BufferLayout.revision++;
            foreach (var batch in batches) {
                if (batch.Instances.Count == 0) continue;

                var mesh = batch.Mesh;
                var instBegin = queue.InstanceCount;

                // Calculate visible instances
                var bbox = mesh.BoundingBox;
                var bboxCtr = bbox.Centre;
                var bboxExt = bbox.Extents;
                foreach (var instance in batch.Instances) {
                    var data = Scene.GetInstanceData(new CSInstance((int)instance));
                    var matrix = *(Matrix4x4*)(data.Data);
                    if (!frustum.GetIsVisible(Vector3.Transform(bboxCtr, matrix), bboxExt)) continue;
                    queue.AppendInstance(instance);
                }
                // If no instances were created, quit
                if (queue.InstanceCount == instBegin) continue;

                // Compute and cache CB and resource data
                var meshMatHash = HashCode.Combine(batch.Mesh.GetHashCode(), batch.MaterialSet, graphics.GetHashCode());
                if (!mPipelineCache.TryGetValue(meshMatHash, out var resolved)) {
                    var materials = Scene.MaterialCollection.GetMaterials(batch.MaterialSet);
                    var pso = MaterialEvaluator.ResolvePipeline(graphics, batch.BufferLayoutCache, materials);
                    resolved = new ResolvedPipeline() {
                        mPipeline = pso,
                    };
                    mPipelineCache.Add(meshMatHash, resolved);
                    var psobuffsers = pso.GetConstantBuffers();
                    var psoresources = pso.GetResources();
                    foreach (var cb in psobuffsers) {
                        var resolvedId = Scene.ResolvedMaterials.RequireResolved(graphics, cb.GetValues(), batch.MaterialSet);
                        resolved.mResolvedCBs.Add(resolvedId);
                    }
                    using var tresources = new PooledArray<CSUniformValue>(psoresources.Length);
                    for (int i = 0; i < psoresources.Length; ++i) {
                        var res = psoresources[i];
                        tresources[i] = new CSUniformValue {
                            mName = res.mName,
                            mOffset = (int)(i * sizeof(void*)),
                            mSize = (int)sizeof(void*),
                        };
                    }
                    resolved.mResolvedResources = Scene.ResolvedMaterials.RequireResolved(graphics, tresources, batch.MaterialSet);
                }

                var pipeline = resolved.mPipeline;
                var psoconstantBuffers = pipeline.GetConstantBuffers();
                var resources = graphics.RequireFrameData<nint>(psoconstantBuffers.Length + pipeline.GetResourceCount());
                int r = 0;

                // Get constant buffer data for this batch
                for (int i = 0; i < resolved.mResolvedCBs.Count; ++i) {
                    var cbid = resolved.mResolvedCBs[i];
                    nint resource = 0;
                    if (cbid >= 0) {
                        var psocb = psoconstantBuffers[i];
                        ref var resolvedMat = ref Scene.ResolvedMaterials.GetResolved(cbid);
                        resource = RequireConstantBuffer(graphics, resolvedMat.mEvaluator);
                    }
                    resources[r++] = resource;
                }
                // Get other resource data for this batch
                {
                    ref var resCB = ref Scene.ResolvedMaterials.GetResolved(resolved.mResolvedResources);
                    resCB.mEvaluator.EvaluateSafe(resources.Slice(r).Reinterpret<byte>());
                    r += resources.Length;
                }

                // Need to force this to use queues instance buffer
                // TODO: A more generic approach
                var bindings = graphics.RequireFrameData<CSBufferLayout>(batch.BufferLayoutCache);
                bindings[0] = mesh.IndexBuffer;
                bindings[1] = mesh.VertexBuffer;
                bindings[^1] = queue.mInstanceBufferLayout;
                bindings[^1].mOffset = instBegin;
                bindings[^1].mCount = queue.InstanceCount - instBegin;
                // Add the draw command
                queue.AppendMesh(
                    "ASDF",//mesh.GetMeshData().mName,
                    pipeline,
                    bindings,
                    resources,
                    RangeInt.FromBeginEnd((int)instBegin, (int)queue.InstanceCount)
                );
            }
        }

        private nint RequireConstantBuffer(CSGraphics graphics, MaterialEvaluator evaluator) {
            Span<byte> outdata = stackalloc byte[evaluator.mDataSize];
            evaluator.Evaluate(outdata);
            return graphics.RequireConstantBuffer(outdata);
        }
    }
}
