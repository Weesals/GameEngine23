using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Game;

namespace Weesals.UI.Interaction {
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
            var m0 = layout.InverseTransformPosition2D(events.PreviousPosition) / layout.GetSize();
            var m1 = layout.InverseTransformPosition2D(events.CurrentPosition) / layout.GetSize();
            if (m0 == m1) return;
            var ray0 = camera.ViewportToRay(m0);
            var ray1 = camera.ViewportToRay(m1);
            // TODO: Project onto the coarse terrain
            var pos0 = ray0.ProjectTo(new Plane(Vector3.UnitY, 0f));
            var pos1 = ray1.ProjectTo(new Plane(Vector3.UnitY, 0f));
            var delta = pos1 - pos0;
            camera.Position -= delta;
        }
        public void OnEndDrag(PointerEvent events) {
        }
    }
}
