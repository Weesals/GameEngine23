﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.UI;

namespace Weesals.Landscape.Editor {
    public class BrushWaterTool : UXBrushTool, IInteraction, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler {

        public const float InvalidHeight = -999f;

        private float surfaceHeight = InvalidHeight;

        public float WaterHeightDelta = -1f;
        public float WaterDepth = 1f;

        public string WaterFloorTypeName = "TL_WaterFloor";
        public float PaintFloorHeight = -0.5f;
        public string WaterFringeTypeName = "TL_Sand";
        public float PaintFringeHeight = 0.3f;

        public ActivationScore GetActivation(PointerEvent events) {
            bool isBtnDown = events.HasButton(0) || events.HasButton(1);
            if (events.IsDrag && isBtnDown) return ActivationScore.Active;
            if (isBtnDown) return ActivationScore.SatisfiedAndReady;
            return ActivationScore.Potential;
        }

        public void OnPointerDown(PointerEvent events) {
            events.SetState(PointerEvent.States.Drag, true);
        }
        public void OnBeginDrag(PointerEvent events) {
            bool isBtnDown = events.HasButton(0) || events.HasButton(1);
            if (!isBtnDown) { events.Yield(); return; }
            surfaceHeight = InvalidHeight;
        }
        public void OnDrag(PointerEvent events) {
            if (events.ButtonState == 0) {
                surfaceHeight = InvalidHeight;
            } else {
                bool invert = events.GetIsButtonDown(1);
                bool alternate = false;
                bool smoothing = invert && alternate;

                var layout = Viewport.GetComputedLayout();
                var m = layout.InverseTransformPosition2D(events.PreviousPosition) / layout.GetSize();
                var mray = Camera.ViewportToRay(m);
                var cursorPosition = mray.ProjectTo(new Plane(Vector3.UnitY, 0f));

                var changed = LandscapeChangeEvent.MakeNone();

                LandscapeData.SetWaterEnabled(true);

                // Compute the target height to pull the terrain towards
                if (surfaceHeight == InvalidHeight || smoothing) {
                    var theightMap = LandscapeData.GetHeightMap();
                    var twaterMap = LandscapeData.GetWaterMap();
                    surfaceHeight = theightMap.GetHeightAtF(cursorPosition.toxz());
                    surfaceHeight = MathF.Round(surfaceHeight) + WaterHeightDelta;
                    surfaceHeight = Math.Max(surfaceHeight, (float)twaterMap.GetHeightAt(cursorPosition.toxz()) / LandscapeData.HeightScale);
                }

                var brushConfig = CreateBrushContext(Context).BrushConfiguration;
                var it = new TileIterator(LandscapeData, cursorPosition, brushConfig.BrushSize);
                var itSml = new TileIterator(LandscapeData, cursorPosition, brushConfig.BrushSize - 1);

                //Context.GetService<MapMakerUndoStack>().RecordLandscape(it.RangeRect);

                var floorHeight = !invert ? surfaceHeight - WaterDepth : surfaceHeight - WaterHeightDelta;
                UXLandscapeCliffTool.ExtrudeCliffArea(LandscapeData, itSml, floorHeight, null, ref changed);

                if (!invert) {
                    var floorId = LandscapeData.Layers.FindLayerId(WaterFloorTypeName);
                    var fringeId = LandscapeData.Layers.FindLayerId(WaterFringeTypeName);
                    var waterMap = LandscapeData.GetRawWaterMap();
                    var heightMap = LandscapeData.GetRawHeightMap();
                    var controlMap = LandscapeData.GetRawControlMap();
                    var surfaceHeightTerrain = (int)(surfaceHeight * LandscapeData.HeightScale);
                    Int2 changeMin = int.MaxValue;
                    Int2 changeMax = int.MinValue;
                    for (int y = it.RangeMin.Y; y <= it.RangeMax.Y; y++) {
                        for (int x = it.RangeMin.X; x <= it.RangeMax.X; x++) {
                            var terrainPos = new Int2(x, y);
                            var terrainIndex = it.Sizing.ToIndex(terrainPos);
                            var dstL = it.GetNormalizedDistance(terrainPos);
                            if (dstL >= 0.99f) continue;
                            waterMap[terrainIndex] = new LandscapeData.WaterCell() { Height = (short)surfaceHeightTerrain, };
                            var delta = (float)(heightMap[terrainIndex].Height - waterMap[terrainIndex].Height) / LandscapeData.HeightScale;
                            var desiredId = delta < PaintFloorHeight ? floorId : delta < PaintFringeHeight ? fringeId : -2;
                            ref var controlCell = ref controlMap[terrainIndex];
                            if (desiredId == -2 || controlCell.TypeId == desiredId) continue;
                            controlCell.TypeId = (byte)desiredId;
                            changeMin = Int2.Min(changeMin, terrainPos);
                            changeMax = Int2.Max(changeMax, terrainPos);
                        }
                    }
                    changed.CombineWith(new LandscapeChangeEvent(changeMin, changeMax, controlMap: true, waterMap: true));
                }
                BrushWaterTool.RepairWaterArea(LandscapeData, it, ref changed);

                // Update terrain dependencies
                LandscapeData.NotifyLandscapeChanged(changed);
            }
            DrawBrush(LandscapeData, Context, events.ButtonState != 0);
        }
        public void OnEndDrag(PointerEvent events) { }

        public static void RepairWaterArea(LandscapeData landscapeData, in TileIterator it, ref LandscapeChangeEvent changed) {
            if (!landscapeData.WaterEnabled) return;
            var waterMapRaw = landscapeData.GetRawWaterMap();
            var heightMapRaw = landscapeData.GetRawHeightMap();
            Int2 repairMin = int.MaxValue;
            Int2 repairMax = int.MinValue;
            for (int y = it.RangeMin.Y; y <= it.RangeMax.Y; y++) {
                for (int x = it.RangeMin.X; x <= it.RangeMax.X; x++) {
                    var terrainPos = new Int2(x, y);

                    if (!it.GetIsInRange(terrainPos)) continue;

                    ref var waterHeight = ref waterMapRaw[it.Sizing.ToIndex(terrainPos)];
                    if (waterHeight.IsInvalid) continue;

                    var minHeight = short.MaxValue;
                    for (int dy = -1; dy <= 1; dy++) {
                        for (int dx = -1; dx <= 1; dx++) {
                            var dpnt = terrainPos + new Int2(dx, dy);
                            if (!it.Sizing.IsInBounds(dpnt)) continue;
                            minHeight = (short)Math.Min(minHeight, heightMapRaw[it.Sizing.ToIndex(dpnt)].Height);
                        }
                    }

                    if (minHeight > waterHeight.Height) {
                        waterHeight.Data = 0;
                        repairMin = Int2.Min(repairMin, terrainPos);
                        repairMax = Int2.Max(repairMax, terrainPos);
                    }

                }
            }
            changed.CombineWith(new LandscapeChangeEvent(repairMin, repairMax, waterMap: true));
        }

    }
}
