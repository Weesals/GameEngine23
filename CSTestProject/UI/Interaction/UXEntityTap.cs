﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Game;
using Weesals.Game.Gameplay;

namespace Weesals.UI.Interaction {
    public class UXEntityTap : IInteraction, IPointerClickHandler {
        public readonly UIPlay PlayUI;
        public Play Play => PlayUI.Play;

        public UXEntityTap(UIPlay playUI) {
            PlayUI = playUI;
        }

        public ActivationScore GetActivation(PointerEvent events) {
            //if (!CanInteract(events)) return ActivationScore.None;
            if (events.HasButton(0)) return ActivationScore.Satisfied;
            return ActivationScore.Potential;
        }
        public bool CanInteract(PointerEvent events) {
            var target = FindTarget(events);
            return target.IsValid;
        }
        private GenericTarget FindTarget(PointerEvent events) {
            var layout = PlayUI.GetComputedLayout();
            var m = layout.InverseTransformPosition2D(events.PreviousPosition) / layout.GetSize();
            var mray = PlayUI.Play.Camera.ViewportToRay(m);
            return PlayUI.Play.HitTest(mray);
        }
        public void OnPointerClick(PointerEvent events) {
            var entity = FindTarget(events);
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
