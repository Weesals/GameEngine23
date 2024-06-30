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
            if (events.GetIsButtonDown(0) && events.HasModifier(Modifiers.Alt)) return ActivationScore.Active;
            return ActivationScore.Potential;
        }

        public void OnBeginDrag(PointerEvent events) {
            if (events.GetIsButtonDown(1)) {
                // Right-click pan
            } else if (events.GetIsButtonDown(0) && events.HasModifier(Modifiers.Alt)) {
                // Left-click alt drag
            } else {
                events.Yield();
                return;
            }
            rubberband.Clear();
        }
        public void OnDrag(PointerEvent events) {
            var camera = PlayUI.Play.Camera;
            var layout = PlayUI.GetComputedLayout();
            var m0 = layout.InverseTransformPosition2D(events.PreviousPosition);
            var m1 = layout.InverseTransformPosition2D(events.CurrentPosition);
            var size = layout.GetSize();
            if (m0 == m1) return;
            if (events.HasModifier(Modifiers.Alt)) {
                const float OrbitDistance = 30f;
                camera.Position += camera.Forward * OrbitDistance;
                var delta = (m1 - m0) / Math.Max(size.X, size.Y);
                camera.Orientation =
                    Quaternion.CreateFromAxisAngle(Vector3.UnitY, delta.X * 3.0f)
                    * camera.Orientation
                    * Quaternion.CreateFromAxisAngle(Vector3.UnitX, delta.Y * 3.0f)
                    ;
                camera.Position -= camera.Forward * OrbitDistance;
            } else if (events.HasModifier(Modifiers.Control)) {
                var delta = (m1 - m0) / Math.Max(size.X, size.Y);
                camera.Orientation =
                    Quaternion.CreateFromAxisAngle(Vector3.UnitY, delta.X * 3.0f)
                    * camera.Orientation
                    * Quaternion.CreateFromAxisAngle(Vector3.UnitX, delta.Y * 3.0f)
                    ;
            } else {
                var ray0 = camera.ViewportToRay(m0 / size).Normalize();
                var ray1 = camera.ViewportToRay(m1 / size).Normalize();
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
