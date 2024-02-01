using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using Weesals.Engine;
using Weesals.Utility;

namespace Navigation {

    public class NavDebug {

        public NavMesh2Baker NavBaker;

        public bool ShowTriangleLabels = true;
        public bool ShowCornerLabels = true;
        public bool ShowAdjacency = true;

        public void Initialise(NavMesh2Baker baker) {
            NavBaker = baker;
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
            var mesh = NavBaker.NavMesh.GetReadOnly();

            /*foreach (var edge in NavBaker.pinnedEdges) {
                Gizmos.DrawLine(
                    mesh.GetCorner(edge.Corner1).ToUVector3(0f),
                    mesh.GetCorner(edge.Corner2).ToUVector3(0f)
                );
            }

            return;*/
            var validTriangles = new PooledHashSet<ushort>(32);
            for (var it = mesh.GetTriangleEnumerator(); it.MoveNext();) {
                validTriangles.Add((ushort)it.Index);
            }

            if (ShowCornerLabels) {
                using (new HandlesColor(Color.Blue)) {
                    for (var it = NavBaker.NavMesh.GetCornerEnumerator(); it.MoveNext();) {
                        Handles.Label(it.Current.ToUVector3(0f), "C" + it.Index);
                    }
                }
            }
            //var frustum = new Frustum4(Camera.current);
            for (var it = mesh.GetTriangleEnumerator(); it.MoveNext();) {
                var tri = it.Current;
                var c1 = mesh.GetCorner(tri.C1).ToUVector3(0f);
                var c2 = mesh.GetCorner(tri.C2).ToUVector3(0f);
                var c3 = mesh.GetCorner(tri.C3).ToUVector3(0f);
                using (new HandlesColor(GetColor(tri.Type).WithAlpha(32))) {
                    Handles.DrawAAConvexPolygon(c1, c2, c3);
                }
            }
            if (ShowTriangleLabels) {
                for (var it = mesh.GetTriangleEnumerator(); it.MoveNext();) {
                    var tri = it.Current;
                    var c1 = mesh.GetCorner(tri.C1).ToUVector3(0f);
                    var c2 = mesh.GetCorner(tri.C2).ToUVector3(0f);
                    var c3 = mesh.GetCorner(tri.C3).ToUVector3(0f);
                    Handles.Label((c1 + c2 + c3) / 3f, ("T" + it.Index)
                    //, validTriangles.Contains((ushort)it.Index) ? EditorStyles.whiteLabel : EditorStyles.boldLabel
                    );
                }
            }
            for (var it = mesh.GetTriangleEnumerator(); it.MoveNext();) {
                var tri = it.Current;
                var c1 = mesh.GetCorner(tri.C1).ToUVector3(0f);
                var c2 = mesh.GetCorner(tri.C2).ToUVector3(0f);
                var c3 = mesh.GetCorner(tri.C3).ToUVector3(0f);
                var ccw = !NavUtility.IsCW(mesh.GetCorner(tri.C1), mesh.GetCorner(tri.C2), mesh.GetCorner(tri.C3));
                using (new HandlesColor((ccw ? Color.Red : Color.White.WithAlpha(100)))) {
                    Handles.DrawAAPolyLine(c1, c2, c3, c1);
                }
                if (ShowAdjacency) {
                    DrawEdge(new TriangleEdge((ushort)it.Index, 0), c1, c2, validTriangles);
                    DrawEdge(new TriangleEdge((ushort)it.Index, 1), c2, c3, validTriangles);
                    DrawEdge(new TriangleEdge((ushort)it.Index, 2), c3, c1, validTriangles);
                }
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
                (nedge.EdgeId == 0 ? Color.Yellow : Color.Cyan).WithAlpha(20);
            using (new HandlesColor(color)) {
                //Gizmos.DrawLine(Vector3.Lerp(triP, ePos, 0.5f), ePos);
                Handles.DrawLine(Vector3.Lerp(othP, ePos, 0.5f), ePos);
            }
        }
#endif

    }
}
