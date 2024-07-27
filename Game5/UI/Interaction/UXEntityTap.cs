using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Game5.Game;
using Game5.Game.Gameplay;
using Weesals.Engine;
using Weesals.UI;

namespace Game5.UI.Interaction {
    public class UXEntityTap : IInteraction, IPointerClickHandler {
        public readonly UIPlay PlayUI;
        public Play Play => PlayUI.Play;

        public UXEntityTap(UIPlay playUI) {
            PlayUI = playUI;
        }

        public ActivationScore GetActivation(PointerEvent events) {
            //if (!CanInteract(events)) return ActivationScore.None;
            // Need WasDrag because drag might have just ended, and we still need to not use self
            if (events.HasButton(0) && !events.WasDrag) return ActivationScore.Satisfied;
            return ActivationScore.Potential;
        }
        public bool CanInteract(PointerEvent events) {
            var target = FindTarget(events);
            return target.IsValid;
        }
        private ItemReference FindTarget(PointerEvent events) {
            var layout = PlayUI.GetComputedLayout();
            var m = layout.InverseTransformPosition2D(events.PreviousPosition) / layout.GetSize();
            var mray = PlayUI.Play.Camera.ViewportToRay(m);
            return PlayUI.Play.HitTest(mray);
        }
        public void OnPointerClick(PointerEvent events) {
            var entity = FindTarget(events);
            //if (!Play.World.IsValid(entity.GetEntity())) entity = default;
            if (!entity.IsValid) {
                Play.SelectionManager.ClearSelected();
                events.Yield();
                return;
            }
            Play.SelectionManager.SetSelected(entity);
            //events.Cancel(this);
        }
    }
}
