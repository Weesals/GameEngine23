using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine.Profiling;
using Weesals.Utility;

namespace Weesals.Engine {
    unsafe public class MeshDraw {

        protected static ProfilerMarker ProfileMarker_MeshDraw = new("Mesh Draw");
        protected static ProfilerMarker ProfileMarker_GetPass = new("Get Pass");
        protected static ProfilerMarker ProfileMarker_ResolveResources = new("Resolve Resources");

        protected struct RenderPassCache {
            public ulong mPipelineHash;
            public CSPipeline mPipeline;
            public bool IsValid => mPipeline.IsValid;
        }

        protected Mesh mMesh;
        protected List<Material> mMaterials = new();
        protected List<CSBufferLayout> mBufferLayout = new();
        protected List<RenderPassCache> mPassCache = new();
        protected int resourceGeneration;

        public int RenderOrder { get; set; }

        public MeshDraw(Mesh mesh, Material material) : this(mesh, new Span<Material>(ref material)) { }
        public MeshDraw(Mesh mesh, Span<Material> materials) {
            mMesh = mesh;
            foreach (var mat in materials) mMaterials.Add(mat);
        }

        public Mesh GetMesh() { return mMesh; }
        public virtual void InvalidateMesh() {
            mBufferLayout.Clear();
            mBufferLayout.Add(mMesh.IndexBuffer);
            mBufferLayout.Add(mMesh.VertexBuffer);
            //mMesh.CreateMeshLayout(mBufferLayout);
            mPassCache.Clear();
            resourceGeneration = Resources.Generation;
        }

        unsafe protected RenderPassCache GetPassCache(CSGraphics graphics, Span<Material> materials = default) {
            if (materials.Length == 0) materials = CollectionsMarshal.AsSpan(mMaterials);
            using var marker = ProfileMarker_GetPass.Auto();
            if (resourceGeneration != Resources.Generation) InvalidateMesh();
            if (mBufferLayout.Count == 0) InvalidateMesh();
            var pipelineHash = graphics.GetGlobalPSOHash();
            for (int i = 0; i < materials.Length; i++) {
                pipelineHash += (ulong)materials[i].GetHashCode() * 12345;
            }
            int min = 0, max = mPassCache.Count - 1;
            while (min < max) {
                int mid = (min + max) >> 1;
                if (mPassCache[mid].mPipelineHash < pipelineHash) {
                    min = mid + 1;
                } else {
                    max = mid;
                }
            }
            if (min == mPassCache.Count || mPassCache[min].mPipelineHash != pipelineHash) {
                var pipeline = MaterialEvaluator.ResolvePipeline(graphics,
                    CollectionsMarshal.AsSpan(mBufferLayout), materials);
                mPassCache.Insert(min, new RenderPassCache {
                    mPipelineHash = pipelineHash,
                    mPipeline = pipeline,
                });
            }
            return mPassCache[min];
        }
        unsafe public void Draw(CSGraphics graphics, CSDrawConfig config) {
            using var marker = ProfileMarker_MeshDraw.Auto();
            if (mBufferLayout.Count == 0) InvalidateMesh();
            var passCache = GetPassCache(graphics);
            if (!passCache.IsValid) return;
            Debug.Assert(passCache.mPipeline.GetBindingCount() == mBufferLayout.Count);
            MemoryBlock<nint> resources;
            using (var markerRes = ProfileMarker_ResolveResources.Auto()) {
                resources = MaterialEvaluator.ResolveResources(graphics, passCache.mPipeline, mMaterials);
            }
            graphics.Draw(passCache.mPipeline, mBufferLayout, resources.AsCSSpan(), config);
        }
    }

    public class MeshDrawInstanced : MeshDraw {
        protected string name;
        protected BufferLayoutPersistent mInstanceBuffer;
        public CSBufferLayout InstanceBuffer => mInstanceBuffer;

        public MeshDrawInstanced(Mesh mesh, Material material) : this(mesh, new Span<Material>(ref material)) { }
        public MeshDrawInstanced(Mesh mesh, Span<Material> materials) : base(mesh, materials) {
            name = mesh.Name.ToString();
            mInstanceBuffer = new BufferLayoutPersistent(BufferLayoutPersistent.Usages.Instance);
        }
        unsafe public override void InvalidateMesh() {
            base.InvalidateMesh();
            if (mInstanceBuffer.ElementCount > 0)
                mBufferLayout.Add(mInstanceBuffer.BufferLayout);
        }
        public int GetInstanceCount() {
            return mInstanceBuffer.Count;
        }
        public void SetInstanceCount(int count) {
            mInstanceBuffer.RequireCount(count);
        }
        unsafe public int AddInstanceElement(CSIdentifier name, BufferFormat fmt = BufferFormat.FORMAT_R32_UINT) {
            int id = mInstanceBuffer.AppendElement(new CSBufferElement(name, fmt));
            mPassCache.Clear();
            mBufferLayout.Clear();
            return id;
        }
        unsafe public TypedBufferView<T> GetElementData<T>(int elementId) where T : unmanaged {
            return new(mInstanceBuffer.Elements[elementId], mInstanceBuffer.Count);
        }
        unsafe public void SetInstanceData(void* data, int count, int elementId = 0, bool markDirty = true) {
            if (mInstanceBuffer.Count != count) {
                if (mInstanceBuffer.BufferCapacityCount < count) {
                    mInstanceBuffer.AllocResize(count);
                }
                mInstanceBuffer.BufferLayout.mCount = count;
                mInstanceBuffer.CalculateImplicitSize();
                markDirty = true;
            }
            if (markDirty && data != null) {
                var el = mInstanceBuffer.Elements[elementId];
                Unsafe.CopyBlock(el.mData, data, (uint)(count * BufferFormatType.GetMeta(el.mFormat).GetByteSize()));
                mInstanceBuffer.BufferLayout.revision++;
                if (mBufferLayout.Count > 0) mBufferLayout[^1] = mInstanceBuffer.BufferLayout;
            }
        }
        unsafe public void SetInstanceData(void* data, int count, int elementId, int hash) {
            bool dirty = mInstanceBuffer.BufferLayout.revision != hash;
            if (mInstanceBuffer.Count != count) {
                if (mInstanceBuffer.BufferCapacityCount < count) {
                    mInstanceBuffer.AllocResize(count);
                }
                mInstanceBuffer.BufferLayout.mCount = count;
                mInstanceBuffer.CalculateImplicitSize();
                dirty = true;
            }
            if (dirty) {
                var el = mInstanceBuffer.Elements[elementId];
                Unsafe.CopyBlock(el.mData, data, (uint)(count * BufferFormatType.GetMeta(el.mFormat).GetByteSize()));
                mInstanceBuffer.BufferLayout.revision = hash;
                if (mBufferLayout.Count > 0) mBufferLayout[^1] = mInstanceBuffer.BufferLayout;
            }
        }
        public void RevisionFromDataHash() {
            var revision = mInstanceBuffer.Revision;
            mInstanceBuffer.RevisionFromDataHash();
            if (revision != mInstanceBuffer.Revision) {
                InvalidateMesh();
            }
        }

        new unsafe public void Draw(CSGraphics graphics, CSDrawConfig config) {
            using var marker = ProfileMarker_MeshDraw.Auto();
            int instanceCount = GetInstanceCount();
            if (instanceCount <= 0) return;
            var passCache = GetPassCache(graphics);
            if (!passCache.IsValid) return;
            Debug.Assert(passCache.mPipeline.GetBindingCount() == mBufferLayout.Count);

            MemoryBlock<nint> resources;
            using (var markerRes = ProfileMarker_ResolveResources.Auto()) {
                resources = MaterialEvaluator.ResolveResources(graphics, passCache.mPipeline, mMaterials);
            }
            graphics.Draw(passCache.mPipeline, mBufferLayout, resources.AsCSSpan(), config, instanceCount);
        }
        unsafe public void Draw(CSGraphics graphics, ref MaterialStack materials, ScenePass pass, CSDrawConfig config) {
            Draw(graphics, ref materials, pass.RenderQueue, config);
        }
        unsafe public void Draw(CSGraphics graphics, RenderQueue queue, CSDrawConfig config) {
            Draw(graphics, ref Graphics.MaterialStack, queue, config);
        }
        unsafe public void Draw(CSGraphics graphics, ref MaterialStack materials, RenderQueue queue, CSDrawConfig config) {
            using var marker = ProfileMarker_MeshDraw.Auto();
            int instanceCount = GetInstanceCount();
            if (instanceCount <= 0) return;

            using var push = materials.Push(CollectionsMarshal.AsSpan(mMaterials));

            //var passCache = GetPassCache(graphics, materials);
            if (mBufferLayout.Count == 0) InvalidateMesh();
            var pipeline = MaterialEvaluator.ResolvePipeline(graphics,
                CollectionsMarshal.AsSpan(mBufferLayout), materials);
            var passCache = new RenderPassCache() { mPipeline = pipeline, };

            if (!passCache.IsValid) return;
            Debug.Assert(passCache.mPipeline.GetBindingCount() == mBufferLayout.Count);

            MemoryBlock<nint> resources;
            using (var markerRes = ProfileMarker_ResolveResources.Auto()) {
                resources = MaterialEvaluator.ResolveResources(graphics, passCache.mPipeline, materials);
            }
            var buffers = graphics.RequireFrameData(mBufferLayout);

            queue.AppendUsedTextures(resources.Slice(passCache.mPipeline.ConstantBufferCount).Reinterpret<CSBufferReference>());
            queue.AppendMesh(name, passCache.mPipeline, buffers, resources, instanceCount, RenderOrder);
        }
    }
    public class MeshDrawIndirect : MeshDraw {
        protected string name;
        protected BufferLayoutPersistent mInstanceArgs;
        public CSBufferLayout ArgsBuffer => mInstanceArgs;

        public MeshDrawIndirect(Mesh mesh, Material material) : this(mesh, new Span<Material>(ref material)) { }
        public MeshDrawIndirect(Mesh mesh, Span<Material> materials) : base(mesh, materials) {
            name = mesh.Name.ToString();
            mInstanceArgs = new BufferLayoutPersistent(BufferLayoutPersistent.Usages.Uniform);
            unsafe {
                mInstanceArgs.AppendElement(new("INDIRECTARGS", BufferFormat.FORMAT_UNKNOWN, 4));
            }
            mInstanceArgs.AllocResize(5);
            var indirectArgsData = new TypedBufferView<uint>(mInstanceArgs.Elements[0], 5);
            indirectArgsData[0] = (uint)mesh.IndexCount;
            indirectArgsData[1] = 2048;
            indirectArgsData[2] = 0;
            indirectArgsData[3] = 0;
            indirectArgsData[4] = 0;
            mInstanceArgs.NotifyChanged();
        }
        unsafe public void Draw(CSGraphics graphics, ScenePass pass, CSDrawConfig config) {
            Draw(graphics, ref Graphics.MaterialStack, pass, config);
        }
        unsafe public void Draw(CSGraphics graphics, ref MaterialStack materials, ScenePass pass, CSDrawConfig config) {
            using var marker = ProfileMarker_MeshDraw.Auto();

            mBufferLayout.Clear();
            // Must come first
            mBufferLayout.Add(mInstanceArgs);
            mBufferLayout.Add(mMesh.IndexBuffer);
            mBufferLayout.Add(mMesh.VertexBuffer);

            using var push = materials.Push(CollectionsMarshal.AsSpan(mMaterials));

            //var passCache = GetPassCache(graphics, materials);
            var pipeline = MaterialEvaluator.ResolvePipeline(graphics,
                CollectionsMarshal.AsSpan(mBufferLayout), materials);
            var passCache = new RenderPassCache() { mPipeline = pipeline, };

            if (!passCache.IsValid) return;
            Debug.Assert(passCache.mPipeline.GetBindingCount() == mBufferLayout.Count);

            MemoryBlock<nint> resources;
            using (var markerRes = ProfileMarker_ResolveResources.Auto()) {
                resources = MaterialEvaluator.ResolveResources(graphics, passCache.mPipeline, materials);
            }
            var buffers = graphics.RequireFrameData(mBufferLayout);
            pass.RenderQueue.AppendUsedTextures(resources.Slice(passCache.mPipeline.ConstantBufferCount).Reinterpret<CSBufferReference>());
            pass.RenderQueue.AppendMesh(name, passCache.mPipeline, buffers, resources, RenderOrder);
        }
    }
}
