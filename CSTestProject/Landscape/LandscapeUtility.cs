using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.Utility;

namespace Weesals.Landscape {
    public static class LandscapeUtility {

        // How tall an equilateral triangle is vs its width
        public static readonly float TriAspect = MathF.Sqrt(3f) / 2f;
        public static readonly float TriH = 1f / MathF.Sqrt(3f);

        public static Mesh GenerateSubdividedTriangle(int triSize) {
            int DS = (triSize * 2 + 3);
            var tileMesh = new Mesh("TriTile");
            using var vertices = new PooledList<Vector3>();
            using var normals = new PooledList<Vector3>();
            using var indices = new PooledList<ushort>();
            for (int y = 0; y <= triSize; y++) {
                for (int x = 0; x <= (triSize - y); x++) {
                    var pos = new Vector3(x, 0, y);
                    pos.X += pos.Z * 0.5f;
                    pos.Z *= TriAspect;
                    vertices.Add(pos);
                    normals.Add(Vector3.UnitY);
                    if (y > 0) {
                        int r0 = (y - 1) * (DS - (y - 1)) / 2,
                                r1 = (y + 0) * (DS - (y + 0)) / 2;
                        indices.Add((ushort)(r0 + x));
                        indices.Add((ushort)(r1 + x));
                        indices.Add((ushort)(r0 + x + 1));
                        if (x > 0) {
                            indices.Add((ushort)(r1 + x - 1));
                            indices.Add((ushort)(r1 + x));
                            indices.Add((ushort)(r0 + x));
                        }
                    }
                }
            }
            tileMesh.RequireVertexNormals(BufferFormat.FORMAT_R8G8B8A8_UNORM);
            tileMesh.SetIndexFormat(false);
            tileMesh.SetVertexCount(vertices.Count);
            tileMesh.GetPositionsV().Set(vertices);
            tileMesh.GetNormalsV().Set(normals);
            tileMesh.SetIndices(indices);
            tileMesh.MarkChanged();
            return tileMesh;
        }
        public static Mesh GenerateSubdividedQuad(int xcount, int ycount, bool useXZ = true, bool enableNormals = true, bool enableUvs = false) {
            var tileMesh = new Mesh("LandscapeTile");
            tileMesh.SetVertexCount((xcount + 1) * (ycount + 1));
            tileMesh.SetIndexCount(xcount * ycount * 6);
            if (enableNormals)
                tileMesh.RequireVertexNormals(BufferFormat.FORMAT_R8G8B8A8_SNORM);
            if (enableUvs)
                tileMesh.RequireVertexTexCoords(0, BufferFormat.FORMAT_R16G16_UNORM);
            tileMesh.SetIndexFormat(false);
            var vpositions = tileMesh.GetPositionsV();
            var vnormals = tileMesh.GetNormalsV();
            var vuvs = tileMesh.GetTexCoordsV();
            for (int y = 0; y < ycount + 1; ++y) {
                for (int x = 0; x < xcount + 1; ++x) {
                    int v = x + y * (xcount + 1);
                    var pos = new Vector3(x, useXZ ? 0 : 1 - y, useXZ ? y : 0);
                    vpositions[v] = pos;
                    if (enableNormals)
                        vnormals[v] = new Vector3(0.0f, 1.0f, 0.0f);
                    if (enableUvs)
                        vuvs[v] = new Vector2((float)x / xcount, (float)y / ycount);
                }
            }
            var indices = tileMesh.GetIndicesV<ushort>();
            for (int y = 0; y < ycount; ++y) {
                for (int x = 0; x < xcount; ++x) {
                    int i = (x + y * xcount) * 6;
                    var v0 = (uint)(x + (y + 0) * (xcount + 1));
                    var v1 = (uint)(x + (y + 1) * (xcount + 1));
                    indices[i + 0] = (ushort)(v0);
                    indices[i + 1] = (ushort)(v1 + 1);
                    indices[i + 2] = (ushort)(v0 + 1);
                    indices[i + 3] = (ushort)(v0);
                    indices[i + 4] = (ushort)(v1);
                    indices[i + 5] = (ushort)(v1 + 1);
                }
            }
            tileMesh.MarkChanged();
            return tileMesh;
        }

    }
}
