using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Weesals.Engine;
using Weesals.UI;
using Weesals.Utility;

namespace Weesals.Landscape.Editor {
    public interface IToolServiceProvider<T> {
        T Service { get; }
    }
    public struct ToolContext {
        public readonly LandscapeRenderer Landscape;
        public readonly LandscapeData LandscapeData => Landscape.LandscapeData;
        public readonly CanvasRenderable Viewport;
        public readonly Camera Camera;
        public readonly object Services;
        public T? GetService<T>() { return Services is IToolServiceProvider<T> provider ? provider.Service : default; }
        public ToolContext(LandscapeRenderer landscape, CanvasRenderable viewport, Camera camera, object services) {
            Landscape = landscape;
            Viewport = viewport;
            Camera = camera;
            Services = services;
        }
    }
    public struct BrushContext {
        public ToolContext ToolContext;
        public BrushConfiguration BrushConfiguration;
        public float BrushSize => BrushConfiguration.BrushSize;
    }

    public class BrushConfiguration {

        public int BrushSize = 14;
        public FloatCurve BrushFalloff = FloatCurve.MakeLinear();

        public Material BrushMaterial = new();
        public float BrushMeshWith = 0.3f;

        private DynamicMesh brushMesh;

        public void DrawBrush(ScenePassManager scene, LandscapeData landscapeData, ToolContext context, Vector3 pos, float radiusMin, float radiusMax, float angle) {
            if (brushMesh == null) brushMesh = new DynamicMesh("Brush");
            using var vertices = new PooledList<Vector3>();
            using var uvs = new PooledList<Vector2>();
            using var indices = new PooledList<ushort>();
            AppendRing(landscapeData, ref vertices.AsMutable(), ref uvs.AsMutable(), ref indices.AsMutable(), pos, radiusMin, 0f);
            AppendRing(landscapeData, ref vertices.AsMutable(), ref uvs.AsMutable(), ref indices.AsMutable(), pos, radiusMax, angle, 16);
            brushMesh.SetVertexCount(vertices.Count);
            brushMesh.RequireVertexTexCoords(0);
            brushMesh.RequireVertexNormals();
            brushMesh.GetPositionsV().Set(vertices);
            brushMesh.GetNormalsV().Set(Vector3.UnitY);
            brushMesh.GetTexCoordsV().Set(uvs);
            brushMesh.SetIndices(indices);
            brushMesh.MarkChanged();
            brushMesh.CalculateBoundingBox();
            scene.DrawDynamicMesh(brushMesh, Matrix4x4.Identity, BrushMaterial);
        }

        private void AppendRing(LandscapeData landscapeData
            , ref PooledList<Vector3> vertices
            , ref PooledList<Vector2> uvs
            , ref PooledList<ushort> indices, Vector3 centre, float radius, float angle, int dashes = 1) {
            var twoPi = MathF.PI * 2f;
            var angO = angle;
            for (int d = 0; d < dashes; d++) {
                var angFrom = (float)(d + 0 + angO) / dashes * twoPi;
                var angTo = (float)(d + 1 + angO) / dashes * twoPi;
                int segs = Math.Max((int)MathF.Ceiling((angTo - angFrom) * 5) + 1, 4);
                int vfrom = vertices.Count;
                for (var i = 0; i < segs; ++i) {
                    var ang = Easing.Lerp(angFrom, angTo, (float)i / segs);
                    var dir = new Vector3(MathF.Sin(ang), 0f, MathF.Cos(ang));
                    var pos = centre + dir * radius;
                    pos.Y = landscapeData.GetHeightMap().GetHeightAtF(pos.toxz()) + 0.05f;
                    vertices.Add(pos + dir * BrushMeshWith / 2f);
                    vertices.Add(pos - dir * BrushMeshWith / 2f);
                    uvs.Add(new Vector2(0f, 0f));
                    uvs.Add(new Vector2(0f, 1f));
                }
                var vcount = (vertices.Count - vfrom);
                var vend = vcount - (dashes == 1 ? 0 : 2);
                for (int i = 0; i < vend; i += 2) {
                    indices.Add((ushort)(vfrom + ((i + 0) % vcount)));
                    indices.Add((ushort)(vfrom + ((i + 3) % vcount)));
                    indices.Add((ushort)(vfrom + ((i + 1) % vcount)));
                    indices.Add((ushort)(vfrom + ((i + 0) % vcount)));
                    indices.Add((ushort)(vfrom + ((i + 2) % vcount)));
                    indices.Add((ushort)(vfrom + ((i + 3) % vcount)));
                }
            }
        }
    }

    public class UXBrushTool {
        public bool Active { get; private set; }
        public ToolContext Context;
        public LandscapeData LandscapeData => Context.LandscapeData;
        public CanvasRenderable Viewport => Context.Viewport;
        public Camera Camera => Context.Camera;
        public T? GetService<T>() { return Context.GetService<T>(); }

        private TimedEvent active;
        private float reticleAngle;

        public virtual void InitializeTool(ToolContext context) {
            Context = context;
        }

        protected BrushContext CreateBrushContext(ToolContext context) {
            return new BrushContext() {
                ToolContext = context,
                BrushConfiguration = context.GetService<BrushConfiguration>()!,
            };
        }

        protected void DrawBrush(LandscapeData landscapeData, in ToolContext context, Vector3 pos, bool _active) {
            active.SetChecked(_active);
            var scene = context.GetService<ScenePassManager>()!;
            var brushConfig = context.GetService<BrushConfiguration>()!;
            var radMin = brushConfig.BrushSize / 2f * 0.5f;
            var radMax = brushConfig.BrushSize / 2f;
            radMax += 0.5f * (active
                ? 1 - Easing.PowerOut(0.15f).Evaluate(active.TimeSinceEvent)
                : Easing.PowerInOut(0.5f).Evaluate(active.TimeSinceEvent)
            );
            if (!active) reticleAngle += Time.deltaTime;
            brushConfig.DrawBrush(scene, landscapeData, context, pos, radMin, radMax, reticleAngle);
        }

        public virtual void SetActive(bool active) {
            Active = active;
        }
    }
    public struct TileIterator {
        public LandscapeData.SizingData Sizing;
        public Int2 RangeMin;
        public Int2 RangeMax;
        public Vector3 Position;
        public float Range;
        public RectI RangeRect => new RectI(RangeMin.X, RangeMin.Y, RangeMax.X - RangeMin.X + 1, RangeMax.Y - RangeMin.Y + 1);
        public TileIterator(LandscapeData landscapeData, Vector3 position, float range) {
            Position = position;
            Range = range;
            Sizing = landscapeData.GetSizing();
            var halfSize = range / 2;
            var rangeCtr = Sizing.WorldToLandscape(position);
            RangeMin = Int2.Max(Int2.CeilToInt((Vector2)rangeCtr - new Vector2(halfSize)), 0);
            RangeMax = Int2.Min(Int2.FloorToInt((Vector2)rangeCtr + new Vector2(halfSize)), Sizing.Size - 1);
        }

        public float GetNormalizedDistance(Int2 terrainPos) {
            var distance = Vector2.Distance(Sizing.LandscapeToWorld(terrainPos).toxz(), Position.toxz());
            return distance / (Range / 2f);
        }

        public bool GetIsInRange(Int2 terrainPos) {
            var dst2 = Vector2.DistanceSquared(Sizing.LandscapeToWorld(terrainPos).toxz(), Position.toxz());
            var radius = (Range / 2f);
            return dst2 < radius * radius;
        }
    }
}
