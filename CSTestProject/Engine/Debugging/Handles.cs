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

        private class MeshBuffer : IDisposable {
            public Material Material;
            public DynamicMesh Mesh;
            public int RenderHash;
            public MeshBuffer(Material material) {
                Material = material;
                Mesh = new("DynMesh");
                Mesh.RequireVertexPositions();
                Mesh.RequireVertexTexCoords(0);
                Mesh.RequireVertexColors();
            }
            public void Dispose() {
                Mesh.Dispose();
            }
            public void Clear() {
                RenderHash = 0;
                Mesh.SetVertexCount(0);
                Mesh.SetIndexCount(0);
            }
        }

        private static MeshBuffer lineBuffer;
        private static MeshBuffer polygonBuffer;
        private static MeshBuffer textBuffer;

        public static CSFont Font;

        private struct Batch {
            public MeshBuffer Buffer;
            public int Begin, Count;
        }
        private static List<Batch> batches = new();

        static Handles() {
            Font = CSResources.LoadFont("./assets/Roboto-Regular.ttf");

            var lineMat = new Material(Resources.LoadShader("./assets/handles.hlsl", "VSLine"), Resources.LoadShader("./assets/handles.hlsl", "PSLine"));
            lineMat.SetRasterMode(RasterMode.MakeNoCull());
            lineMat.SetBlendMode(BlendMode.MakeAlphaBlend());
            lineMat.SetDepthMode(DepthMode.MakeOff());
            lineBuffer = new(lineMat);
            lineBuffer.Mesh.RequireVertexTangents();
            var polyMat = new Material(Resources.LoadShader("./assets/handles.hlsl", "VSMain"), Resources.LoadShader("./assets/handles.hlsl", "PSMain"));
            polyMat.SetBlendMode(BlendMode.MakeAlphaBlend());
            polyMat.SetDepthMode(DepthMode.MakeOff());
            polygonBuffer = new(polyMat);
            polygonBuffer.Mesh.RequireVertexTangents();
            var textMat = new Material(Resources.LoadShader("./assets/handles.hlsl", "VSText"), Resources.LoadShader("./assets/handles.hlsl", "PSText"));
            textMat.SetRasterMode(RasterMode.MakeNoCull());
            textMat.SetBlendMode(BlendMode.MakeAlphaBlend());
            textMat.SetDepthMode(DepthMode.MakeOff());
            textMat.SetTexture("Texture", Font.GetTexture());
            textBuffer = new(textMat);
            textBuffer.Mesh.RequireVertexTangents();
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
                var buffers = graphics.RequireFrameData<CSBufferLayout>(2);
                buffers[0] = batch.Buffer.Mesh.IndexBuffer;
                buffers[1] = batch.Buffer.Mesh.VertexBuffer;
                buffers[0].mOffset = batches[i].Begin;
                buffers[0].mCount = batches[i].Count;
                buffers[0].revision = batch.Buffer.RenderHash;
                buffers[1].revision = batch.Buffer.RenderHash;
                if (!renderMap.TryGetValue(batch.Buffer, out var renderData)) {
                    materials[0] = batch.Buffer.Material;
                    renderData.Pipeline = MaterialEvaluator.ResolvePipeline(graphics, buffers, materials);
                    renderData.Resources = MaterialEvaluator.ResolveResources(graphics, renderData.Pipeline, materials);
                    renderMap[batch.Buffer] = renderData;
                }
                pass.RenderQueue.AppendMesh("Handles", renderData.Pipeline, buffers, renderData.Resources);
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
            vertices.GetColors().Set(color);
            for (int i = 0; i < vertices.Count; i++) {
                var pos = Vector3.Transform(points[i], matrix);
                positions[i] = pos;
                tangents[i] = default;
                renderHash += pos.GetHashCode();
            }
            var indices = indRange.GetIndices();
            for (int i = 0; i < vertices.Count - 2; ++i) {
                indices[i * 3 + 0] = vertices.BaseVertex + i;
                indices[i * 3 + 1] = vertices.BaseVertex + i + 1;
                indices[i * 3 + 2] = vertices.BaseVertex + vertices.Count - 1;
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
            from = Vector3.Transform(from, matrix);
            to = Vector3.Transform(to, matrix);
            var vertices = lineBuffer.Mesh.AppendVerts(4);
            var indices = lineBuffer.Mesh.AppendIndices(6);
            Span<int> quadInds = stackalloc[] { 0, 1, 3, 0, 3, 2, };
            foreach (ref var i in quadInds) i += vertices.BaseVertex;
            indices.GetIndices().Set(quadInds);
            Span<Vector2> quadUVs = stackalloc[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), };
            vertices.GetTangents().Set(to - from);
            vertices.GetTexCoords().Set(quadUVs);
            vertices.GetColors().Set(color * Handles.color);
            Span<Vector3> quadPos = stackalloc[] { from, from, to, to, };
            vertices.GetPositions().Set(quadPos);
            PushBatch(lineBuffer, indices.BaseIndex, indices.Count,
                color.GetHashCode() + from.GetHashCode() ^ to.GetHashCode());
        }

        public static void Label(Vector3 position, string label) {
            position = Vector3.Transform(position, matrix);
            CanvasText text = new();
            text.Text = label;
            text.Font = Font;
            text.FontSize = 10;
            text.Alignment = TextAlignment.Left;
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
            Vector3 min = centre - size / 2, max = centre + size / 2;
            for (int a = 0; a < 3; a++) {
                var axis1 = (a + 1) % 3;
                var axis2 = (a + 2) % 3;
                for (int q = 0; q < 4; q++) {
                    Vector3 p0 = min, p1 = max;
                    p0[axis1] = p1[axis1] = (q < 2 ? min : max)[axis1];
                    p0[axis2] = p1[axis2] = ((q % 2) == 0 ? min : max)[axis2];
                    Handles.DrawLine(p0, p1);
                }
            }
        }
    }
}
