﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.UI;
using Weesals.Utility;

namespace Weesals.Landscape.Editor {
    public interface IToolServiceProvider<T> {
        T Service { get; }
    }
    public struct ToolContext {
        public readonly LandscapeData LandscapeData;
        public readonly CanvasRenderable Viewport;
        public readonly Camera Camera;
        public readonly object Services;
        public T? GetService<T>() { return Services is IToolServiceProvider<T> provider ? provider.Service : default; }
        public ToolContext(LandscapeData landscapeData, CanvasRenderable viewport, Camera camera, object services) {
            LandscapeData = landscapeData;
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

        private Mesh brushMesh;

        public void DrawBrush(Scene scene, LandscapeData landscapeData, ToolContext context, Vector3 pos, float radiusMin, float radiusMax, float angle) {
            if (brushMesh == null) brushMesh = new Mesh("Brush");
            using var vertices = new PooledList<Vector3>();
            using var uvs = new PooledList<Vector2>();
            using var indices = new PooledList<ushort>();
            AppendRing(landscapeData, vertices, uvs, indices, pos, radiusMin, angle);
            AppendRing(landscapeData, vertices, uvs, indices, pos, radiusMax, angle, 16);
            brushMesh.GetPositionsV().Set(vertices);
            brushMesh.GetTexCoordsV().Set(uvs);
            brushMesh.SetIndices(indices);
            //scene
            //Graphics.DrawMesh(brushMesh, Matrix4x4.identity, BrushMaterial, 0);
        }

        private void AppendRing(LandscapeData landscapeData, PooledList<Vector3> vertices, PooledList<Vector2> uvs, PooledList<ushort> indices, Vector3 centre, float radius, float angle, int dashes = 1) {
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
        public ToolContext Context;
        public LandscapeData LandscapeData => Context.LandscapeData;
        public CanvasRenderable Viewport => Context.Viewport;
        public Camera Camera => Context.Camera;
        public T? GetService<T>() { return Context.GetService<T>(); }
        public void InitializeTool(ToolContext context) {
            Context = context;
        }

        protected BrushContext CreateBrushContext(ToolContext context) {
            return new BrushContext() {
                ToolContext = context,
                BrushConfiguration = context.GetService<BrushConfiguration>()!,
            };
        }

        protected void DrawBrush(LandscapeData landscapeData, in ToolContext context, bool _active) {
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
