using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.UI;
using Weesals.Utility;

namespace Weesals.Engine {
    public static class Handles {

        public static Matrix4x4 matrix;
        public static Color color;
        public static int RenderHash;
        public static readonly Color ColorFactor = new Color(64, 64, 64, 255);

        private class MeshBuffer : IDisposable {
            public Material Material;
            public DynamicMesh Mesh;
            public BufferLayoutPersistent InstanceBuffer;
            public int RenderHash;
            public MeshBuffer(Material material) {
                Material = material;
                Mesh = new("DynMesh");
                Mesh.RequireVertexPositions();
                Mesh.RequireVertexTexCoords(0);
                Mesh.RequireVertexColors();
                Mesh.RequireVertexTangents();
            }
            public void Dispose() {
                Mesh.Dispose();
            }
            public void Clear() {
                RenderHash = 0;
                if (InstanceBuffer.IsValid) {
                    InstanceBuffer.SetCount(0);
                } else {
                    Mesh.SetVertexCount(0);
                    Mesh.SetIndexCount(0);
                }
            }
        }
        private class LineMeshBuffer : MeshBuffer {
            public int PositionElement;
            public int DeltaElement;
            public int ColorElement;
            public LineMeshBuffer(Material material) : base(material) {
                InstanceBuffer = new(BufferLayoutPersistent.Usages.Instance);
                PositionElement = InstanceBuffer.AppendElement(new("LINEPOSITION", BufferFormat.FORMAT_R32G32B32A32_FLOAT));
                DeltaElement = InstanceBuffer.AppendElement(new("LINEDELTA", BufferFormat.FORMAT_R32G32B32A32_FLOAT));
                ColorElement = InstanceBuffer.AppendElement(new("LINECOLOR", BufferFormat.FORMAT_R8G8B8A8_UNORM));
                Mesh.SetVertexCount(4);
                Mesh.SetIndexCount(6);
                Span<Vector3> positions = stackalloc Vector3[] { new(0f, 0f, 0f), new(0f, 0f, 0f), new(0f, 0f, 1f), new(0f, 0f, 1f) };
                Span<Vector2> uvs = stackalloc Vector2[] { new(0f, 0f), new(1f, 0f), new(0f, 1f), new(1f, 1f) };
                Mesh.GetPositionsV<Vector3>().Set(positions);
                Mesh.GetTexCoordsV<Vector2>().Set(uvs);
                Mesh.GetColorsV<Color>().Set(Color.White);
                Mesh.GetTangentsV<Vector3>().Set(Vector3.Zero);
                Span<uint> quadInds = stackalloc uint[] { 0, 1, 3, 0, 3, 2, };
                Mesh.GetIndicesV().Set(quadInds);
            }
            public void Dispose() {
                base.Dispose();
                InstanceBuffer.Dispose();
            }
        }

        private static LineMeshBuffer lineBuffer;
        private static MeshBuffer polygonBuffer;
        private static MeshBuffer textBuffer;

        public static Font Font;

        private struct Batch {
            public MeshBuffer Buffer;
            public int Begin, Count;
        }
        private static List<Batch> batches = new();

        static Handles() {
            Font = Resources.LoadFont("./Assets/Roboto-Regular.ttf");

            var lineMat = new Material(Resources.LoadShader("./Assets/handles.hlsl", "VSLine"), Resources.LoadShader("./Assets/handles.hlsl", "PSLine"));
            lineMat.SetRasterMode(RasterMode.MakeNoCull());
            lineMat.SetBlendMode(BlendMode.MakeAlphaBlend());
            lineMat.SetDepthMode(DepthMode.MakeOff());
            lineBuffer = new(lineMat);
            var polyMat = new Material(Resources.LoadShader("./Assets/handles.hlsl", "VSMain"), Resources.LoadShader("./Assets/handles.hlsl", "PSMain"));
            polyMat.SetBlendMode(BlendMode.MakeAlphaBlend());
            polyMat.SetDepthMode(DepthMode.MakeOff());
            polygonBuffer = new(polyMat);
            var textMat = new Material(Resources.LoadShader("./Assets/handles.hlsl", "VSText"), Resources.LoadShader("./Assets/handles.hlsl", "PSText"));
            textMat.SetRasterMode(RasterMode.MakeNoCull());
            textMat.SetBlendMode(BlendMode.MakeAlphaBlend());
            textMat.SetDepthMode(DepthMode.MakeOff());
            textMat.SetTexture("Texture", Font.Texture);
            textBuffer = new(textMat);
            Reset();
        }
        public static void Reset() {
            RenderHash = 0;
            matrix = Matrix4x4.Identity;
            color = Color.White;
            lineBuffer.Clear();
            textBuffer.Clear();
            polygonBuffer.Clear();
            batches.Clear();
        }


        private struct RenderData {
            public CSPipeline Pipeline;
            public MemoryBlock<nint> Resources;
        }
        unsafe public static void Render(CSGraphics graphics, ScenePass pass) {
            if (!pass.TagsToInclude.Has(RenderTag.Transparent)) return;
            using var materials = new PooledArray<Material>(3);
            materials[2] = pass.Scene.RootMaterial;
            materials[1] = pass.GetPassMaterial();
            Dictionary<MeshBuffer, RenderData> renderMap = new();
            for (int i = 0; i < batches.Count; i++) {
                var batch = batches[i];
                var instanced = batch.Buffer.InstanceBuffer.IsValid;
                var instances = 1;
                var buffers = graphics.RequireFrameData<CSBufferLayout>(instanced ? 3 : 2);
                buffers[0] = batch.Buffer.Mesh.IndexBuffer;
                buffers[1] = batch.Buffer.Mesh.VertexBuffer;
                if (instanced) {
                    buffers[2] = batch.Buffer.InstanceBuffer;
                    buffers[2].mOffset = batches[i].Begin;
                    buffers[2].mCount = batches[i].Count;
                    buffers[2].revision = batch.Buffer.RenderHash;
                    instances = batches[i].Count;
                } else {
                    buffers[0].mOffset = batches[i].Begin;
                    buffers[0].mCount = batches[i].Count;
                    buffers[0].revision = batch.Buffer.RenderHash;
                    buffers[1].revision = batch.Buffer.RenderHash;
                }
                if (!renderMap.TryGetValue(batch.Buffer, out var renderData)) {
                    materials[0] = batch.Buffer.Material;
                    renderData.Pipeline = MaterialEvaluator.ResolvePipeline(graphics, buffers, materials);
                    renderData.Resources = MaterialEvaluator.ResolveResources(graphics, renderData.Pipeline, materials);
                    renderMap[batch.Buffer] = renderData;
                }
                pass.RenderQueue.AppendMesh("Handles", renderData.Pipeline, buffers, renderData.Resources, instances);
            }
        }

        private static void PushBatch(MeshBuffer buffer, int baseIndex, int count, int renderHash) {
            if (batches.Count > 0 && batches[^1].Buffer == buffer) {
                var lastBatch = batches[^1];
                Debug.Assert(lastBatch.Begin + lastBatch.Count == baseIndex);
                lastBatch.Count += count;
                batches[^1] = lastBatch;
            } else {
                batches.Add(new Batch() {
                    Buffer = buffer,
                    Begin = baseIndex,
                    Count = count,
                });
            }
            buffer.RenderHash = buffer.RenderHash * 123 + renderHash;
            RenderHash = RenderHash * 123 + renderHash;
        }

        public static void DrawAAConvexPolygon(params Vector3[] points) {
            DrawAAConvexPolygon(points.AsSpan());
        }
        public static void DrawAAConvexPolygon(Span<Vector3> points) {
            if (points.Length < 3) return;
            var renderHash = color.GetHashCode();
            var vertices = polygonBuffer.Mesh.AppendVerts(points.Length);
            var indRange = polygonBuffer.Mesh.AppendIndices((points.Length - 2) * 3);
            var positions = vertices.GetPositions();
            var tangents = vertices.GetTangents();
            vertices.GetColors().Set(color * ColorFactor);
            for (int i = 0; i < vertices.Count; i++) {
                var pos = Vector3.Transform(points[i], matrix);
                positions[i] = pos;
                tangents[i] = default;
                renderHash += pos.GetHashCode();
            }
            var indices = indRange.GetIndices();
            for (int i = 0; i < vertices.Count - 2; ++i) {
                indices[i * 3 + 0] = (uint)(vertices.BaseVertex + i);
                indices[i * 3 + 1] = (uint)(vertices.BaseVertex + i + 1);
                indices[i * 3 + 2] = (uint)(vertices.BaseVertex + vertices.Count - 1);
            }
            PushBatch(polygonBuffer, indRange.BaseIndex, indRange.Count,
                renderHash);
        }

        public static void DrawAAPolyLine(params Vector3[] points) {
            DrawAAPolyLine(points.AsSpan());
        }
        public static void DrawAAPolyLine(Span<Vector3> points) {
            for (int i = 1; i < points.Length; i++) {
                DrawLine(points[i - 1], points[i]);
            }
        }

        public static void DrawLine(Vector3 from, Vector3 to, float thickness = 1f) {
            DrawLine(from, to, Color.White, thickness);
        }
        public static void DrawLine(Vector3 from, Vector3 to, Color color, float thickness = 1f) {
            color *= Handles.color * ColorFactor;
            from = Vector3.Transform(from, matrix);
            to = Vector3.Transform(to, matrix);

            if (lineBuffer.InstanceBuffer.IsValid) {
                var range = new RangeInt(lineBuffer.InstanceBuffer.Count, 1);
                lineBuffer.InstanceBuffer.RequireCount(lineBuffer.InstanceBuffer.Count + 1);
                var linePosition = new TypedBufferView<Vector4>(lineBuffer.InstanceBuffer.Elements[lineBuffer.PositionElement], range);
                var lineDelta = new TypedBufferView<Vector4>(lineBuffer.InstanceBuffer.Elements[lineBuffer.DeltaElement], range);
                var lineColor = new TypedBufferView<Color>(lineBuffer.InstanceBuffer.Elements[lineBuffer.ColorElement], range);

                linePosition.Set(new Vector4(from, 0.0f));
                lineDelta.Set(new Vector4(to - from, thickness));
                lineColor.Set(color);

                PushBatch(lineBuffer, range.Start, range.Length,
                    color.GetHashCode() + from.GetHashCode() ^ to.GetHashCode());
            }

            /*var vertices = lineBuffer.Mesh.AppendVerts(4);
            var indices = lineBuffer.Mesh.AppendIndices(6);
            Span<uint> quadInds = stackalloc uint[] { 0, 1, 3, 0, 3, 2, };
            foreach (ref var i in quadInds) i += (uint)vertices.BaseVertex;
            indices.GetIndices().Set(quadInds);
            Span<Vector2> quadUVs = stackalloc[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), };
            vertices.GetTangents().Set(Vector3.Normalize(to - from) * thickness);
            vertices.GetTexCoords().Set(quadUVs);
            vertices.GetColors().Set(color);
            Span<Vector3> quadPos = stackalloc[] { from, from, to, to, };
            vertices.GetPositions().Set(quadPos);
            PushBatch(lineBuffer, indices.BaseIndex, indices.Count,
                color.GetHashCode() + from.GetHashCode() ^ to.GetHashCode());*/
        }

        public static void Label(Vector3 position, string label) {
            position = Vector3.Transform(position, matrix);
            CanvasText text = new();
            text.Text = label;
            text.Font = Font;
            text.FontSize = 12;
            text.Alignment = TextAlignment.Left;
            text.Color = color * ColorFactor;
            CanvasLayout layout = CanvasLayout.MakeBox(new Vector2(200f, 0f));
            //layout.Position += position;
            int vstart = textBuffer.Mesh.VertexCount;
            var indices = text.WriteToMesh(textBuffer.Mesh, layout);
            if (indices.Length == 0) return;
            textBuffer.Mesh.GetTangentsV().Slice(vstart).Set(position);
            PushBatch(textBuffer, indices.Start, indices.Length,
                color.GetHashCode() + position.GetHashCode() ^ label.GetHashCode());
            text.Dispose(default!);
        }

    }

    public static class Gizmos {
        public static void DrawWireCube(Vector3 centre, Vector3 size) {
            DrawWireCube(centre, size, Color.White);
        }
        public static void DrawWireCube(Vector3 centre, Vector3 size, Color color) {
            Vector3 min = centre - size / 2, max = centre + size / 2;
            for (int a = 0; a < 3; a++) {
                var axis1 = (a + 1) % 3;
                var axis2 = (a + 2) % 3;
                for (int q = 0; q < 4; q++) {
                    Vector3 p0 = min, p1 = max;
                    p0[axis1] = p1[axis1] = (q < 2 ? min : max)[axis1];
                    p0[axis2] = p1[axis2] = ((q % 2) == 0 ? min : max)[axis2];
                    Handles.DrawLine(p0, p1, color: color);
                }
            }
        }
    }
}
