using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Weesals.Editor;
using Weesals.Engine;
using Weesals.UI;

namespace Weesals.Landscape.Editor {
    public class UXLandscapePaintTool : UXBrushTool, IInteraction, IPointerDownHandler, IPointerUpHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerEventsRaw {

        [EditorSelector(nameof(AllLayers))]
        public LandscapeLayer Type;
        private LandscapeLayer[] AllLayers => Context.LandscapeData.Layers.TerrainLayers;

        [EditorField]
        public FloatCurve BrushFalloff = FloatCurve.MakeSmoothStep(1f, 0f);

        [EditorField]
        public int BrushSize {
            get {
                var brushConfig = Context.GetService<BrushConfiguration>()!;
                return brushConfig.BrushSize;
            }
            set {
                var brushConfig = Context.GetService<BrushConfiguration>()!;
                brushConfig.BrushSize = value;
            }
        }

        public override void InitializeTool(ToolContext context) {
            base.InitializeTool(context);
            Type = context.LandscapeData.Layers[0];
        }

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
        }
        public void OnDrag(PointerEvent events) { }
        public void ProcessPointer(PointerEvent events) {
            var layout = Viewport.GetComputedLayout();
            var m = layout.InverseTransformPosition2D(events.PreviousPosition) / layout.GetSize();
            var mray = Camera.ViewportToRay(m);
            var cursorPosition = mray.ProjectTo(new Plane(Vector3.UnitY, 0f));

            if (!events.IsButtonDown) {
            } else {
                var brushConfig = CreateBrushContext(Context).BrushConfiguration;
                var it = new TileIterator(LandscapeData, cursorPosition, brushConfig.BrushSize);

                var changed = LandscapeChangeEvent.MakeNone();
                // Repaint cliff texture
                PaintArea(LandscapeData, it, Type, ref changed);

                if (changed.HasChanges) LandscapeData.NotifyLandscapeChanged(changed);
            }
            DrawBrush(LandscapeData, Context, cursorPosition, events.ButtonState != 0);
        }

        public void OnEndDrag(PointerEvent events) { }


        private void PaintArea(LandscapeData landscapeData, TileIterator it, LandscapeLayer type, ref LandscapeChangeEvent changed) {
            var controlMapRaw = landscapeData.GetRawControlMap();
            byte typeId = (byte)landscapeData.Layers.FindLayerId(type);
            var rnd = new Random();
            Int2 min = new Int2(int.MaxValue), max = new Int2(int.MinValue);
            for (int y = it.RangeMin.Y; y <= it.RangeMax.Y; y++) {
                for (int x = it.RangeMin.X; x <= it.RangeMax.X; x++) {
                    var terrainPos = new Int2(x, y);
                    var amount = BrushFalloff.Evaluate(it.GetNormalizedDistance(terrainPos)) * Time.deltaTime * 50f;
                    if (rnd.NextSingle() >= amount) continue;

                    var terrainIndex = it.Sizing.ToIndex(terrainPos);

                    ref var cell = ref controlMapRaw[terrainIndex];
                    if (cell.TypeId == typeId) continue;
                    cell.TypeId = typeId;
                    controlMapRaw[terrainIndex] = cell;
                    min = Int2.Min(min, terrainPos);
                    max = Int2.Max(max, terrainPos);
                }
            }
            changed.CombineWith(new LandscapeChangeEvent(min, max, controlMap: true));
        }

    }
}
