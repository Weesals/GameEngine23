using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using Weesals.Editor;
using Weesals.Engine;
using Weesals.Utility;

namespace Navigation {

    public class NavDebug {

        public NavMesh2Baker NavBaker;

        [EditorField]
        public bool ShowTriangleLabels = false;
        [EditorField]
        public bool ShowCornerLabels = false;
        [EditorField]
        public bool ShowAdjacency = false;

        public void Initialise(NavMesh2Baker baker) {
            NavBaker = baker;
        }

        [EditorButton]
        public void Repair() {
            var mutator = new NavMesh2Baker.Mutator(NavBaker);
            mutator.RepairSwap();
        }

#if UNITY_EDITOR || true
        public struct HandlesColor : IDisposable {
            Color oldCol;
            public HandlesColor(Color color) {
                oldCol = Handles.color;
                Handles.color = oldCol * color;
            }
            public void Dispose() { Handles.color = oldCol; }
        }
        public void OnDrawGizmosSelected() {
            if (NavBaker == null || !NavBaker.IsCreated) return;
            var ro = NavBaker.NavMesh.GetReadOnly();
            var aj = NavBaker.NavMesh.GetAdjacency();

            /*foreach (var edge in NavBaker.pinnedEdges) {
                Gizmos.DrawLine(
                    mesh.GetCorner(edge.Corner1).ToUVector3(0f),
                    mesh.GetCorner(edge.Corner2).ToUVector3(0f)
                );
            }

            return;*/
            var adjacency = new PooledHashSet<Edge>(ro.NavMesh.adjacency.Count);
            foreach (var adj in ro.NavMesh.adjacency) adjacency.Add(adj.Key);
            var adjacency2 = new PooledHashMap<Edge, Int2>(ro.NavMesh.adjacency.Count);

            var validTriangles = new PooledHashSet<ushort>(32);
            for (var it = ro.GetTriangleEnumerator(); it.MoveNext();) {
                validTriangles.Add((ushort)it.Index);
            }

            if (ShowCornerLabels) {
                using (new HandlesColor(Color.Cyan)) {
                    for (var it = NavBaker.NavMesh.GetCornerEnumerator(); it.MoveNext();) {
                        Handles.Label(it.Current.ToUVector3(0f), "C" + it.Index);
                    }
                }
            }
            //var frustum = new Frustum4(Camera.current);
            for (var it = ro.GetTriangleEnumerator(); it.MoveNext();) {
                var tri = it.Current;
                var c1 = ro.GetCorner(tri.C1).ToUVector3(0f);
                var c2 = ro.GetCorner(tri.C2).ToUVector3(0f);
                var c3 = ro.GetCorner(tri.C3).ToUVector3(0f);
                adjacency.Remove(tri.GetEdge(0));
                adjacency.Remove(tri.GetEdge(1));
                adjacency.Remove(tri.GetEdge(2));
                using (new HandlesColor(GetColor(tri.Type).WithAlpha(32))) {
                    Handles.DrawAAConvexPolygon(c1, c2, c3);
                }
                for (int i = 0; i < 3; i++) {
                    var edge = tri.GetEdge(i);
                    var sign = edge.GetSign(tri.GetCorner(i));
                    if (!adjacency2.TryGetValue(edge, out var item)) item = -1;
                    item[sign ? 0 : 1] = it.Index;
                }
            }
            if (ShowTriangleLabels) {
                for (var it = ro.GetTriangleEnumerator(); it.MoveNext();) {
                    var tri = it.Current;
                    var c1 = ro.GetCorner(tri.C1).ToUVector3(0f);
                    var c2 = ro.GetCorner(tri.C2).ToUVector3(0f);
                    var c3 = ro.GetCorner(tri.C3).ToUVector3(0f);
                    Handles.Label((c1 + c2 + c3) / 3f, ("T" + it.Index)
                    //, validTriangles.Contains((ushort)it.Index) ? EditorStyles.whiteLabel : EditorStyles.boldLabel
                    );
                }
            }
            for (var it = ro.GetTriangleEnumerator(); it.MoveNext();) {
                var tri = it.Current;
                var ccw = !NavUtility.IsCW(ro.GetCorner(tri.C1), ro.GetCorner(tri.C2), ro.GetCorner(tri.C3));
                for (int i = 0; i < 3; i++) {
                    var c1 = ro.GetCorner(tri.GetCorner(i)).ToUVector3(0f);
                    var c2 = ro.GetCorner(tri.GetCornerWrapped(i + 1)).ToUVector3(0f);
                    var color = ccw ? Color.Red : Color.White.WithAlpha(100);
                    if (NavBaker.pinnedEdges.Contains(tri.GetEdge(i))) {
                        color = Color.Yellow;
                    }
                    using (new HandlesColor(color)) {
                        Handles.DrawLine(c1, c2);
                    }
                    if (ShowAdjacency) {
                        DrawEdge(new TriangleEdge((ushort)it.Index, (ushort)i), c1, c2, validTriangles);
                    }
                }
            }
            foreach (var item in aj.adjacency) {
                var edge = item.Key;
                adjacency2.TryGetValue(edge, out var triAj);
                if (triAj.X != item.Value.Triangle1 || triAj.Y != item.Value.Triangle2) {
                    Handles.DrawLine(ro.GetCorner(edge.Corner1).ToUVector3(0f),
                        ro.GetCorner(edge.Corner1).ToUVector3(0f), Color.Red, 2.0f);
                }
            }
            foreach (var edge in adjacency) {
                Handles.DrawLine(ro.GetCorner(edge.Corner1).ToUVector3(0f),
                    ro.GetCorner(edge.Corner1).ToUVector3(0f), Color.Red, 2.0f);
            }
            Handles.matrix = Matrix4x4.Identity;
            /*var mray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
            var mpos = mray.ProjectTo(Vector3.up, Vector3.zero);
            var ro = mesh;
            var aj = NavBaker.NavMesh.GetAdjacency();
            var items = new NativeList<ushort>(16, Allocator.Temp);
            var targetPnt = Coordinate.FromFloat2(((float3)mpos).xz);
            var initialTriI = aj.triangleGrid.FindTriangleAt((int2)targetPnt >> NavMesh.TriGridShift, out _);
            if (initialTriI == NavMesh2Baker.InvalidTriId) initialTriI = 0;
            var target = aj.MoveTo(ro, initialTriI, targetPnt, items);
            if (items.Length > 2) {
                var points = new Vector3[items.Length];
                for (int i = 0; i < points.Length; i++) {
                    points[i] = NavBaker.GetCentre(items[i]);
                }
                Handles.DrawAAPolyLine(points);
                Debug.Log(items.Length);
            }
            Gizmos.DrawCube(NavBaker.GetCentre(target), Vector3.one);*/
        }

        private Color GetColor(TriangleType type) {
            int id = type.TypeId * 2;
            return Color.FromFloat(MathF.Sin(id) * 0.5f + 0.5f, MathF.Sin(id + 2) * 0.5f + 0.5f, MathF.Sin(id + 4) * 0.5f + 0.5f);
        }

        private void DrawEdge(TriangleEdge edge, Vector3 c1, Vector3 c2, PooledHashSet<ushort> validTriangles) {
            var nedge = NavBaker.GetAdjacentEdge(edge);
            if (!nedge.IsValid) return;
            var nedge2 = NavBaker.GetAdjacentEdge(nedge);
            var ePos = Vector3.Lerp(c1, c2, 0.48f);
            //Gizmos.DrawWireSphere(ePos, 0.1f);
            //var triP = NavMesh.GetCentre(triI);
            var othP = NavBaker.GetCentre(nedge.TriangleId);
            var color =
                !nedge2.Equals(edge) ? Color.Red :
                NavBaker.GetCornerIndex(nedge.NextEdge()) != NavBaker.GetCornerIndex(edge) ? Color.Red :
                NavBaker.GetCornerIndex(edge.NextEdge()) != NavBaker.GetCornerIndex(nedge) ? Color.Red :
                !validTriangles.Contains(nedge.TriangleId) ? Color.Red :
                !validTriangles.Contains(nedge2.TriangleId) ? Color.Red :
                (nedge.EdgeId == 0 ? Color.Green : Color.Cyan).WithAlpha(64);
            using (new HandlesColor(color)) {
                //Gizmos.DrawLine(Vector3.Lerp(triP, ePos, 0.5f), ePos);
                Handles.DrawLine(Vector3.Lerp(othP, ePos, 0.5f), ePos);
            }
        }
#endif

    }
}
