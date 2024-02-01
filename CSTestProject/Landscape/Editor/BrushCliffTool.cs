using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Weesals.Engine;
using Weesals.Game;
using Weesals.UI;

namespace Weesals.Landscape.Editor {
    public class UXLandscapeCliffTool : UXBrushTool, IInteraction, IPointerDownHandler, IPointerUpHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerEventsRaw {

        public const float InvalidHeight = -999f;

        public struct CliffTypeData {
            public string SurfaceType;
            public string EdgeType;
        }

        private float surfaceHeight = InvalidHeight;
        public float HeightDelta = 2f;

        public CliffTypeData Type = new CliffTypeData() { SurfaceType = "TL_Moss", EdgeType = "TL_Cliff", };
        public FloatCurve HeightCurve = new FloatCurve(new FloatKeyframe(0f, 0f), new FloatKeyframe(0.5f, 1f));
        public FloatCurve SmoothHeightCurve = FloatCurve.MakeSmoothStep();

        public ActivationScore GetActivation(PointerEvent events) {
            bool isBtnDown = events.HasButton(0) || events.HasButton(1);
            if (events.IsDrag && isBtnDown) return ActivationScore.Active;
            if (isBtnDown) return ActivationScore.SatisfiedAndReady;
            return ActivationScore.Potential;
        }
        public void OnPointerDown(PointerEvent events) {
            events.SetState(PointerEvent.States.Drag, true);
        }
        public void OnPointerUp(PointerEvent events) {
            events.Cancel(this);
        }
        public void OnBeginDrag(PointerEvent events) {
            bool isBtnDown = events.HasButton(0) || events.HasButton(1);
            if (!isBtnDown) { events.Yield(); return; }
            surfaceHeight = InvalidHeight;
        }
        public void OnDrag(PointerEvent events) { }
        public void ProcessPointer(PointerEvent events) {
            var layout = Viewport.GetComputedLayout();
            var m = layout.InverseTransformPosition2D(events.PreviousPosition) / layout.GetSize();
            var mray = Camera.ViewportToRay(m);
            var cursorPosition = mray.ProjectTo(new Plane(Vector3.UnitY, 0f));

            if (!events.IsButtonDown) {
                surfaceHeight = InvalidHeight;
            } else {
                bool invert = events.GetIsButtonDown(1);
                bool alternate = false;

                bool smoothing = alternate && invert;
                // Compute the target height to pull the terrain towards
                if (surfaceHeight == InvalidHeight || smoothing) {
                    var heightMap = LandscapeData.GetHeightMap();
                    surfaceHeight = heightMap.GetHeightAtF(cursorPosition.toxz());
                    if (!alternate)
                        surfaceHeight += HeightDelta * (invert ? -1 : 1);
                    surfaceHeight = MathF.Round(surfaceHeight);
                }

                var brushConfig = CreateBrushContext(Context).BrushConfiguration;
                var it = new TileIterator(LandscapeData, cursorPosition, brushConfig.BrushSize);

                var changed = LandscapeChangeEvent.MakeNone();
                // Apply height action
                if (smoothing) SmoothCliffArea(LandscapeData, it, SmoothHeightCurve, ref changed);
                else ExtrudeCliffArea(LandscapeData, it, surfaceHeight, HeightCurve, ref changed);
                // Repaint cliff texture
                PaintCliffArea(LandscapeData, it, Type, ref changed);

                BrushWaterTool.RepairWaterArea(LandscapeData, it, ref changed);

                ExtrudeCliffArea(LandscapeData, it, surfaceHeight, HeightCurve, ref changed);
                if (changed.HasChanges) LandscapeData.NotifyLandscapeChanged(changed);
            }
            DrawBrush(LandscapeData, Context, cursorPosition, events.ButtonState != 0);
        }
        public void OnEndDrag(PointerEvent events) { }

        public static void SmoothCliffArea(LandscapeData landscapeData, in TileIterator it, FloatCurve intensityCurve, ref LandscapeChangeEvent changed) {
            var heightMapRaw = landscapeData.GetRawHeightMap();
            var rnd = new Random();
            Int2 min = new Int2(int.MaxValue), max = new Int2(int.MinValue);
            for (int y = it.RangeMin.Y; y <= it.RangeMax.Y; y++) {
                for (int x = it.RangeMin.X; x <= it.RangeMax.X; x++) {
                    var terrainPos = new Int2(x, y);

                    var dstL = it.GetNormalizedDistance(terrainPos);
                    if (dstL > 0.99f) continue;

                    int heightSum = 0, heightCount = 0;
                    for (int dy = -1; dy <= 1; dy++) {
                        for (int dx = -1; dx <= 1; dx++) {
                            var dpnt = terrainPos + new Int2(dx, dy);
                            if (!it.Sizing.IsInBounds(dpnt)) continue;
                            heightSum += heightMapRaw[it.Sizing.ToIndex(dpnt)].Height;
                            heightCount++;
                        }
                    }

                    var heightAve = (float)heightSum / Math.Max(1, heightCount);

                    var amount = intensityCurve.Evaluate(dstL) * Math.Clamp(Time.deltaTime * 50f, 0f, 1f);

                    var terrainIndex = it.Sizing.ToIndex(terrainPos);
                    var cell = heightMapRaw[terrainIndex];
                    float delta = cell.Height - heightAve;
                    delta = Easing.Lerp(delta, 0, amount);
                    delta += (delta < 0 ? -1f : 1f) * (float)rnd.NextDouble();
                    var newHeight = (short)(heightAve + delta);
                    if (cell.Height == newHeight) continue;
                    cell.Height = newHeight;
                    heightMapRaw[terrainIndex] = cell;
                    min = Int2.Min(min, new Int2(x, y));
                    max = Int2.Max(max, new Int2(x, y));
                }
            }
            changed.CombineWith(new LandscapeChangeEvent(min, max, heightMap: true));
        }
        public static void ExtrudeCliffArea(LandscapeData landscapeData, in TileIterator it, float surfaceHeight, FloatCurve? heightCurve, ref LandscapeChangeEvent changed) {
            var heightMapRaw = landscapeData.GetRawHeightMap();
            var surfaceHeightTerrain = (int)(surfaceHeight * LandscapeData.HeightScale);
            var rnd = new Random();
            Int2 min = new Int2(int.MaxValue), max = new Int2(int.MinValue);
            for (int y = it.RangeMin.Y; y <= it.RangeMax.Y; y++) {
                for (int x = it.RangeMin.X; x <= it.RangeMax.X; x++) {
                    var terrainPos = new Int2(x, y);

                    var dstL = it.GetNormalizedDistance(terrainPos);
                    if (dstL > 0.99f) continue;

                    var threshold = dstL;
                    if (heightCurve != null) threshold = (1f - heightCurve.Evaluate(1f - threshold));
                    threshold *= Math.Abs(GetFringeHeight(landscapeData, it, terrainPos) - surfaceHeightTerrain);

                    var terrainIndex = it.Sizing.ToIndex(terrainPos);
                    var cell = heightMapRaw[terrainIndex];
                    var delta = cell.Height - surfaceHeightTerrain;
                    if (Math.Abs(delta) < threshold) continue;
                    threshold = Easing.MoveTowards(
                        Easing.Lerp(Math.Abs(delta), threshold, Math.Clamp(Time.deltaTime * 10f, 0f, 1f)),
                        threshold,
                        (float)rnd.NextDouble()
                    );
                    var newHeight = (short)MathF.Round(surfaceHeightTerrain + (delta < 0 ? -threshold : threshold));
                    if (cell.Height == newHeight) continue;
                    cell.Height = newHeight;
                    heightMapRaw[terrainIndex] = cell;
                    min = Int2.Min(min, new Int2(x, y));
                    max = Int2.Max(max, new Int2(x, y));
                }
            }
            changed.CombineWith(new LandscapeChangeEvent(min, max, heightMap: true));
        }
        private void PaintCliffArea(LandscapeData landscapeData, in TileIterator it, CliffTypeData type, ref LandscapeChangeEvent changed) {
            if (landscapeData.Layers == null) return;
            var surfaceTypeId = landscapeData.Layers.FindLayerId(type.SurfaceType);
            var edgeTypeId = landscapeData.Layers.FindLayerId(type.EdgeType);
            var heightMap = landscapeData.GetHeightMap();
            var controlMapRaw = landscapeData.GetRawControlMap();
            var heightMapRaw = landscapeData.GetRawHeightMap();
            var surfaceHeightTerrain = (int)(surfaceHeight * LandscapeData.HeightScale);
            Int2 min = new Int2(int.MaxValue), max = new Int2(int.MinValue);
            for (int y = it.RangeMin.Y; y <= it.RangeMax.Y; y++) {
                for (int x = it.RangeMin.X; x <= it.RangeMax.X; x++) {
                    var terrainPos = new Int2(x, y);
                    var terrainIndex = it.Sizing.ToIndex(terrainPos);
                    var dstL = it.GetNormalizedDistance(terrainPos);
                    if (dstL >= 0.99f) continue;

                    var dd = heightMap.GetDerivative(terrainPos);
                    var isCliff = Int2.Dot(dd, dd) * dstL > LandscapeData.HeightScale * 50 * HeightDelta;
                    var typeId = (byte)(isCliff ? edgeTypeId : surfaceTypeId);

                    var hcell = heightMapRaw[terrainIndex];
                    if (Math.Abs(hcell.Height - surfaceHeightTerrain) > LandscapeData.HeightScale && !isCliff) continue;

                    var cell = controlMapRaw[terrainIndex];
                    if (cell.TypeId == typeId) continue;
                    cell.TypeId = typeId;
                    controlMapRaw[terrainIndex] = cell;
                    min = Int2.Min(min, new Int2(x, y));
                    max = Int2.Max(max, new Int2(x, y));
                }
            }
            changed.CombineWith(new LandscapeChangeEvent(min, max, controlMap: true));
        }
        private static short GetFringeHeight(LandscapeData landscapeData, in TileIterator it, Int2 terrainPos) {
            var heightMapRaw = landscapeData.GetRawHeightMap();
            var ctrPnt = it.Sizing.WorldToLandscape(it.Position);
            var delta = terrainPos - ctrPnt;
            var endPos = Int2.RoundToInt((Vector2)delta * (it.Range / 2f / Math.Max(0.001f, delta.Length)));
            endPos += (Math.Abs(endPos.X) > Math.Abs(endPos.Y) ? new Int2(endPos.X < 0 ? -1 : 1, 0) : new Int2(0, endPos.Y < 0 ? -1 : 1));
            endPos += ctrPnt;
            if (!it.Sizing.IsInBounds(endPos)) endPos = terrainPos;
            var endIndex = it.Sizing.ToIndex(endPos);
            var endCell = heightMapRaw[endIndex];
            return endCell.Height;
        }

    }
}
