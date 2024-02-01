using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Utility;

namespace Weesals.Engine {
    public class Mesh {

        protected string name;
        protected Material material;

        protected BoundingBox boundingBox;

        protected int revision;
        protected sbyte vertexPositionId;
        protected sbyte vertexNormalId;
        protected sbyte vertexTangentId;
        protected sbyte vertexColorId;
        protected sbyte vertexTexCoord0Id;
        protected BufferLayoutPersistent indexBuffer;
        protected BufferLayoutPersistent vertexBuffer;
        protected bool isDynamic;

        public string Name => name;
        public CSBufferLayout IndexBuffer => indexBuffer;
        public CSBufferLayout VertexBuffer => vertexBuffer;
        public BoundingBox BoundingBox => boundingBox;
        public int Revision => revision;
        public Material Material => material;

        public int VertexCount => vertexBuffer.Count;
        public int IndexCount => indexBuffer.Count;

        unsafe public Mesh(string _name) {
            revision = 0;
            name = _name;
            vertexBuffer = new BufferLayoutPersistent(BufferLayoutPersistent.Usages.Vertex);
            indexBuffer = new BufferLayoutPersistent(BufferLayoutPersistent.Usages.Index);
            vertexPositionId = 0;
            vertexNormalId = -1;
            vertexTangentId = -1;
            vertexColorId = -1;
            vertexTexCoord0Id = -1;
		    vertexPositionId = (sbyte)vertexBuffer.AppendElement(new CSBufferElement("POSITION", BufferFormat.FORMAT_R32G32B32_FLOAT, sizeof(Vector3), null));
		    indexBuffer.AppendElement(new CSBufferElement("INDEX", BufferFormat.FORMAT_R32_UINT, sizeof(int), null));
            material = new();
            boundingBox = default;
        }
        public void Dispose() {
            vertexBuffer.Dispose();
            indexBuffer.Dispose();
        }

        public void Reset() {
            SetVertexCount(0);
            SetIndexCount(0);
            MarkChanged();
        }
        public void CalculateBoundingBox() {
            Vector3 min = new Vector3(float.MaxValue);
            Vector3 max = new Vector3(float.MinValue);
            foreach (var pos in GetPositionsV()) {
                min = Vector3.Min(min, pos);
                max = Vector3.Max(max, pos);
            }
            boundingBox = BoundingBox.FromMinMax(min, max);
        }

        private void RequireVertexElementFormat(ref sbyte elementId, BufferFormat fmt, string semantic) {
            if (elementId == -1) {
                elementId = (sbyte)vertexBuffer.AppendElement(new CSBufferElement(semantic, fmt));
                return;
            }
            vertexBuffer.SetElementFormat(elementId, fmt);
        }

        public void RequireVertexPositions(BufferFormat fmt = BufferFormat.FORMAT_R32G32B32_FLOAT) {
            RequireVertexElementFormat(ref vertexPositionId, fmt, "POSITION");
        }
        public void RequireVertexNormals(BufferFormat fmt = BufferFormat.FORMAT_R32G32B32_FLOAT) {
            RequireVertexElementFormat(ref vertexNormalId, fmt, "NORMAL");
        }
        public void RequireVertexTangents(BufferFormat fmt = BufferFormat.FORMAT_R32G32B32_FLOAT) {
            RequireVertexElementFormat(ref vertexTangentId, fmt, "TANGENT");
        }
        public void RequireVertexTexCoords(int coord, BufferFormat fmt = BufferFormat.FORMAT_R32G32_FLOAT) {
            Debug.Assert(coord == 0);
            // TODO: Ordered texcoord lists in Elements starting at vertexTexCoord0Id
            RequireVertexElementFormat(ref vertexTexCoord0Id, fmt, "TEXCOORD");
        }
        public void RequireVertexColors(BufferFormat fmt = BufferFormat.FORMAT_R8G8B8A8_UNORM) {
            RequireVertexElementFormat(ref vertexColorId, fmt, "COLOR");
        }
        public void SetIndexFormat(bool _32bit) {
            indexBuffer.SetElementFormat(0, _32bit ? BufferFormat.FORMAT_R32_UINT : BufferFormat.FORMAT_R16_UINT);
        }

        public void SetVertexCount(int count) {
		    if (vertexBuffer.Count == count) return;
            if (!isDynamic) vertexBuffer.AllocResize(count);
            else if (count > vertexBuffer.BufferCapacityCount) vertexBuffer.AllocResize((int)BitOperations.RoundUpToPowerOf2((uint)count));
            vertexBuffer.BufferLayout.mCount = count;
            MarkChanged();
	    }
        public void SetIndexCount(int count) {
		    if (indexBuffer.Count == count) return;
            if (!isDynamic) indexBuffer.AllocResize(count);
            else if (count > indexBuffer.BufferCapacityCount) indexBuffer.AllocResize((int)BitOperations.RoundUpToPowerOf2((uint)count));
            indexBuffer.BufferLayout.mCount = count;
            MarkChanged();
	    }

        public void SetIndices(Span<int> indices) {
		    SetIndexCount((int)indices.Length);
		    GetIndicesV().Set(indices);
	    }
        public void SetIndices(Span<ushort> indices) {
            SetIndexCount((int)indices.Length);
            GetIndicesV<ushort>().Set(indices);
        }

        public TypedBufferView<Vector3> GetPositionsV() {
            return new TypedBufferView<Vector3>(vertexBuffer.Elements[vertexPositionId], vertexBuffer.Count);
        }
        public TypedBufferView<Vector3> GetNormalsV(bool require = false) {
            if (vertexNormalId == -1) { if (require) RequireVertexNormals(); else return default; }
            return new TypedBufferView<Vector3>(vertexBuffer.Elements[vertexNormalId], vertexBuffer.Count);
        }
        public TypedBufferView<Vector3> GetTangentsV(bool require = false) {
            if (vertexTangentId == -1) { if (require) RequireVertexTangents(); else return default; }
            return new TypedBufferView<Vector3>(vertexBuffer.Elements[vertexTangentId], vertexBuffer.Count);
        }
        public TypedBufferView<Vector2> GetTexCoordsV(int channel = 0, bool require = false) {
            if (vertexTexCoord0Id == -1) { if (require) RequireVertexTexCoords(channel); else return default; }
            return new TypedBufferView<Vector2>(vertexBuffer.Elements[vertexTexCoord0Id], vertexBuffer.Count);
        }
        public TypedBufferView<Color> GetColorsV(bool require = false) {
            if (vertexColorId == -1) { if (require) RequireVertexColors(); else return default; }
            return new TypedBufferView<Color>(vertexBuffer.Elements[vertexColorId], vertexBuffer.Count);
        }
        public TypedBufferView<int> GetIndicesV() {
            return new TypedBufferView<int>(indexBuffer.Elements[0], indexBuffer.Count);
        }
        public TypedBufferView<T> GetIndicesV<T>() where T : unmanaged {
            return new TypedBufferView<T>(indexBuffer.Elements[0], indexBuffer.Count);
        }


        // Notify graphics and other dependents that the mesh data has changed
        public void MarkChanged() {
            revision++;
            indexBuffer.BufferLayout.revision++;
            vertexBuffer.BufferLayout.revision++;
        }

        unsafe public static Mesh CreateFrom(CSMesh other) {
            var otherData = other.GetMeshData();
            var mesh = new Mesh(otherData.mName.ToString());
            ref var vbo = ref mesh.vertexBuffer;
            ref var otherVbo = ref *other.GetVertexBuffer();
            vbo.CopyFrom(otherVbo);
            ref var ibo = ref mesh.indexBuffer;
            ref var otherIbo = ref *other.GetIndexBuffer();
            ibo.CopyFrom(otherIbo);
            var otherMat = other.GetMaterial();
            mesh.material.CopyFrom(otherMat);
            mesh.boundingBox = other.GetBoundingBox();
            return mesh;
        }

        public override string ToString() { return Name; }
    }

    // A mesh that is optimised for frequent changes
    public class DynamicMesh : Mesh {
        public DynamicMesh(string _name) : base(_name) {
            isDynamic = true;
        }
        public struct VertexRange {
            public readonly DynamicMesh Mesh;
            public readonly RangeInt Range;
            public int BaseVertex => Range.Start;
            public int Count => Range.Length;
            public VertexRange(DynamicMesh mesh, RangeInt range) {
                Mesh = mesh;
                Range = range;
            }
            public TypedBufferView<Vector3> GetPositions() {
                return Mesh.GetPositionsV().Slice(Range);
            }
            public TypedBufferView<Vector3> GetNormals(bool require = false) {
                return Mesh.GetNormalsV(require).Slice(Range);
            }
            public TypedBufferView<Vector3> GetTangents(bool require = false) {
                return Mesh.GetTangentsV(require).Slice(Range);
            }
            public TypedBufferView<Vector2> GetTexCoords(int coord = 0, bool require = false) {
                return Mesh.GetTexCoordsV(coord, require).Slice(Range);
            }
            public TypedBufferView<Color> GetColors(bool require = false) {
                return Mesh.GetColorsV(require).Slice(Range);
            }
        }
        public struct IndexRange {
            public readonly DynamicMesh Mesh;
            public readonly RangeInt Range;
            public int BaseIndex => Range.Start;
            public int Count => Range.Length;
            public IndexRange(DynamicMesh mesh, RangeInt range) {
                Mesh = mesh;
                Range = range;
            }
            public TypedBufferView<int> GetIndices() {
                return Mesh.GetIndicesV().Slice(Range);
            }
        }
        public VertexRange AppendVerts(int count) {
            int begin = VertexCount;
            SetVertexCount(begin + count);
            return new VertexRange(this, new RangeInt(begin, count));
        }
        public IndexRange AppendIndices(int count) {
            int begin = IndexCount;
            SetIndexCount(begin + count);
            return new IndexRange(this, new RangeInt(begin, count));
        }
    }
}
