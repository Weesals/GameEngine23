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
        protected sbyte vertexPositionId = -1;
        protected sbyte vertexNormalId = -1;
        protected sbyte vertexTangentId = -1;
        protected sbyte vertexColorId = -1;
        protected sbyte vertexTexCoord0Id = -1;
        protected BufferLayoutPersistent indexBuffer;
        protected BufferLayoutPersistent vertexBuffer;
        protected bool isDynamic;

        public string Name => name;
        public CSBufferLayout IndexBuffer => indexBuffer.BufferLayout;
        public CSBufferLayout VertexBuffer => vertexBuffer.BufferLayout;
        public BoundingBox BoundingBox => boundingBox;
        public int Revision => revision;
        public Material Material => material;
        public Matrix4x4 Transform = Matrix4x4.Identity;

        public int VertexCount => vertexBuffer.Count;
        public int IndexCount => indexBuffer.Count;

        unsafe public Mesh(string _name) {
            revision = 0;
            name = _name;
            vertexBuffer = new BufferLayoutPersistent(BufferLayoutPersistent.Usages.Vertex);
            indexBuffer = new BufferLayoutPersistent(BufferLayoutPersistent.Usages.Index);
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
        public void SetBoundingBox(BoundingBox boundingBox) {
            this.boundingBox = boundingBox;
        }

        protected void RequireVertexElementFormat(ref sbyte elementId, BufferFormat fmt, string semantic) {
            if (fmt == BufferFormat.FORMAT_UNKNOWN) {
                Debug.Assert(elementId == -1);
                return;
            }
            if (elementId != -1) vertexBuffer.SetElementFormat(elementId, fmt);
            else elementId = (sbyte)vertexBuffer.AppendElement(new CSBufferElement(semantic, fmt));
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

        public void SetVertexCount(int count) => SetBufferCount(ref vertexBuffer, count);
        public void SetIndexCount(int count) => SetBufferCount(ref indexBuffer, count);
        private void SetBufferCount(ref BufferLayoutPersistent buffer, int count) {
            if (buffer.Count == count) return;
            if (!isDynamic) buffer.AllocResize(count);
            else if (count > buffer.BufferCapacityCount) buffer.AllocResize((int)BitOperations.RoundUpToPowerOf2((uint)count));
            buffer.BufferLayout.mCount = count;
            MarkChanged();
        }

        public void SetIndices(Span<uint> indices) {
		    SetIndexCount(indices.Length);
		    GetIndicesV().Set(indices);
	    }
        public void SetIndices(Span<ushort> indices) {
            SetIndexCount(indices.Length);
            GetIndicesV<ushort>().Set(indices);
        }

        public BufferFormat GetPositionsFormat() {
            return vertexPositionId >= 0 ? vertexBuffer.Elements[vertexPositionId].mFormat : BufferFormat.FORMAT_UNKNOWN;
        }
        public BufferFormat GetNormalsFormat() {
            return vertexNormalId >= 0 ? vertexBuffer.Elements[vertexNormalId].mFormat : BufferFormat.FORMAT_UNKNOWN;
        }
        public BufferFormat GetTangentsFormat() {
            return vertexTangentId >= 0 ? vertexBuffer.Elements[vertexTangentId].mFormat : BufferFormat.FORMAT_UNKNOWN;
        }
        public BufferFormat GetTexcoordFormat(int coord) {
            return vertexTexCoord0Id >= 0 ? vertexBuffer.Elements[vertexTexCoord0Id].mFormat : BufferFormat.FORMAT_UNKNOWN;
        }
        public BufferFormat GetColorsFormat() {
            return vertexColorId >= 0 ? vertexBuffer.Elements[vertexColorId].mFormat : BufferFormat.FORMAT_UNKNOWN;
        }
        public BufferFormat GetIndicesFormat() {
            return indexBuffer.Elements[0].mFormat;
        }

        public TypedBufferView<Vector3> GetPositionsV()
            => GetPositionsV<Vector3>();
        public TypedBufferView<T> GetPositionsV<T>() where T: unmanaged {
            return new TypedBufferView<T>(vertexBuffer.Elements[vertexPositionId], vertexBuffer.Count);
        }

        public TypedBufferView<Vector3> GetNormalsV(bool require = false)
            => GetNormalsV<Vector3>(require);
        public TypedBufferView<T> GetNormalsV<T>(bool require = false) where T : unmanaged {
            if (vertexNormalId == -1) { if (require) RequireVertexNormals(); else return default; }
            return new TypedBufferView<T>(vertexBuffer.Elements[vertexNormalId], vertexBuffer.Count);
        }

        public TypedBufferView<Vector3> GetTangentsV(bool require = false)
            => GetTangentsV<Vector3>(require);
        public TypedBufferView<T> GetTangentsV<T>(bool require = false) where T : unmanaged {
            if (vertexTangentId == -1) { if (require) RequireVertexTangents(); else return default; }
            return new TypedBufferView<T>(vertexBuffer.Elements[vertexTangentId], vertexBuffer.Count);
        }

        public TypedBufferView<Vector2> GetTexCoordsV(int channel = 0, bool require = false)
            => GetTexCoordsV<Vector2>(channel, require);
        public TypedBufferView<T> GetTexCoordsV<T>(int channel = 0, bool require = false) where T : unmanaged {
            if (vertexTexCoord0Id == -1) { if (require) RequireVertexTexCoords(channel); else return default; }
            return new TypedBufferView<T>(vertexBuffer.Elements[vertexTexCoord0Id], vertexBuffer.Count);
        }

        public TypedBufferView<Color> GetColorsV(bool require = false)
            => GetColorsV<Color>(require);
        public TypedBufferView<T> GetColorsV<T>(bool require = false) where T : unmanaged {
            if (vertexColorId == -1) { if (require) RequireVertexColors(); else return default; }
            return new TypedBufferView<T>(vertexBuffer.Elements[vertexColorId], vertexBuffer.Count);
        }

        public TypedBufferView<uint> GetIndicesV() {
            return GetIndicesV<uint>();
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

        public override string ToString() { return Name; }
    }

    // A mesh that is optimised for frequent changes
    public class DynamicMesh : Mesh {
        public DynamicMesh(string _name) : base(_name) {
            isDynamic = true;
        }
        public void Clear() {
            SetVertexCount(0);
            SetIndexCount(0);
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
            public TypedBufferView<T> GetPositions<T>() where T : unmanaged {
                return Mesh.GetPositionsV<T>().Slice(Range);
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
            public TypedBufferView<uint> GetIndices() {
                return Mesh.GetIndicesV().Slice(Range);
            }
            public TypedBufferView<T> GetIndices<T>() where T : unmanaged {
                return Mesh.GetIndicesV<T>().Slice(Range);
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
