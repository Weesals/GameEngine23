using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Game5.Game;
using Weesals.Engine;
using Weesals.UI;

namespace Game5.UI.Interaction {
    public class UXCameraControls : IInteraction, IBeginDragHandler, IDragHandler, IEndDragHandler {
        public readonly UIPlay PlayUI;
        private TimedEvent<Vector2> rubberband;

        public UXCameraControls(UIPlay playUI) {
            PlayUI = playUI;
        }

        public ActivationScore GetActivation(PointerEvent events) {
            if (PlayUI.Play.Camera == null) return ActivationScore.None;
            if (events.GetIsButtonDown(1) && events.IsDrag) return ActivationScore.Active;
            return ActivationScore.Potential;
        }

        public void OnBeginDrag(PointerEvent events) {
            if (!events.GetIsButtonDown(1)) { events.Yield(); return; }
            rubberband.Clear();
        }
        public void OnDrag(PointerEvent events) {
            var camera = PlayUI.Play.Camera;
            var layout = PlayUI.GetComputedLayout();
            if (Input.GetKeyDown(KeyCode.LeftControl)) {
                var size = layout.GetSize();
                var delta = layout.InverseTransformPosition2D(events.CurrentPosition)
                    - layout.InverseTransformPosition2D(events.PreviousPosition);
                delta /= Math.Max(size.X, size.Y);
                camera.Orientation =
                    Quaternion.CreateFromAxisAngle(Vector3.UnitY, delta.X * 3.0f)
                    * camera.Orientation
                    * Quaternion.CreateFromAxisAngle(Vector3.UnitX, delta.Y * 3.0f)
                    ;
            } else {
                var m0 = layout.InverseTransformPosition2D(events.PreviousPosition) / layout.GetSize();
                var m1 = layout.InverseTransformPosition2D(events.CurrentPosition) / layout.GetSize();
                if (m0 == m1) return;
                var ray0 = camera.ViewportToRay(m0).Normalize();
                var ray1 = camera.ViewportToRay(m1).Normalize();
                var dst0 = ray0.GetDistanceTo(new Plane(Vector3.UnitY, 0f));
                var dst1 = ray1.GetDistanceTo(new Plane(Vector3.UnitY, 0f));
                dst0 = LimitDst(dst0);
                dst1 = LimitDst(dst1);
                // TODO: Project onto the coarse terrain
                var pos0 = ray0.GetPoint(dst0);
                var pos1 = ray1.GetPoint(dst1);
                var delta = pos1 - pos0;
                delta.Y = 0f;
                camera.Position -= delta;
            }
        }
        private static float LimitDst(float d) {
            const float CritDst = 1000.0f;
            if (d < 0f) return CritDst;
            else if (d > CritDst) return CritDst;// + MathF.Sqrt(d - CritDst);
            return d;
        }
        public void OnEndDrag(PointerEvent events) {
        }
    }
}
