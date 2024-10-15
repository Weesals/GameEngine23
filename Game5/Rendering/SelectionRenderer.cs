using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Game5.Game;
using Weesals;
using Weesals.Engine;
using Weesals.UI;

namespace Game5.Rendering {
    public class SelectionRenderer {

        public readonly SelectionManager Selection;

        private Mesh reticuleMesh;
        private Material reticuleMaterial;
        private MeshDrawInstanced instancedDraw;
        private int instances_PosSize;
        private int instances_PlayerId;

        public struct SelectionData {
            public ItemReference Item;
            public float TimeSelected;
        }
        private List<SelectionData> selectionData = new();

        public SelectionRenderer(SelectionManager selection) {
            Selection = selection;
            Selection.OnEntitySelected += Selection_OnEntitySelected;

            reticuleMesh = GenerateMesh(true);
            reticuleMaterial = new(Resources.LoadShader("./Assets/selection.hlsl", "VSMain"), Resources.LoadShader("./Assets/selection.hlsl", "PSMain"));
            reticuleMaterial.SetBlendMode(BlendMode.MakeAlphaBlend());
            reticuleMaterial.SetDepthMode(DepthMode.MakeReadOnly());
            //reticuleMaterial.SetRasterMode(RasterMode.MakeNoCull());
            instancedDraw = new(reticuleMesh, reticuleMaterial);
            instances_PosSize = instancedDraw.AddInstanceElement("INST_POSSIZE", BufferFormat.FORMAT_R32G32B32A32_FLOAT);
            instances_PlayerId = instancedDraw.AddInstanceElement("INST_PLAYERID", BufferFormat.FORMAT_R32G32B32A32_FLOAT);
        }

        private void Selection_OnEntitySelected(ItemReference entity, bool selected) {
            if (selected) {
                selectionData.Add(new SelectionData() {
                    Item = entity,
                    TimeSelected = UnityEngine.Time.time,
                });
            } else {
                int index = 0;
                for (; index < selectionData.Count; index++) {
                    if (selectionData[index].Item == entity) break;
                }
                if (index < selectionData.Count) {
                    selectionData.RemoveAt(index);
                }
            }
        }

        public void Draw(CSGraphics graphics, RenderQueue queue) {
            if (selectionData.Count == 0) return;

            instancedDraw.SetInstanceCount(selectionData.Count);
            var instancesPositions = instancedDraw.GetElementData<Vector4>(instances_PosSize);
            var instancesPlayerIds = instancedDraw.GetElementData<Vector4>(instances_PlayerId);
            int index = 0;
            foreach (var item in selectionData) {
                var pos = item.Item.GetWorldPosition();
                var size = new Vector2(1f, 1f);
                var isBox = false;
                var entity = item.Item.GetAccessor();
                if (entity.IsValid) {
                    var protoData = entity.GetComponent<PrototypeData>();
                    size = SimulationWorld.WorldScale * (Vector2)protoData.Footprint.Size;
                    isBox = protoData.Footprint.Shape == EntityFootprint.Shapes.Box;
                }
                instancesPositions[index] = new(pos, item.TimeSelected);
                instancesPlayerIds[index] = new(size.X, size.Y, item.Item.TryGetOwnerId(), isBox ? 1f : 0f);
                ++index;
            }
            instancedDraw.RevisionFromDataHash();
            instancedDraw.Draw(graphics, queue, CSDrawConfig.Default);
        }

        private static Mesh GenerateMesh(bool filled) {
            const int Segments = 4;
            var mesh = new Mesh("Selection Reticle");
            using var vertices = new PooledList<Vector3>(8);
            using var uvs = new PooledList<Vector3>(8);
            using var normals = new PooledList<Vector3>(8);
            using var indices = new PooledList<ushort>(8);
            for (int q = 0; q < 4; q++) {
                int x = q < 2 ? q : 3 - q;
                int y = q / 2;
                for (int a = 0; a <= Segments; a++) {
                    var aF = a / (float)Segments;
                    var ang = (q + aF) * MathF.PI / 2f;
                    var dir = new Vector2(-MathF.Cos(ang), -MathF.Sin(ang));
                    for (int d = 0; d < 2; d++) {
                        var vpos = dir * (0.15f + d * 0.1f);
                        vertices.Add(new Vector3(vpos.X, 0, vpos.Y));
                        uvs.Add(new Vector3(x, y, d));
                        normals.Add(new Vector3(dir.X, 0f, dir.Y));
                    }
                }
            }
            for (int i = 0; i < vertices.Count; i += 2) {
                int i2 = (i + 2) % vertices.Count;
                indices.Add((ushort)(i + 1));
                indices.Add((ushort)(i + 0));
                indices.Add((ushort)(i2 + 0));
                indices.Add((ushort)(i + 1));
                indices.Add((ushort)(i2 + 0));
                indices.Add((ushort)(i2 + 1));
            }
            if (filled) {
                int count = 4 * (Segments + 1);
                for (int i = 2; i < count; ++i) {
                    indices.Add(0);
                    indices.Add((ushort)(i * 2 + 0));
                    indices.Add((ushort)(i * 2 - 2));
                }
            }
            mesh.SetVertexCount(vertices.Count);
            mesh.RequireVertexTexCoords(0, BufferFormat.FORMAT_R32G32B32_FLOAT);
            mesh.RequireVertexNormals();

            mesh.GetPositionsV().Set(vertices);
            mesh.GetTexCoordsV<Vector3>(0).Set(uvs);
            mesh.GetNormalsV().Set(normals);
            mesh.SetIndices(indices);

            mesh.MarkChanged();

            mesh.CalculateBoundingBox();

            return mesh;
        }

    }
}
