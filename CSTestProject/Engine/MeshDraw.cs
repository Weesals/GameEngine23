using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Weesals.Utility;

namespace Weesals.Engine {
	unsafe public class MeshDraw {
        protected struct RenderPassCache {
            public CSIdentifier mRenderPass;
            public ulong mPipelineHash;
            public CSPipeline mPipeline;
            public bool IsValid() { return mPipeline.IsValid(); }
        }

        protected Mesh mMesh;
		protected List<Material> mMaterials = new();
		protected List<CSBufferLayout> mBufferLayout = new();
		protected List<RenderPassCache> mPassCache = new();

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
        }

        unsafe protected RenderPassCache GetPassCache(CSGraphics graphics) {
            var renderPass = CSName.None;
            if (mBufferLayout.Count == 0) InvalidateMesh();
            var pipelineHash = (ulong)renderPass.GetId() + graphics.GetGlobalPSOHash();
            int min = 0, max = mPassCache.Count - 1;
            while (min < max) {
                int mid = (min + max) >> 1;
                if (mPassCache[mid].mPipelineHash < pipelineHash) {
                    min = mid + 1;
                } else {
                    max = mid;
                }
            }
            if (min == mPassCache.Count || mPassCache[min].mRenderPass != renderPass) {
                var pipeline = MaterialEvaluator.ResolvePipeline(graphics, mBufferLayout, mMaterials);
                mPassCache.Insert(min, new RenderPassCache {
			        mRenderPass = renderPass,
			        mPipeline = pipeline,
		        });
            }
            return mPassCache[min];
        }
        unsafe public void Draw(CSGraphics graphics, CSDrawConfig config) {
            if (mBufferLayout.Count == 0) InvalidateMesh();
            var passCache = GetPassCache(graphics);
            if (!passCache.IsValid()) return;
            Debug.Assert(passCache.mPipeline.GetBindingCount() == mBufferLayout.Count);
            var resources = MaterialEvaluator.ResolveResources(graphics, passCache.mPipeline, mMaterials);
            graphics.Draw(passCache.mPipeline, mBufferLayout, resources, config);
        }
    }

	public class MeshDrawInstanced : MeshDraw {
        protected string name;
		protected BufferLayoutPersistent mInstanceBuffer;
		public MeshDrawInstanced(Mesh mesh, Material material) : this(mesh, new Span<Material>(ref material)) {
		}
		public MeshDrawInstanced(Mesh mesh, Span<Material> materials) : base(mesh, materials) {
            name = mesh.Name.ToString();
            mInstanceBuffer = new BufferLayoutPersistent(BufferLayoutPersistent.Usages.Instance);
        }
        unsafe public override void InvalidateMesh() {
            base.InvalidateMesh();
            mBufferLayout.Add(mInstanceBuffer.BufferLayout);
        }
        public int GetInstanceCount() {
            return mInstanceBuffer.Count;
        }
		unsafe public int AddInstanceElement(CSIdentifier name, BufferFormat fmt = BufferFormat.FORMAT_R32_UINT) {
            int id = mInstanceBuffer.AppendElement(new CSBufferElement(name, fmt));
            mPassCache.Clear();
            return id;
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
            if (markDirty) {
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
        new unsafe public void Draw(CSGraphics graphics, CSDrawConfig config) {
            int instanceCount = GetInstanceCount();
            if (instanceCount <= 0) return;
            var passCache = GetPassCache(graphics);
            if (!passCache.IsValid()) return;
            Debug.Assert(passCache.mPipeline.GetBindingCount() == mBufferLayout.Count);

            var resources = MaterialEvaluator.ResolveResources(graphics, passCache.mPipeline, mMaterials);
            graphics.Draw(passCache.mPipeline, mBufferLayout, resources, config, instanceCount);
        }
		unsafe public void Draw(CSGraphics graphics, ScenePass pass, CSDrawConfig config) {
            int instanceCount = GetInstanceCount();
            if (instanceCount <= 0) return;
            var passCache = GetPassCache(graphics);
            if (!passCache.IsValid()) return;
            Debug.Assert(passCache.mPipeline.GetBindingCount() == mBufferLayout.Count);

            using var materials = new PooledArray<Material>(mMaterials.Count + 1);
            materials[0] = pass.OverrideMaterial;
            CollectionsMarshal.AsSpan(mMaterials).CopyTo(materials.AsSpan(1));
            var resources = MaterialEvaluator.ResolveResources(graphics, passCache.mPipeline, materials);
            var buffers = graphics.RequireFrameData(mBufferLayout);

            pass.RenderQueue.AppendMesh(name, passCache.mPipeline, buffers, resources, instanceCount);
        }
	};

}
